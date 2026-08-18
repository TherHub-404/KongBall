using UnityEngine;
using UnityEngine.UI;

namespace KongBall
{
    // Reads the networked match state and drives: the score/timer line + a big centre banner
    // (3-2-1 countdown, VIA!, GOAL!, winner) with a pop animation and audio ticks.
    public class NetScoreUI : MonoBehaviour
    {
        public Text scoreText;
        public Text bannerText;

        int _lastTotal = -1;
        int _lastCount = -1;
        float _viaTimer;
        float _pop;
        MatchController.Phase _lastPhase = MatchController.Phase.Finished;

        void Update()
        {
            var mc = MatchController.Instance;
            if (mc == null) return;

            // Goal jingle on score change.
            int total = mc.ScoreBlue + mc.ScoreRed;
            if (_lastTotal < 0) _lastTotal = total;
            else if (total != _lastTotal) { _lastTotal = total; if (SfxManager.Instance != null) SfxManager.Instance.PlayGoal(); }

            if (scoreText != null)
            {
                string extra;
                switch (mc.CurPhase)
                {
                    case MatchController.Phase.Finished:
                        extra = mc.Winner == 0 ? "   VINCE BLU" : mc.Winner == 1 ? "   VINCE ROSSO" : "   PAREGGIO";
                        // Said here and not in the banner: that one is sized for "GOAL!", so an extra
                        // line on it runs off both edges of the screen.
                        if (mc.ByForfeit) extra += "  (RITIRO)";
                        break;
                    default:
                        if (mc.endless) { extra = ""; break; }
                        int t = Mathf.CeilToInt(mc.MatchTime);
                        extra = "   " + (t / 60) + ":" + (t % 60).ToString("00"); break;
                }
                scoreText.text = "BLU " + mc.ScoreBlue + " - " + mc.ScoreRed + " ROSSO" + extra;
            }

            if (bannerText != null) UpdateBanner(mc);
        }

        void UpdateBanner(MatchController mc)
        {
            string b = null;
            var ph = mc.CurPhase;

            // Waiting says nothing here. This banner is sized for "GOAL!", so a full sentence ran off
            // both edges of the screen — and the waiting room is now a screen of its own anyway.
            if (ph == MatchController.Phase.Countdown)
            {
                int c = Mathf.Clamp(Mathf.CeilToInt(mc.PhaseTimer), 1, 3);
                b = c.ToString();
                if (c != _lastCount) { _lastCount = c; _pop = 1f; if (SfxManager.Instance != null) SfxManager.Instance.PlayKick(); }
                _viaTimer = 0.9f; // armed for when play starts
            }
            else if (ph == MatchController.Phase.GoalPause)
            {
                b = "GOAL!";
                if (_lastPhase != MatchController.Phase.GoalPause) _pop = 1f;
            }
            else if (ph == MatchController.Phase.Finished)
            {
                b = mc.Winner == 0 ? "VINCE BLU!" : mc.Winner == 1 ? "VINCE ROSSO!" : "PAREGGIO";
            }
            else // Playing
            {
                _lastCount = -1;
                if (_viaTimer > 0f)
                {
                    _viaTimer -= Time.deltaTime;
                    b = "VIA!";
                    if (_lastPhase == MatchController.Phase.Countdown) _pop = 1f;
                }
            }
            _lastPhase = ph;

            if (string.IsNullOrEmpty(b))
            {
                if (bannerText.enabled) bannerText.enabled = false;
                return;
            }
            bannerText.enabled = true;
            bannerText.text = b;
            _pop = Mathf.MoveTowards(_pop, 0f, Time.deltaTime * 3f);
            bannerText.transform.localScale = Vector3.one * (1f + _pop * 0.6f);
        }
    }
}
