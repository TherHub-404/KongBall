using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace KongBall
{
    // The front door: two ways in, kept deliberately separate.
    //
    //  GIOCA          -> matchmaking. No session name, so Photon joins a matching room or creates
    //                    one, and the mode is a matchmaking filter, so 1v1 and 2v2 never mix.
    //  CON GLI AMICI  -> a named room created invisible, which Photon excludes from random
    //                    matchmaking. A code is the only way in.
    //
    // Built entirely in code, like ConnectingScreen and the SFX: no scene authoring, so it cannot
    // drift out of sync with a prefab and it works in the one scene we ship.
    public class MainMenu : MonoBehaviour
    {
        public const int CodeLength = 4;

        // No I/O/0/1: a code is read out loud or typed from a screenshot, and those four are where
        // that goes wrong.
        const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        const int SortingOrder = 4900;
        static readonly Color Backdrop = new Color(0.05f, 0.09f, 0.07f, 0.97f);
        static readonly Color Accent = new Color(0.35f, 0.85f, 0.65f);
        static readonly Color Quiet = new Color(0.22f, 0.30f, 0.27f);
        static readonly Color Field = new Color(0.12f, 0.16f, 0.14f);
        static readonly Color Ink = new Color(0.05f, 0.07f, 0.06f);

        static MainMenu _current;

        GameObject _home;
        GameObject _modes;
        GameObject _friends;
        InputField _code;
        Text _hint;

        public static void Show()
        {
            if (_current != null) { _current.GoHome(); return; }
            var go = new GameObject("MainMenu");
            _current = go.AddComponent<MainMenu>();
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

        // --- layout ------------------------------------------------------------------------------

        void Build()
        {
            Ui.NewOverlayCanvas(gameObject, SortingOrder);

            var bg = Ui.NewImage("Backdrop", transform);
            bg.color = Backdrop;
            Ui.Stretch(bg.rectTransform);

            var title = Ui.NewText("Title", transform, 78);
            if (title != null)
            {
                title.text = "KONGBALL";
                title.color = Accent;
                Ui.Place(title.rectTransform, 0f, 210f, 900f, 110f);
            }

            BuildHome();
            BuildModes();
            BuildFriends();
            GoHome();
        }

        void BuildHome()
        {
            _home = NewPanel("Home");

            Button(_home.transform, "GIOCA", 0f, 40f, Accent, Ink, () => Swap(_home, _modes));
            Button(_home.transform, "GIOCA CON GLI AMICI", 0f, -80f, Quiet, Color.white,
                   () => Swap(_home, _friends));
        }

        void BuildModes()
        {
            _modes = NewPanel("Modes");

            Button(_modes.transform, "1 vs 1", 0f, 70f, Accent, Ink,
                   () => Launch(() => NetLauncher.Instance.StartQuickMatch(MatchMode.OneVsOne)));
            Button(_modes.transform, "2 vs 2", 0f, -30f, Accent, Ink,
                   () => Launch(() => NetLauncher.Instance.StartQuickMatch(MatchMode.TwoVsTwo)));

            var note = Ui.NewText("Note", _modes.transform, 24);
            if (note != null)
            {
                note.text = "si gioca appena la squadra e' al completo.\n"
                          + "se non si trova nessuno, si torna qui dopo due minuti.";
                note.color = new Color(0.55f, 0.62f, 0.58f);
                Ui.Place(note.rectTransform, 0f, -110f, 900f, 70f);
            }

            Button(_modes.transform, "INDIETRO", 0f, -215f, Quiet, Color.white, GoHome, 320f, 70f);
        }

        void BuildFriends()
        {
            _friends = NewPanel("Friends");

            var head = Ui.NewText("Head", _friends.transform, 30);
            if (head != null)
            {
                head.text = "CREA UNA STANZA E DETTA IL CODICE,\nOPPURE ENTRA CON IL CODICE DI UN AMICO";
                head.color = new Color(0.62f, 0.70f, 0.66f);
                Ui.Place(head.rectTransform, 0f, 120f, 900f, 90f);
            }

            Button(_friends.transform, "CREA  1 vs 1", -170f, 30f, Accent, Ink,
                   () => Launch(() => NetLauncher.Instance.CreatePrivateMatch(MatchMode.OneVsOne)), 300f);
            Button(_friends.transform, "CREA  2 vs 2", 170f, 30f, Accent, Ink,
                   () => Launch(() => NetLauncher.Instance.CreatePrivateMatch(MatchMode.TwoVsTwo)), 300f);

            _code = NewInput("Code", _friends.transform, -110f, -70f, 400f);
            Button(_friends.transform, "ENTRA", 190f, -70f, Accent, Ink, JoinWithCode, 260f);

            _hint = Ui.NewText("Hint", _friends.transform, 24);
            if (_hint != null)
            {
                _hint.color = new Color(0.9f, 0.55f, 0.45f);
                Ui.Place(_hint.rectTransform, 0f, -135f, 900f, 40f);
            }

            Button(_friends.transform, "INDIETRO", 0f, -215f, Quiet, Color.white, GoHome, 320f, 70f);
        }

        void Swap(GameObject from, GameObject to)
        {
            if (from != null) from.SetActive(false);
            if (to != null) to.SetActive(true);
        }

        void GoHome()
        {
            if (_home != null) _home.SetActive(true);
            if (_modes != null) _modes.SetActive(false);
            if (_friends != null) _friends.SetActive(false);
            if (_hint != null) _hint.text = "";
        }

        // --- actions -----------------------------------------------------------------------------

        void JoinWithCode()
        {
            string raw = _code != null ? _code.text : "";
            string code = Normalise(raw);
            if (code.Length != CodeLength)
            {
                // "il codice e' di 4 caratteri" is a lie when the player typed four of them and
                // Normalise dropped a couple, so say which ones a code never contains.
                bool dropped = raw.Trim().Length >= CodeLength;
                if (_hint != null)
                    _hint.text = dropped ? "un codice non contiene I, O, 0 o 1"
                                         : "il codice e' di " + CodeLength + " caratteri";
                return;
            }
            Launch(() => NetLauncher.Instance.JoinPrivateMatch(code));
        }

        // The menu is torn down only once the launcher has ACCEPTED. Hiding regardless left the
        // player looking at the empty pitch with no menu and no match whenever the launcher refused
        // — which it does whenever a start is already in flight.
        static void Launch(System.Func<bool> start)
        {
            if (NetLauncher.Instance == null)
            {
                Debug.LogWarning("[Menu] no NetLauncher in the scene");
                return;
            }
            if (start()) Hide();
        }

        public static string NewCode()
        {
            var sb = new StringBuilder(CodeLength);
            for (int i = 0; i < CodeLength; i++)
                sb.Append(CodeAlphabet[Random.Range(0, CodeAlphabet.Length)]);
            return sb.ToString();
        }

        // Typed codes arrive with stray spaces and in whatever case the keyboard felt like.
        public static string Normalise(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder(CodeLength);
            foreach (char c in raw.ToUpperInvariant())
                if (CodeAlphabet.IndexOf(c) >= 0 && sb.Length < CodeLength) sb.Append(c);
            return sb.ToString();
        }

        // A single button on its own canvas, for use outside the menu — the waiting room needs a way
        // out, and waiting alone in a room nobody joins was otherwise another dead end. Caller owns
        // the returned object and destroys it.
        public static GameObject Overlay(string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Overlay_" + label);
            Ui.NewOverlayCanvas(go, SortingOrder - 100);

            var img = Ui.NewImage("Btn", go.transform);
            img.color = Quiet;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(300f, 72f);
            rt.anchoredPosition = new Vector2(0f, -60f);

            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var t = Ui.NewText("Text", img.transform, 30);
            if (t != null) { t.text = label; t.color = Color.white; Ui.Stretch(t.rectTransform); }
            return go;
        }

        // --- tiny UI helpers ---------------------------------------------------------------------

        GameObject NewPanel(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            Ui.Stretch((RectTransform)go.transform);
            return go;
        }

        void Button(Transform parent, string label, float x, float y, Color fill, Color ink,
                    UnityEngine.Events.UnityAction onClick, float width = 520f, float height = 88f)
        {
            var img = Ui.NewImage("Btn_" + label, parent);
            img.color = fill;
            Ui.Place(img.rectTransform, x, y, width, height);

            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var t = Ui.NewText("Text", img.transform, 34);
            if (t != null) { t.text = label; t.color = ink; Ui.Stretch(t.rectTransform); }
        }

        InputField NewInput(string name, Transform parent, float x, float y, float width)
        {
            var img = Ui.NewImage(name, parent);
            img.color = Field;
            Ui.Place(img.rectTransform, x, y, width, 88f);

            var text = Ui.NewText("Text", img.transform, 44);
            if (text == null) return null;
            text.supportRichText = false;
            text.color = Color.white;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(16f, 0f);
            trt.offsetMax = new Vector2(-16f, 0f);

            var placeholder = Ui.NewText("Placeholder", img.transform, 40);
            if (placeholder != null)
            {
                placeholder.text = "CODICE";
                placeholder.color = new Color(0.45f, 0.52f, 0.49f);
                Ui.Stretch(placeholder.rectTransform);
            }

            var input = img.gameObject.AddComponent<InputField>();
            input.targetGraphic = img;
            input.textComponent = text;
            if (placeholder != null) input.placeholder = placeholder;
            input.characterLimit = CodeLength;
            input.characterValidation = InputField.CharacterValidation.Alphanumeric;
            input.lineType = InputField.LineType.SingleLine;
            // Uppercase as it is typed, so what the player sees matches what gets sent.
            input.onValueChanged.AddListener(v =>
            {
                string up = v.ToUpperInvariant();
                if (up != v) input.text = up;
            });
            return input;
        }

    }
}
