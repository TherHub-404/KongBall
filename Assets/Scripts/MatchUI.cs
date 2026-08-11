using UnityEngine;
using UnityEngine.UI;

namespace CalcioStumble
{
    // Minimal HUD: score, countdown, GOAL! banner, end-of-match panel with replay.
    public class MatchUI : MonoBehaviour
    {
        public Text scoreText;
        public Text timerText;
        public Text goalBanner;
        public GameObject endPanel;
        public Text endText;
        public Button replayButton;

        [Header("Title screen")]
        public GameObject titlePanel;
        public Text titleText;
        public Text titleHint;
        public Button titleButton;

        float _titlePopT = 1f;

        void Awake()
        {
            if (replayButton != null) replayButton.onClick.AddListener(OnReplay);
            if (titleButton != null) titleButton.onClick.AddListener(OnTitleTap);
            HideEndPanel();
            HideGoalBanner();
        }

        public void ShowTitle()
        {
            if (titlePanel != null) titlePanel.SetActive(true);
            _titlePopT = 0f;
        }
        public void HideTitle()
        {
            if (titlePanel != null) titlePanel.SetActive(false);
        }
        void OnTitleTap()
        {
            if (GameManager.Instance != null) GameManager.Instance.BeginMatch();
        }

        public void UpdateScore(int blue, int red)
        {
            if (scoreText != null) scoreText.text = blue + "  -  " + red;
        }

        public void UpdateTimer(float t)
        {
            if (timerText == null) return;
            if (t < 0f) t = 0f;
            int m = Mathf.FloorToInt(t / 60f);
            int s = Mathf.FloorToInt(t % 60f);
            timerText.text = string.Format("{0}:{1:00}", m, s);
        }

        float _popT = 1f;

        public void ShowGoalBanner(string msg)
        {
            if (goalBanner == null) return;
            goalBanner.text = msg;
            goalBanner.gameObject.SetActive(true);
            _popT = 0f; // restart the pop animation on each message (GOAL!, 3, 2, 1)
        }

        void Update()
        {
            if (goalBanner != null && goalBanner.gameObject.activeSelf && _popT < 1f)
            {
                _popT = Mathf.Min(1f, _popT + Time.deltaTime * 4f);
                float s = Mathf.Lerp(1.7f, 1f, 1f - (1f - _popT) * (1f - _popT)); // ease-out pop
                goalBanner.rectTransform.localScale = Vector3.one * s;
            }

            if (titlePanel != null && titlePanel.activeSelf)
            {
                if (_titlePopT < 1f && titleText != null)
                {
                    _titlePopT = Mathf.Min(1f, _titlePopT + Time.deltaTime * 3f);
                    float s = Mathf.Lerp(1.6f, 1f, 1f - (1f - _titlePopT) * (1f - _titlePopT));
                    titleText.rectTransform.localScale = Vector3.one * s;
                }
                if (titleHint != null)
                {
                    var c = titleHint.color;
                    c.a = 0.35f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 2.2f));
                    titleHint.color = c;
                }
            }
        }
        public void HideGoalBanner()
        {
            if (goalBanner != null) goalBanner.gameObject.SetActive(false);
        }

        public void ShowEndPanel(string msg)
        {
            if (endPanel != null) endPanel.SetActive(true);
            if (endText != null) endText.text = msg;
        }
        public void HideEndPanel()
        {
            if (endPanel != null) endPanel.SetActive(false);
        }

        void OnReplay()
        {
            if (GameManager.Instance != null) GameManager.Instance.Replay();
        }
    }
}
