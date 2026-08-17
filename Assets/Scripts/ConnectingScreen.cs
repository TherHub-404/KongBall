using System;
using UnityEngine;
using UnityEngine.UI;

namespace KongBall
{
    // Full-screen overlay covering the gap between "team chosen" and "my player is on the pitch".
    // Joining a Photon session takes a couple of seconds, and without this the game just sat on a
    // frozen view of the empty field with no sign it was doing anything.
    //
    // Built entirely in code — like the aim line in NetPlayer and the clips in SfxManager — so it
    // needs no scene authoring and cannot drift out of sync with the prefab.
    public class ConnectingScreen : MonoBehaviour
    {
        const int SortingOrder = 5000;
        static readonly Color Backdrop = new Color(0.05f, 0.07f, 0.06f, 0.92f);
        static readonly Color Accent = new Color(0.35f, 0.85f, 0.65f);

        Text _label;
        Image[] _dots;
        GameObject _retry;
        float _fade = 1f;
        CanvasGroup _group;
        bool _hiding;

        public static ConnectingScreen Show(string message)
        {
            var go = new GameObject("ConnectingScreen");
            DontDestroyOnLoad(go);
            var screen = go.AddComponent<ConnectingScreen>();
            screen.Build();
            screen.SetMessage(message);
            return screen;
        }

        void Build()
        {
            Ui.NewOverlayCanvas(gameObject, SortingOrder);
            _group = gameObject.AddComponent<CanvasGroup>();

            // Backdrop: also swallows taps so the HUD underneath can't be poked while connecting.
            var bg = Ui.NewImage("Backdrop", transform);
            bg.color = Backdrop;
            Ui.Stretch(bg.rectTransform);

            _label = Ui.NewText("Label", transform, 44);
            if (_label != null)
            {
                var rt = _label.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(900f, 120f);
                rt.anchoredPosition = new Vector2(0f, 40f);
            }

            _dots = new Image[3];
            for (int i = 0; i < _dots.Length; i++)
            {
                var d = Ui.NewImage("Dot" + i, transform);
                d.color = Accent;
                var rt = d.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(18f, 18f);
                rt.anchoredPosition = new Vector2((i - 1) * 36f, -40f);
                _dots[i] = d;
            }
        }

        public void SetMessage(string message)
        {
            if (_label != null) _label.text = message;
        }

        // Connection failed: say so and offer a way out, instead of leaving a dead screen.
        public void ShowError(string message, Action onRetry)
        {
            SetMessage(message);
            foreach (var d in _dots) if (d != null) d.enabled = false;
            if (_retry != null) Destroy(_retry);

            _retry = new GameObject("Retry", typeof(RectTransform));
            _retry.transform.SetParent(transform, false);
            var img = _retry.AddComponent<Image>();
            img.color = Accent;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(320f, 96f);
            rt.anchoredPosition = new Vector2(0f, -90f);

            var btn = _retry.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => { Destroy(gameObject); onRetry?.Invoke(); });

            var t = Ui.NewText("Text", _retry.transform, 36);
            if (t != null)
            {
                t.text = "RIPROVA";
                t.color = new Color(0.05f, 0.07f, 0.06f);
                Ui.Stretch(t.rectTransform);
            }
        }

        public void Hide()
        {
            if (_hiding) return;
            _hiding = true;
        }

        void Update()
        {
            // Pulse the dots left to right so the screen visibly reads as "working", not "stuck".
            if (!_hiding && _dots != null)
            {
                for (int i = 0; i < _dots.Length; i++)
                {
                    if (_dots[i] == null) continue;
                    float p = Mathf.Sin((Time.unscaledTime * 4f) - i * 0.6f) * 0.5f + 0.5f;
                    _dots[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.25f, p);
                    var c = Accent; c.a = Mathf.Lerp(0.35f, 1f, p);
                    _dots[i].color = c;
                }
            }

            // A short fade rather than a hard cut, which also covers the single frame between the
            // player object arriving and the camera snapping onto it.
            if (_hiding)
            {
                _fade -= Time.unscaledDeltaTime * 4f;
                if (_group != null) _group.alpha = Mathf.Max(0f, _fade);
                if (_fade <= 0f) Destroy(gameObject);
            }
        }

    }
}
