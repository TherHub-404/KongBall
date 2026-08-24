using UnityEngine;
using UnityEngine.UI;

namespace KongBall
{
    // Builds the in-match HUD — joystick, kick/jump buttons, the camera-look catcher, the score line
    // and the centre banner — from code onto the scene's own "HUD" Canvas, instead of the hand-authored
    // sprites it carried before the "no Editor, ever" rule existed. Same positions/sizes as before
    // (measured off the scene, not guessed): only the look changes, to match MainMenu/MatchMenu/
    // ConnectingScreen's toy styling.
    //
    // MenuStage.cs toggles the "HUD" GameObject's own Canvas/GraphicRaycaster off and on around the
    // menus; this only ever touches its CHILDREN, so that switch keeps working untouched.
    //
    // Installs itself on scene load, like ArenaDressing — independent of NetLauncher's bootstrap, and
    // it has to run whether or not a match ever starts, since the HUD exists before any Fusion runner
    // does.
    public class MatchHud : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<MatchHud>() != null) return;
            new GameObject("MatchHud").AddComponent<MatchHud>();
        }

        void Start()
        {
            var hud = GameObject.Find("HUD");
            if (hud == null) { Debug.LogWarning("[Hud] no HUD GameObject in the scene"); return; }
            var parent = hud.transform;

            var input = FindAnyObjectByType<LocalInputSource>();

            // Built in this order because a Canvas draws later siblings on top of earlier ones, and
            // GraphicRaycaster hands a touch to whichever graphic is topmost: LookArea is a full-screen
            // catcher and has to lose that contest to the joystick and the two buttons sitting over it,
            // or neither could ever be pressed.
            BuildLookArea(parent, input);
            BuildJoystick(parent, input);
            BuildActionButton(parent, input);
            BuildJumpButton(parent, input);
            BuildScoreAndBanner(parent);
        }

        void BuildLookArea(Transform parent, LocalInputSource input)
        {
            var img = Ui.NewImage("LookArea", parent);
            img.color = new Color(0f, 0f, 0f, 0f);
            Ui.Stretch(img.rectTransform);

            var look = img.gameObject.AddComponent<VirtualLookArea>();
            look.target = input;
        }

        void BuildJoystick(Transform parent, LocalInputSource input)
        {
            var bg = Ui.NewImage("Joystick", parent);
            bg.sprite = Ui.RoundedRectSprite(160);
            bg.type = Image.Type.Sliced;
            bg.color = new Color(Ui.WoodBrown.r, Ui.WoodBrown.g, Ui.WoodBrown.b, 0.35f);
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(320f, 320f);
            rt.anchoredPosition = new Vector2(280f, 280f);

            var handle = Ui.NewImage("Handle", bg.transform);
            handle.sprite = Ui.RoundedRectSprite(75);
            handle.type = Image.Type.Sliced;
            handle.color = new Color(1f, 1f, 1f, 0.55f);
            handle.raycastTarget = false;
            var hrt = handle.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = new Vector2(150f, 150f);

            var joy = bg.gameObject.AddComponent<VirtualJoystick>();
            joy.background = rt;
            joy.handle = hrt;
            joy.target = input;
        }

        void BuildActionButton(Transform parent, LocalInputSource input)
        {
            var hit = Ui.ToyButton("ActionButton", parent, 230f, 230f, Ui.Yellow);
            var rt = hit.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-260f, 250f);

            var vab = hit.gameObject.AddComponent<VirtualActionButton>();
            vab.target = input;
        }

        void BuildJumpButton(Transform parent, LocalInputSource input)
        {
            var hit = Ui.ToyButton("JumpButton", parent, 184f, 184f, Ui.JungleGreen);
            var rt = hit.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-507f, 250f);

            var vjb = hit.gameObject.AddComponent<VirtualJumpButton>();
            vjb.target = input;

            var label = Ui.NewText("Label", hit.transform, 30);
            if (label != null)
            {
                label.text = "JUMP";
                label.color = Color.white;
                label.raycastTarget = false;
                Ui.Stretch(label.rectTransform);
            }
        }

        // NetScoreUI.cs is untouched: it only reads mc and writes into scoreText/bannerText, so
        // building the two Text objects here and handing them to the existing component in the scene
        // is the whole migration — no new score/timer/banner logic to get wrong.
        void BuildScoreAndBanner(Transform parent)
        {
            var panel = Ui.Panel("ScorePanel", parent, 760f, 96f,
                new Color(Ui.Cream.r, Ui.Cream.g, Ui.Cream.b, 0.82f));
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.anchoredPosition = new Vector2(0f, -12f);

            var scoreText = Ui.NewText("ScoreText", panel.transform, 40);
            if (scoreText != null)
            {
                Ui.Stretch(scoreText.rectTransform);
                scoreText.color = Ui.Ink;
                scoreText.raycastTarget = false;
            }

            var bannerText = Ui.NewText("BannerText", parent, 160);
            if (bannerText != null)
            {
                bannerText.fontStyle = FontStyle.Bold;
                bannerText.color = Color.white;
                bannerText.raycastTarget = false;
                bannerText.enabled = false; // NetScoreUI.Update() turns it on for a countdown/GOAL!/winner

                var brt = bannerText.rectTransform;
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.sizeDelta = new Vector2(1000f, 300f);
                brt.anchoredPosition = new Vector2(0f, 120f);

                var outline = bannerText.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(5f, -5f);
            }

            var scoreUI = FindAnyObjectByType<NetScoreUI>();
            if (scoreUI != null)
            {
                scoreUI.scoreText = scoreText;
                scoreUI.bannerText = bannerText;
            }
            else Debug.LogWarning("[Hud] no NetScoreUI in the scene to wire up");
        }
    }
}
