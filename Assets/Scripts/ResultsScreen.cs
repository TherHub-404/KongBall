using UnityEngine;
using UnityEngine.UI;

namespace KongBall
{
    // The screen a match actually ends on. Before this there was only a six-second banner
    // ("VINCE BLU!") and an automatic return to the menu — no way to leave sooner, and in practice no
    // obvious way to go again besides the small RESET button in the corner (MatchMenu.BuildReset).
    //
    // Shown once by NetLauncher.WatchForMatchEnd the moment MatchController reaches Finished;
    // NetLauncher's own timer (postMatchSeconds) becomes a longer safety net behind it for a player
    // who has put the phone down, not the primary way out any more.
    //
    // Same shape as MatchMenu/ConnectingScreen: its own Canvas, Ui.ToyButton for every button.
    public class ResultsScreen : MonoBehaviour
    {
        // Above the gameplay HUD (0) and MatchMenu's corner button (4700); below MainMenu (4900) — the
        // two full-screen menus never coexist, so the gap above MatchMenu is the only one that matters.
        const int SortingOrder = 4800;

        static readonly Color Backdrop = new Color(0.05f, 0.07f, 0.06f, 0.90f);
        static readonly Color Primary = Ui.JungleGreen;
        static readonly Color Secondary = Ui.WoodBrown;

        static ResultsScreen _current;

        System.Action _onMenu;
        GameObject _replayBtn;

        // Idempotent, like MatchMenu.Show: NetLauncher calls this once per Finished phase, but a
        // second call (e.g. if it ever raced with something else) just refreshes who to call on MENU
        // and whether RIGIOCA should show, instead of stacking a second screen.
        public static void Show(System.Action onMenu, bool showReplay)
        {
            if (_current == null)
            {
                var go = new GameObject("ResultsScreen");
                _current = go.AddComponent<ResultsScreen>();
                _current.Build();
            }
            _current._onMenu = onMenu;
            if (_current._replayBtn != null) _current._replayBtn.SetActive(showReplay);
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

            var bg = Ui.NewImage("Backdrop", transform);
            bg.color = Backdrop;
            Ui.Stretch(bg.rectTransform);

            // Read once, at the moment the screen is built: MatchController stops changing the score
            // the instant it reaches Finished, so there is nothing later to go stale against.
            var mc = MatchController.Instance;

            var title = Ui.NewText("Title", transform, 88);
            if (title != null)
            {
                title.text = WinnerText(mc);
                title.color = Color.white;
                Ui.Place(title.rectTransform, 0f, 140f, 1000f, 140f);
            }

            var score = Ui.NewText("Score", transform, 44);
            if (score != null)
            {
                score.text = mc != null ? "BLU  " + mc.ScoreBlue + " - " + mc.ScoreRed + "  ROSSO" : "";
                score.color = new Color(1f, 1f, 1f, 0.85f);
                Ui.Place(score.rectTransform, 0f, 40f, 900f, 60f);
            }

            if (mc != null && mc.ByForfeit)
            {
                var note = Ui.NewText("Forfeit", transform, 28);
                if (note != null)
                {
                    note.text = "VITTORIA PER RITIRO DELL'AVVERSARIO";
                    note.color = new Color(1f, 1f, 1f, 0.6f);
                    Ui.Place(note.rectTransform, 0f, -10f, 900f, 40f);
                }
            }

            // RIGIOCA calls the same MatchController.ResetMatch() the corner RESET button does — this
            // is that action given the prominence a "go again" choice deserves, not a second mechanism.
            _replayBtn = Button(transform, "RIGIOCA", 0f, -100f, Primary,
                                 () => { MatchController.Instance?.ResetMatch(); Hide(); });

            Button(transform, "MENU", 0f, -190f, Secondary,
                   () => { var m = _onMenu; _onMenu = null; m?.Invoke(); });
        }

        static string WinnerText(MatchController mc)
        {
            if (mc == null) return "";
            if (mc.Winner == 0) return "VINCE BLU";
            if (mc.Winner == 1) return "VINCE ROSSO";
            return "PAREGGIO";
        }

        GameObject Button(Transform parent, string label, float x, float y, Color fill,
                           UnityEngine.Events.UnityAction onClick)
        {
            var hit = Ui.ToyButton("Btn_" + label, parent, 420f, 84f, fill);
            Ui.Place(hit.rectTransform, x, y, 420f, 84f);

            var btn = hit.gameObject.AddComponent<Button>();
            btn.targetGraphic = hit;
            btn.onClick.AddListener(UiSfx.Open);
            btn.onClick.AddListener(onClick);

            var t = Ui.NewText("Text", hit.transform, 32);
            if (t != null) { t.text = label; t.color = Color.white; Ui.Stretch(t.rectTransform); }

            return hit.gameObject;
        }
    }
}
