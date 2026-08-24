using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KongBall
{
    // Shared plumbing for the screens this game builds in code rather than authoring in the scene
    // (ConnectingScreen, MainMenu). It exists because the two had grown identical copies of the same
    // four helpers, including the built-in-font lookup — and a font lookup that behaves differently
    // in two places is the kind of thing that gets found on a device and not before.
    //
    // Also carries the KONGBALL_UI_VISUAL_BIBLE design system: the four world colours and the
    // rounded, outlined, "toy-like" button/panel construction it asks for. Foundations only — no
    // existing screen calls ToyButton/Panel yet, that is deliberately a separate pass per screen.
    public static class Ui
    {
        // The world's four colours (plus the light cream the bible's own background uses). Every
        // screen should read as one of these, or a tint of one — the bible's DON'T list opens with
        // "change palette from one screen to another".
        public static readonly Color JungleGreen = new Color32(0x48, 0xBE, 0x55, 0xFF);
        public static readonly Color JungleGreenDark = new Color32(0x17, 0x6B, 0x2A, 0xFF);
        public static readonly Color Yellow = new Color32(0xFF, 0xD8, 0x3D, 0xFF);
        public static readonly Color WoodBrown = new Color32(0x8B, 0x54, 0x2F, 0xFF);
        public static readonly Color Ink = new Color32(0x18, 0x30, 0x1D, 0xFF);
        public static readonly Color Cream = new Color32(0xFF, 0xF4, 0xD6, 0xFF);

        public static Image NewImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<Image>();
        }

        // Returns null when no built-in font is available, and every caller is expected to cope: a
        // screen without text is poor, a screen that throws is worse.
        public static Text NewText(string name, Transform parent, int size)
        {
            var font = Font();
            if (font == null) return null;
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static Font _font;
        static bool _resolved;

        // Titan One (SIL OFL 1.1, Google Fonts) — bold and rounded, per the bible's "heavy, cartoon,
        // strong presence" headline rule; shipped under Resources so it loads with no Editor asset
        // reference to wire up. Falls back to the old system-font lookup if it's ever missing, same
        // as before: a screen with the wrong font is fine, a screen that throws is not.
        public static Font Font()
        {
            if (_resolved) return _font;
            _resolved = true;
            _font = Resources.Load<Font>("Fonts/TitanOne-Regular");
            if (_font != null) return _font;
            foreach (var n in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
            {
                try { _font = Resources.GetBuiltinResource<Font>(n); } catch { _font = null; }
                if (_font != null) break;
            }
            return _font;
        }

        public static Canvas NewOverlayCanvas(GameObject go, int sortingOrder)
        {
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        // Anchored to the bottom edge instead of the middle. Anything that must keep clear of the
        // character belongs here: the character is placed as a fraction of the screen height, so on a
        // taller screen it reaches further down in canvas units and would meet a fixed-position button
        // coming the other way.
        public static void PlaceFromBottom(RectTransform rt, float x, float up, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, up);
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // --- Design system: rounded shapes, toy buttons, panels --------------------------------
        // Not visually checked on a device yet — the mask math below is reasoned through, not
        // measured (see AGENTS.md #10). Treat sizes/ratios as a first pass to look at on a phone.

        static readonly Dictionary<int, Sprite> _roundedRectCache = new Dictionary<int, Sprite>();

        // A white, 9-sliced rounded-rectangle sprite with the given corner radius in pixels — every
        // button and panel shares this instead of a hand-drawn asset, since nobody here can open an
        // image editor bound to the project any more than they can open Unity's. Cached by radius.
        public static Sprite RoundedRectSprite(int radiusPx)
        {
            radiusPx = Mathf.Max(1, radiusPx);
            if (_roundedRectCache.TryGetValue(radiusPx, out var cached) && cached != null) return cached;

            int size = radiusPx * 2 + 2; // one radius per corner, plus a slice of flat centre
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            float r = radiusPx;
            for (int y = 0; y < size; y++)
            {
                bool yCorner = y < r || y >= size - r;
                float cy = y < r ? r : size - r;
                for (int x = 0; x < size; x++)
                {
                    bool xCorner = x < r || x >= size - r;
                    bool inside;
                    if (xCorner && yCorner)
                    {
                        float cx = x < r ? r : size - r;
                        float dx = (x + 0.5f) - cx;
                        float dy = (y + 0.5f) - cy;
                        inside = dx * dx + dy * dy <= r * r;
                    }
                    else inside = true; // flat edge strip or centre: never clipped by a corner arc
                    pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            _roundedRectCache[radiusPx] = sprite;
            return sprite;
        }

        static Image NewSlicedImage(string name, Transform parent, Sprite sprite)
        {
            var img = NewImage(name, parent);
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            return img;
        }

        // A "toy" button per the bible: thick dark outline, a lower depth edge that stays visible
        // under the surface, a main colour, and a lighter top highlight — instead of a flat
        // rectangle. Returns the ROOT: full size, invisible, and the thing a caller Places, sizes
        // and hangs a Button/pointer handler on, so a press-scale animation moves the whole
        // composite as one object instead of only one of its layers.
        public static Image ToyButton(string name, Transform parent, float w, float h, Color mainColor)
        {
            float radius = h * 0.32f;
            float outline = Mathf.Max(3f, h * 0.05f);
            float depth = Mathf.Max(4f, h * 0.09f);
            var sprite = RoundedRectSprite(Mathf.RoundToInt(radius));

            var hit = NewImage(name, parent);
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.rectTransform.sizeDelta = new Vector2(w, h);

            var depthImg = NewSlicedImage("Depth", hit.transform, sprite);
            Place(depthImg.rectTransform, 0f, -depth * 0.5f, w, h);
            depthImg.color = Color.Lerp(mainColor, Ink, 0.55f);
            depthImg.raycastTarget = false;

            var outlineImg = NewSlicedImage("Outline", hit.transform, sprite);
            Place(outlineImg.rectTransform, 0f, depth * 0.5f, w, h);
            outlineImg.color = Ink;
            outlineImg.raycastTarget = false;

            var surfaceImg = NewSlicedImage("Surface", hit.transform, sprite);
            Place(surfaceImg.rectTransform, 0f, depth * 0.5f, w - outline * 2f, h - outline * 2f);
            surfaceImg.color = mainColor;
            surfaceImg.raycastTarget = false;

            var hl = NewSlicedImage("Highlight", surfaceImg.transform, RoundedRectSprite(Mathf.RoundToInt(radius * 0.6f)));
            var hlRt = hl.rectTransform;
            hlRt.anchorMin = new Vector2(0.5f, 1f);
            hlRt.anchorMax = new Vector2(0.5f, 1f);
            hlRt.pivot = new Vector2(0.5f, 1f);
            hlRt.sizeDelta = new Vector2(Mathf.Max(0f, w - outline * 4f), Mathf.Max(0f, (h - outline * 2f) * 0.42f));
            hlRt.anchoredPosition = new Vector2(0f, -outline);
            hl.color = new Color(1f, 1f, 1f, 0.30f);
            hl.raycastTarget = false;

            hit.gameObject.AddComponent<ToyButtonFeedback>();

            return hit;
        }

        // A rounded, outlined container — the "chunky panel" language, never a thin card with a
        // hairline border. Lighter than ToyButton on purpose: a container communicates grouping,
        // not "press me", so it skips the depth peek and the highlight.
        public static Image Panel(string name, Transform parent, float w, float h, Color fillColor)
        {
            float radius = Mathf.Min(w, h) * 0.14f;
            float outline = Mathf.Max(3f, h * 0.035f);
            var sprite = RoundedRectSprite(Mathf.RoundToInt(radius));

            var outlineImg = NewSlicedImage(name, parent, sprite);
            outlineImg.rectTransform.sizeDelta = new Vector2(w, h);
            outlineImg.color = Ink;

            var fill = NewSlicedImage("Fill", outlineImg.transform, sprite);
            Place(fill.rectTransform, 0f, 0f, w - outline * 2f, h - outline * 2f);
            fill.color = fillColor;

            return outlineImg;
        }
    }
}
