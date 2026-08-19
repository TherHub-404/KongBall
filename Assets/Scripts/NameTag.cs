using UnityEngine;
using UnityEngine.UI;

namespace KongBall
{
    // A label floating above a player, in the world, turned toward the camera.
    //
    // Debug scaffolding: it exists so bots can be told apart from people at a glance while their
    // behaviour is being judged, and it is meant to come out again. Two places to delete: this file,
    // and NetPlayer.UpdateNameTag with its call in Render.
    //
    // A world-space Canvas rather than Unity's 3D TextMesh, on purpose. TextMesh draws through the
    // legacy GUI/Text shader — precisely the kind of built-in shader URP leaves out of a player build,
    // which is what put a magenta pitch on the device once already. UI Text draws with the same shader
    // as the HUD, which we know renders on the phone.
    public class NameTag : MonoBehaviour
    {
        const float Height = 1.4f;       // metres above the player's pivot: the capsule's head is at 0.95
        const float WorldScale = 0.01f;  // canvas units -> metres
        const float Width = 400f;        // canvas units
        const float Lines = 100f;
        const int FontSize = 40;         // ~0.4 m tall, which is ~30 px at this game's camera distance

        Text _text;

        // Returns null when there is no built-in font to draw with — a missing debug label is not
        // worth a null reference in the middle of a match.
        public static NameTag Attach(Transform parent, string label)
        {
            var go = new GameObject("NameTag");
            go.transform.SetParent(parent, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var crt = (RectTransform)go.transform;
            crt.sizeDelta = new Vector2(Width, Lines);
            crt.localPosition = new Vector3(0f, Height, 0f);
            crt.localScale = Vector3.one * WorldScale;

            var text = Ui.NewText("Text", go.transform, FontSize);
            if (text == null) { Destroy(go); return null; }
            Ui.Stretch(text.rectTransform);
            text.color = Color.white;
            // Readable over grass, sand, a red shirt or a blue one without picking a colour per case.
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, 2f);

            var tag = go.AddComponent<NameTag>();
            tag._text = text;
            tag.Set(label);
            return tag;
        }

        public void Set(string label)
        {
            if (_text != null) _text.text = label;
        }

        void LateUpdate()
        {
            // The camera's own rotation, not a look-at: it keeps the label screen-aligned and upright
            // whatever the orbit camera is doing, and it overrides the yaw inherited from the player.
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }
    }
}
