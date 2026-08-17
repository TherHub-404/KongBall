using UnityEngine;
using UnityEngine.UI;

namespace KongBall
{
    // Shared plumbing for the screens this game builds in code rather than authoring in the scene
    // (ConnectingScreen, MainMenu). It exists because the two had grown identical copies of the same
    // four helpers, including the built-in-font lookup — and a font lookup that behaves differently
    // in two places is the kind of thing that gets found on a device and not before.
    public static class Ui
    {
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

        public static Font Font()
        {
            if (_resolved) return _font;
            _resolved = true;
            // Unity renamed the built-in font; try both names rather than assuming a version.
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

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
