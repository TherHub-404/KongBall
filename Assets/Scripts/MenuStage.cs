using UnityEngine;

namespace KongBall
{
    // The backdrop for every screen that is not the match: a plain coloured field with the monkey
    // standing in it, looking at the player.
    //
    // The menu used to be a translucent sheet laid over the live pitch, with the joystick and the
    // player visible underneath, which read as an overlay rather than as a place. This is what
    // separates the two: while it is up, the game is simply not on screen.
    //
    // It works by parking a second camera a kilometre below the arena and putting the monkey down
    // there with it. Nothing else exists at that depth, so the camera sees the character and its own
    // clear colour and nothing else — no layers to reserve, no scene to author, and the match keeps
    // running untouched behind it, which matters because during the waiting room it really is running.
    public class MenuStage : MonoBehaviour
    {
        public static readonly Color Background = new Color(0.97f, 0.78f, 0.24f);

        const float Depth = -1000f;     // far below the arena, where nothing else is
        const float MonkeyHeight = 1.8f;
        const float Fov = 40f;

        // Where the character sits vertically, as a fraction of half the screen: 0 is the middle, +1
        // the top edge. The camera is then derived from these, so the framing holds whatever the model
        // measures. Feet stop above the buttons and the head stops below the title.
        const float FeetFrac = -0.18f;
        const float HeadFrac = 0.55f;

        static MenuStage _current;
        public static bool Visible => _current != null;

        Camera _cam;
        Transform _monkey;
        Vector3 _monkeyBase;            // where Build seated it; the idle sway is applied on top
        Canvas _hud;
        UnityEngine.UI.GraphicRaycaster _hudTaps;
        float _spin;

        public static void Show()
        {
            if (_current != null) return;
            var go = new GameObject("MenuStage");
            _current = go.AddComponent<MenuStage>();
            _current.Build();
        }

        public static void Hide()
        {
            if (_current == null) return;
            _current.RestoreHud();
            Destroy(_current.gameObject);
            _current = null;
        }

        void OnDestroy()
        {
            RestoreHud();
            if (_current == this) _current = null;
        }

        void Build()
        {
            // The joystick and the score live on a Screen Space Overlay canvas, which draws above
            // every camera — so hiding the world is not enough, the controls have to go too.
            //
            // The Canvas component is switched off rather than the GameObject, for the same reason
            // the arena hides wall renderers instead of wall objects: deactivating a UI root takes
            // its scripts down with it, and something reaching for one of them by type would quietly
            // find nothing.
            var hud = GameObject.Find("HUD");
            if (hud != null)
            {
                _hud = hud.GetComponent<Canvas>();
                _hudTaps = hud.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                if (_hud != null) _hud.enabled = false;
                if (_hudTaps != null) _hudTaps.enabled = false;
            }

            var camGo = new GameObject("MenuCamera");
            camGo.transform.SetParent(transform, false);

            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Background;
            _cam.fieldOfView = Fov;         // vertical, so the framing below is the same on any screen
            _cam.nearClipPlane = 0.1f;
            // Short enough that the arena a kilometre up can never fall inside the frustum, whatever
            // the camera ends up pointing at.
            _cam.farClipPlane = 40f;
            _cam.depth = MainCameraDepth() + 10f;   // drawn last, so its clear colour covers the match
            // Provisional: FrameOn replaces it once the character has been measured. It matters only
            // if the model fails to load, so that the screen is still a clean colour field.
            camGo.transform.position = new Vector3(0f, Depth + 1f, -6f);
            camGo.transform.rotation = Quaternion.identity;

            var prefab = Resources.Load<GameObject>("Menu/Monkey");
            if (prefab == null) { Debug.LogWarning("[Menu] Resources/Menu/Monkey missing"); return; }

            var monkey = Instantiate(prefab, transform);
            _monkey = monkey.transform;
            _monkey.localScale = Vector3.one;
            _monkey.rotation = Quaternion.Euler(0f, 180f, 0f);   // in-game the model faces +Z, so turn it around

            // Same rule as the arena: measure the model, never assume its scale or its pivot.
            Bounds b = WorldBounds(monkey);
            if (b.size.y > 1e-4f)
            {
                _monkey.localScale = Vector3.one * (MonkeyHeight / b.size.y);
                b = WorldBounds(monkey);
            }
            _monkey.position += new Vector3(-b.center.x, Depth - b.min.y, -b.center.z);
            _monkeyBase = _monkey.position;

            FrameOn(b.size.y);
        }

        // Derives the camera from the character's real height instead of hard-coded distances, so the
        // composition survives a change of model. Level shot, no pitch: half the frame at the
        // character's distance is `half`, and the two fractions above say where its feet and head land
        // inside that.
        void FrameOn(float height)
        {
            if (_cam == null || height <= 1e-4f) return;
            float half = height / (HeadFrac - FeetFrac);
            float dist = half / Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            _cam.transform.position = new Vector3(0f, Depth - FeetFrac * half, -dist);
            _cam.transform.rotation = Quaternion.identity;
            _cam.farClipPlane = dist + 10f;
        }

        void RestoreHud()
        {
            if (_hud != null) _hud.enabled = true;
            if (_hudTaps != null) _hudTaps.enabled = true;
            _hud = null;
            _hudTaps = null;
        }

        void Update()
        {
            if (_monkey == null) return;
            // A slow sway and breath, so the character reads as waiting for you rather than as a
            // still image someone forgot to animate.
            //
            // Applied ON TOP of the seated position, never instead of it. Writing an absolute y here
            // is what sank the character below the frame: the model's pivot sits half way up, so
            // "position.y = ground" buried it to the eyes — the same mistake that once left the ball
            // floating above its own collider.
            _spin += Time.unscaledDeltaTime;
            float yaw = 180f + Mathf.Sin(_spin * 0.7f) * 11f;
            float bob = Mathf.Sin(_spin * 1.6f) * 0.02f;
            _monkey.rotation = Quaternion.Euler(0f, yaw, 0f);
            _monkey.position = _monkeyBase + Vector3.up * bob;
        }

        static float MainCameraDepth()
        {
            var main = Camera.main;
            return main != null ? main.depth : 0f;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }
    }
}
