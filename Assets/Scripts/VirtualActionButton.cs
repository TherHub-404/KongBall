using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KongBall
{
    // On-screen action button. Reports HELD state (tap/hold) to LocalInputSource — that's the whole
    // input surface now: THE BALL IS NEVER POSSESSED, so there is no carry window left to drag-aim a
    // shot during, and the old IDragHandler/aim-delta wiring went with it (see LocalInputSource).
    //
    // Also owns the button's own presentation: a KICK/PUSH label and a cooldown ring, both built here
    // at runtime rather than authored on the prefab (see Ui.cs — everything on screen in this game is
    // code, not scene authoring). The scene still carries the button's round background Image and its
    // old static icon child, now disabled: a fixed icon could never say which of the two contextual
    // actions is actually available, and text does.
    public class VirtualActionButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public LocalInputSource target;

        Text _label;
        Image _cooldownRing;

        void Awake()
        {
            BuildPresentation();
        }

        void BuildPresentation()
        {
            // The ring first, the label second, so the label draws on top of it.
            var baseImage = GetComponent<Image>();
            var myRt = (RectTransform)transform;

            var ringGo = new GameObject("CooldownRing", typeof(RectTransform));
            ringGo.transform.SetParent(transform, false);
            var ringRt = (RectTransform)ringGo.transform;
            ringRt.anchorMin = ringRt.anchorMax = new Vector2(0.5f, 0.5f);
            ringRt.pivot = new Vector2(0.5f, 0.5f);
            ringRt.sizeDelta = myRt.sizeDelta + new Vector2(28f, 28f); // a ring AROUND the button
            _cooldownRing = ringGo.AddComponent<Image>();
            // Reuses the button's own sprite so the ring matches its round shape exactly, instead of
            // guessing the name of a Unity built-in sprite.
            _cooldownRing.sprite = baseImage != null ? baseImage.sprite : null;
            _cooldownRing.type = Image.Type.Filled;
            _cooldownRing.fillMethod = Image.FillMethod.Radial360;
            _cooldownRing.fillOrigin = (int)Image.Origin360.Top;
            _cooldownRing.fillClockwise = true;
            _cooldownRing.color = new Color(0f, 0f, 0f, 0.55f);
            _cooldownRing.raycastTarget = false;
            _cooldownRing.enabled = false;

            _label = Ui.NewText("ActionLabel", transform, 34);
            if (_label != null)
            {
                Ui.Stretch(_label.rectTransform);
                _label.color = Color.black;
                _label.raycastTarget = false;
                // Kept readable once the dark cooldown ring starts covering the button underneath it.
                var outline = _label.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(1f, 1f, 1f, 0.8f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }
        }

        // Not verified on a phone yet: whether the ring reads as "around the button" rather than "on
        // top of it" at this size depends on how big the button looks on an actual screen — same
        // caveat as when this was first built, still true, still unverified.
        void Update()
        {
            var p = NetPlayer.Local;
            if (p == null)
            {
                if (_label != null) _label.text = "";
                if (_cooldownRing != null) _cooldownRing.enabled = false;
                return;
            }

            // THE BALL IS NEVER POSSESSED: this is the same range check HandleBall uses to decide
            // hit vs push, read here only for what the button should say.
            bool hitReady = p.BallInHitRange;
            if (_label != null) _label.text = hitReady ? "KICK" : "PUSH";

            float frac = hitReady ? p.HitCooldown01 : p.PushCooldown01;
            if (_cooldownRing != null)
            {
                _cooldownRing.enabled = frac > 0.001f;
                _cooldownRing.fillAmount = frac;
            }
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (target != null) target.SetTouchActionHeld(true);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (target != null) target.SetTouchActionHeld(false);
        }
    }
}
