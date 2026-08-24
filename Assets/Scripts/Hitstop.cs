using UnityEngine;

namespace KongBall
{
    // A very short presentation freeze at the instant of an impactful hit — the oldest trick for
    // making a hit feel heavier than the numbers alone produce. Only ever touches Time.timeScale,
    // which every Update()/LateUpdate()-driven presentation system already respects (RigAnimator,
    // MatchCamera) — Fusion's own simulation clock runs on its own tick independent of it, so this
    // never risks the "meccaniche fondanti" AGENTS.md #11 warns about touching.
    //
    // Local-only and deliberately so: every client triggers this off its own replicated counter read
    // (NetBall.HitSeq, NetPlayer.IsStumbled), the same pattern already used for the hit SFX and camera
    // shake — a hit freezes every screen it's visible from at the same instant, not just the thrower's.
    public static class Hitstop
    {
        static float _remaining;
        static Driver _driver;

        public static void Trigger(float seconds)
        {
            _remaining = Mathf.Max(_remaining, seconds);
            Time.timeScale = 0f;
            if (_driver == null)
            {
                var go = new GameObject("HitstopDriver");
                Object.DontDestroyOnLoad(go);
                _driver = go.AddComponent<Driver>();
            }
        }

        // Runs on Update(), not FixedUpdate: it has to keep ticking in unscaled time while
        // Time.timeScale is itself 0, which would otherwise freeze anything reading scaled deltaTime.
        class Driver : MonoBehaviour
        {
            void Update()
            {
                if (_remaining <= 0f) return;
                _remaining -= Time.unscaledDeltaTime;
                if (_remaining <= 0f) { _remaining = 0f; Time.timeScale = 1f; }
            }
        }
    }
}
