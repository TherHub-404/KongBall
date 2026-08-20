using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace KongBall
{
    // Everything that is not the match: home, mode choice, private rooms, waiting room.
    //
    // These are screens, not overlays. MenuStage puts a coloured field and the monkey behind them and
    // takes the game off screen entirely — the previous version was a translucent sheet over the live
    // pitch, joystick and all, which made every one of these look like a dialog interrupting a match
    // rather than a place the player is in.
    //
    // Built in code, like ConnectingScreen and the SFX: no scene authoring, and it works in the one
    // scene we ship.
    public class MainMenu : MonoBehaviour
    {
        public const int CodeLength = 4;

        // No I/O/0/1: a code is read out loud or typed from a screenshot, and those four are where
        // that goes wrong.
        const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        const int SortingOrder = 4900;

        // Wordmark: width in canvas units, and the gap between the screen's top edge and its top.
        // The height follows from the artwork, so these two are the only numbers to touch.
        const float LogoWidth = 440f;
        const float LogoTopGap = 18f;

        // Tuned against MenuStage.Background, which is a strong yellow: mint on yellow was unreadable.
        static readonly Color Ink = new Color(0.10f, 0.15f, 0.09f);
        static readonly Color Primary = new Color(0.10f, 0.40f, 0.23f);
        static readonly Color Secondary = new Color(0.30f, 0.25f, 0.11f);
        static readonly Color Field = new Color(1f, 0.98f, 0.92f);
        static readonly Color Warn = new Color(0.60f, 0.14f, 0.08f);

        static MainMenu _current;

        GameObject _home, _modes, _friends, _waiting;
        InputField _code;
        Text _hint, _waitCount, _waitCode, _waitClock;
        System.Action _onAbandon;

        // --- entry points -------------------------------------------------------------------------

        public static void Show()
        {
            MenuStage.Show();
            Ensure().GoHome();
        }

        public static void ShowWaiting(System.Action onAbandon)
        {
            MenuStage.Show();
            var m = Ensure();
            m._onAbandon = onAbandon;
            m.Only(m._waiting);
        }

        // Live numbers for the waiting screen. Pushed in rather than pulled, so this file needs to
        // know nothing about runners or match phases.
        public static void SetWaiting(int seated, int seats, string code, float secondsLeft)
        {
            if (_current == null) return;
            if (_current._waitCount != null) _current._waitCount.text = seated + " / " + Mathf.Max(2, seats);
            if (_current._waitCode != null)
            {
                bool has = !string.IsNullOrEmpty(code);
                _current._waitCode.text = has ? "CODICE   " + code : "";
                _current._waitCode.enabled = has;
            }
            if (_current._waitClock != null)
            {
                bool has = secondsLeft >= 0f;
                if (has)
                {
                    int s = Mathf.CeilToInt(secondsLeft);
                    _current._waitClock.text = (s / 60) + ":" + (s % 60).ToString("00");
                }
                _current._waitClock.enabled = has;
            }
        }

        public static void Hide()
        {
            if (_current == null) return;
            Destroy(_current.gameObject);
            _current = null;
        }

        static MainMenu Ensure()
        {
            if (_current != null) return _current;
            var go = new GameObject("MainMenu");
            _current = go.AddComponent<MainMenu>();
            _current.Build();
            return _current;
        }

        void OnDestroy()
        {
            if (_current == this) _current = null;
        }

        // --- layout ------------------------------------------------------------------------------

        void Build()
        {
            Ui.NewOverlayCanvas(gameObject, SortingOrder);

            BuildLogo();
            BuildStampLabel();

            BuildHome();
            BuildModes();
            BuildFriends();
            BuildWaiting();
            GoHome();
        }

        // The wordmark, hung from the top edge rather than centred on a magic y: its height depends on
        // the artwork's own proportions, and anchoring to the top keeps the gap under the screen edge
        // fixed on every phone.
        //
        // The file is cropped to the ink before being committed — the original carried 99 px of empty
        // space above and 93 below, and 70 on the left against 38 on the right. Laid out as it came,
        // the logo would have sat low in its box and visibly off-centre.
        void BuildLogo()
        {
            var sprite = LoadLogo();
            if (sprite == null)
            {
                // No artwork: fall back to the name, rather than a menu with no title at all.
                var t = Ui.NewText("Title", transform, 76);
                if (t != null) { t.text = "KONGBALL"; t.color = Ink; Ui.Place(t.rectTransform, 0f, 245f, 900f, 100f); }
                return;
            }

            var img = Ui.NewImage("Logo", transform);
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.raycastTarget = false;

            float ratio = sprite.rect.height > 1f ? sprite.rect.width / sprite.rect.height : 3.4f;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(LogoWidth, LogoWidth / ratio);
            rt.anchoredPosition = new Vector2(0f, -LogoTopGap);
        }

        // Sprite if the importer made one, otherwise built from the texture — so the menu still shows
        // the logo even if the asset is ever reimported as a plain texture.
        static Sprite LoadLogo()
        {
            var sprite = Resources.Load<Sprite>("Menu/Logo");
            if (sprite != null) return sprite;
            var tex = Resources.Load<Texture2D>("Menu/Logo");
            if (tex == null) { Debug.LogWarning("[Menu] Resources/Menu/Logo missing"); return null; }
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        // Who made this build, in the corner: the branch of the pull request that produced it.
        //
        // Top-left, with the same inset as the in-match MENU button. That is not taste — the game is
        // landscape only, so the notch sits on one long edge and the home indicator along the bottom,
        // and that corner is the one placement in this project already proven on a real device.
        //
        // It hangs off the menu ROOT rather than off a panel, so it is also there on the waiting
        // screen: that is where somebody is most likely to wonder which build they are on. It costs
        // nothing when the file is empty, which is the case for anything not built by the pipeline.
        void BuildStampLabel()
        {
            string label = BuildStamp.Label;
            if (string.IsNullOrEmpty(label)) return;

            // Accanto al timbro, l'identita' di rete. Serve a una domanda precisa che due persone si
            // fanno guardandosi in faccia: "perche' non ci troviamo?". Con questo numero la risposta
            // e' leggibile — se differisce, le due build non possono incontrarsi ed e' voluto.
            string rete = NetLauncher.Netcode;
            if (!string.IsNullOrEmpty(rete)) label += "\n" + rete;

            var t = Ui.NewText("BuildStamp", transform, 20);
            if (t == null) return;
            t.text = label;
            t.color = new Color(Ink.r, Ink.g, Ink.b, 0.55f);   // present, not shouting
            t.alignment = TextAnchor.UpperLeft;

            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(520f, 52f);   // due righe: timbro e identita' di rete
            rt.anchoredPosition = new Vector2(26f, -26f);
        }

        void BuildHome()
        {
            _home = NewPanel("Home");
            Button(_home.transform, "GIOCA", 0f, 183f, Primary, Color.white,
                   () => Only(_modes), 520f, 88f, fromBottom: true);
            Button(_home.transform, "GIOCA CON GLI AMICI", 0f, 78f, Secondary, Color.white,
                   () => Only(_friends), 520f, 76f, fromBottom: true);
        }

        void BuildModes()
        {
            _modes = NewPanel("Modes");

            // Side by side rather than stacked: three buttons in a column reached high enough to meet
            // the character's feet on a tall screen, and two choices read better as a pair anyway.
            Button(_modes.transform, "1 vs 1", -150f, 180f, Primary, Color.white,
                   () => Launch(() => NetLauncher.Instance.StartQuickMatch(MatchMode.OneVsOne)),
                   280f, 88f, fromBottom: true);
            Button(_modes.transform, "2 vs 2", 150f, 180f, Primary, Color.white,
                   () => Launch(() => NetLauncher.Instance.StartQuickMatch(MatchMode.TwoVsTwo)),
                   280f, 88f, fromBottom: true);
            // Practice sits BESIDE the way back rather than under the pair, and that is a measured
            // decision, not a taste one: the pair above already clears the character's feet by only
            // ~30 canvas units on a 19.5:9 phone, so a third full-width row would have run into them
            // — the same collision that was found by simulation once already.
            Button(_modes.transform, "INDIETRO", -170f, 72f, Secondary, Color.white,
                   GoHome, 300f, 56f, fromBottom: true);
            Button(_modes.transform, "ALLENAMENTO", 170f, 72f, Primary, Color.white,
                   () => Launch(() => NetLauncher.Instance.StartPractice()), 300f, 56f, fromBottom: true);
        }

        void BuildFriends()
        {
            _friends = NewPanel("Friends");

            var head = Ui.NewText("Head", _friends.transform, 27);
            if (head != null)
            {
                head.text = "CREA UNA STANZA E DETTA IL CODICE,\nOPPURE ENTRA CON QUELLO DI UN AMICO";
                head.color = Ink;
                Ui.Place(head.rectTransform, 0f, 118f, 900f, 80f);
            }

            Button(_friends.transform, "CREA  1 vs 1", -170f, 32f, Primary, Color.white,
                   () => Launch(() => NetLauncher.Instance.CreatePrivateMatch(MatchMode.OneVsOne)), 300f, 76f);
            Button(_friends.transform, "CREA  2 vs 2", 170f, 32f, Primary, Color.white,
                   () => Launch(() => NetLauncher.Instance.CreatePrivateMatch(MatchMode.TwoVsTwo)), 300f, 76f);

            _code = NewInput("Code", _friends.transform, -110f, -68f, 380f);
            Button(_friends.transform, "ENTRA", 175f, -68f, Primary, Color.white, JoinWithCode, 250f, 76f);

            _hint = Ui.NewText("Hint", _friends.transform, 24);
            if (_hint != null)
            {
                _hint.color = Warn;
                Ui.Place(_hint.rectTransform, 0f, -132f, 900f, 40f);
            }

            Button(_friends.transform, "INDIETRO", 0f, -245f, Secondary, Color.white, GoHome, 320f, 56f);
        }

        void BuildWaiting()
        {
            _waiting = NewPanel("Waiting");

            var head = Ui.NewText("Head", _waiting.transform, 30);
            if (head != null)
            {
                head.text = "IN ATTESA DI GIOCATORI";
                head.color = Ink;
                Ui.Place(head.rectTransform, 0f, 120f, 900f, 50f);
            }

            _waitCount = Ui.NewText("Count", _waiting.transform, 68);
            if (_waitCount != null)
            {
                _waitCount.color = Ink;
                Ui.Place(_waitCount.rectTransform, 0f, 55f, 900f, 90f);
            }

            _waitCode = Ui.NewText("Code", _waiting.transform, 34);
            if (_waitCode != null)
            {
                _waitCode.color = Primary;
                Ui.Place(_waitCode.rectTransform, 0f, -10f, 900f, 50f);
            }

            _waitClock = Ui.NewText("Clock", _waiting.transform, 30);
            if (_waitClock != null)
            {
                _waitClock.color = Ink;
                Ui.Place(_waitClock.rectTransform, 0f, -60f, 900f, 44f);
            }

            Button(_waiting.transform, "ABBANDONA", 0f, -240f, Secondary, Color.white,
                   () => { var a = _onAbandon; _onAbandon = null; a?.Invoke(); }, 340f, 64f);
        }

        void Only(GameObject panel)
        {
            if (_home != null) _home.SetActive(panel == _home);
            if (_modes != null) _modes.SetActive(panel == _modes);
            if (_friends != null) _friends.SetActive(panel == _friends);
            if (_waiting != null) _waiting.SetActive(panel == _waiting);
        }

        void GoHome()
        {
            Only(_home);
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
        // player looking at nothing whenever the launcher refused — which it does whenever a start is
        // already in flight.
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

        // --- menu-specific widgets ---------------------------------------------------------------

        GameObject NewPanel(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            Ui.Stretch((RectTransform)go.transform);
            return go;
        }

        void Button(Transform parent, string label, float x, float y, Color fill, Color ink,
                    UnityEngine.Events.UnityAction onClick, float width = 520f, float height = 88f,
                    bool fromBottom = false)
        {
            var img = Ui.NewImage("Btn_" + label, parent);
            img.color = fill;
            if (fromBottom) Ui.PlaceFromBottom(img.rectTransform, x, y, width, height);
            else Ui.Place(img.rectTransform, x, y, width, height);

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
            Ui.Place(img.rectTransform, x, y, width, 76f);

            var text = Ui.NewText("Text", img.transform, 40);
            if (text == null) return null;
            text.supportRichText = false;
            text.color = Ink;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(16f, 0f);
            trt.offsetMax = new Vector2(-16f, 0f);

            var placeholder = Ui.NewText("Placeholder", img.transform, 36);
            if (placeholder != null)
            {
                placeholder.text = "CODICE";
                placeholder.color = new Color(0.55f, 0.52f, 0.45f);
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
