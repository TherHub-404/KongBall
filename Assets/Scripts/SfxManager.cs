using UnityEngine;

namespace CalcioStumble
{
    // Minimal juice: procedurally-generated SFX (no external audio assets needed).
    // Clips are built at runtime, so there are no serialized-asset persistence issues.
    public class SfxManager : MonoBehaviour
    {
        public static SfxManager Instance { get; private set; }

        const int SR = 44100;
        AudioSource _src;
        AudioClip _kick, _impact, _goal;

        void Awake()
        {
            Instance = this;
            _src = gameObject.GetComponent<AudioSource>();
            if (_src == null) _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;

            _kick = MakeKick();
            _impact = MakeImpact();
            _goal = MakeGoal();
        }

        public void PlayKick() { if (_kick != null) _src.PlayOneShot(_kick, 0.9f); }
        public void PlayImpact() { if (_impact != null) _src.PlayOneShot(_impact, 0.85f); }
        public void PlayGoal() { if (_goal != null) _src.PlayOneShot(_goal, 1f); }

        // punchy low thump with a downward pitch sweep
        AudioClip MakeKick()
        {
            int n = (int)(SR * 0.14f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Exp(-9f * t);
                float freq = Mathf.Lerp(230f, 90f, t);
                float phase = 2f * Mathf.PI * freq * (i / (float)SR);
                float click = (i < 200) ? (1f - i / 200f) * 0.4f : 0f;
                d[i] = (Mathf.Sin(phase) * env + click) * 0.9f;
            }
            var c = AudioClip.Create("kick", n, 1, SR, false); c.SetData(d, 0); return c;
        }

        // short noisy "thwack" with body
        AudioClip MakeImpact()
        {
            int n = (int)(SR * 0.11f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Exp(-16f * t);
                float noise = Random.value * 2f - 1f;
                float body = Mathf.Sin(2f * Mathf.PI * 150f * (i / (float)SR));
                d[i] = (noise * 0.6f + body * 0.4f) * env * 0.85f;
            }
            var c = AudioClip.Create("impact", n, 1, SR, false); c.SetData(d, 0); return c;
        }

        // three ascending notes (C5, E5, G5) — cheerful goal jingle
        AudioClip MakeGoal()
        {
            float[] notes = { 523.25f, 659.25f, 783.99f };
            float noteDur = 0.15f;
            int per = (int)(SR * noteDur);
            int n = per * notes.Length;
            var d = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                for (int i = 0; i < per; i++)
                {
                    float t = (float)i / per;
                    float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)) * Mathf.Exp(-2.2f * t);
                    float phase = 2f * Mathf.PI * notes[k] * (i / (float)SR);
                    d[k * per + i] = (Mathf.Sin(phase) + 0.3f * Mathf.Sin(2f * phase)) * env * 0.5f;
                }
            }
            var c = AudioClip.Create("goal", n, 1, SR, false); c.SetData(d, 0); return c;
        }
    }
}
