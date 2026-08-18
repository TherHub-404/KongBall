using UnityEngine;
using UnityEngine.UI;

namespace KongBall
{
    // The only thing on screen during a match that is not the match: a small button in the corner
    // that opens the forfeit confirmation.
    //
    // Deliberately out of the way. The joystick owns the bottom left, jump and grab the bottom right,
    // the score the top middle; this takes the one corner nothing else wants, and it takes two taps,
    // so a thumb resting on the screen can never end somebody's game.
    //
    // Note there is no pause: an online match keeps running while the confirmation is open, which is
    // the honest behaviour — the other players are still playing.
    public class MatchMenu : MonoBehaviour
    {
        // Above the gameplay HUD (sorting order 0), below MainMenu (4900) and ConnectingScreen (5000).
        const int SortingOrder = 4700;

        static readonly Color Quiet = new Color(0.10f, 0.14f, 0.12f, 0.62f);
        static readonly Color Sheet = new Color(0.05f, 0.09f, 0.07f, 0.90f);
        static readonly Color Danger = new Color(0.62f, 0.16f, 0.11f);
        static readonly Color Neutral = new Color(0.24f, 0.30f, 0.27f);

        static MatchMenu _current;

        GameObject _confirm;
        System.Action _onForfeit;

        // Idempotent: the launcher calls this every frame the match is live.
        public static void Show(System.Action onForfeit)
        {
            if (_current != null) { _current._onForfeit = onForfeit; return; }
            var go = new GameObject("MatchMenu");
            _current = go.AddComponent<MatchMenu>();
            _current._onForfeit = onForfeit;
            _current.Build();
        }

        public static void Hide()
        {
            if (_current == null) return;
            Destroy(_current.gameObject);
            _current = null;
        }

        void OnDestroy()
        {
            if (_current == this) _current = null;
        }

        void Build()
        {
            Ui.NewOverlayCanvas(gameObject, SortingOrder);

            var open = Ui.NewImage("Open", transform);
            open.color = Quiet;
            var rt = open.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);   // top-left, the one free corner
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(104f, 60f);
            rt.anchoredPosition = new Vector2(26f, -26f);

            var btn = open.gameObject.AddComponent<Button>();
            btn.targetGraphic = open;
            btn.onClick.AddListener(() => { if (_confirm != null) _confirm.SetActive(true); });

            var label = Ui.NewText("Text", open.transform, 24);
            if (label != null) { label.text = "MENU"; label.color = Color.white; Ui.Stretch(label.rectTransform); }

            BuildConfirm();
        }

        void BuildConfirm()
        {
            _confirm = new GameObject("Confirm", typeof(RectTransform));
            _confirm.transform.SetParent(transform, false);
            Ui.Stretch((RectTransform)_confirm.transform);

            // Also swallows taps, so the joystick underneath cannot be worked while the sheet is open.
            var bg = Ui.NewImage("Backdrop", _confirm.transform);
            bg.color = Sheet;
            Ui.Stretch(bg.rectTransform);

            var q = Ui.NewText("Question", _confirm.transform, 40);
            if (q != null)
            {
                q.text = "ABBANDONARE LA PARTITA?";
                q.color = Color.white;
                Ui.Place(q.rectTransform, 0f, 70f, 900f, 60f);
            }

            Button(_confirm.transform, "ABBANDONA", 0f, -20f, Danger,
                   () => { var f = _onForfeit; _onForfeit = null; f?.Invoke(); });
            Button(_confirm.transform, "CONTINUA", 0f, -120f, Neutral,
                   () => _confirm.SetActive(false));

            _confirm.SetActive(false);
        }

        void Button(Transform parent, string label, float x, float y, Color fill,
                    UnityEngine.Events.UnityAction onClick)
        {
            var img = Ui.NewImage("Btn_" + label, parent);
            img.color = fill;
            Ui.Place(img.rectTransform, x, y, 460f, 80f);

            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var t = Ui.NewText("Text", img.transform, 32);
            if (t != null) { t.text = label; t.color = Color.white; Ui.Stretch(t.rectTransform); }
        }
    }
}
