using UnityEngine;

namespace KongBall
{
    // Small dust puffs at the feet while sprinting at (near) full speed — presentation only, every
    // client runs this locally off the same replicated position NetPlayer already exposes, so it
    // needs no networking of its own (same reasoning as MonkeyAnimator/RigAnimator).
    //
    // Built entirely from code: a ParticleSystem configured through its modules, never authored in
    // the Editor (AGENTS.md #1) — there is no scene, prefab or asset backing this at all.
    public class RunDust : MonoBehaviour
    {
        [Tooltip("Fraction of moveSpeed above which the player counts as \"full regime\" and kicks up dust.")]
        public float fullSpeedThreshold = 0.85f;

        NetPlayer _player;
        ParticleSystem _ps;
        ParticleSystem.EmissionModule _emission;

        void Awake()
        {
            _player = GetComponentInParent<NetPlayer>();

            var go = new GameObject("RunDust");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            _ps = go.AddComponent<ParticleSystem>();
            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 0.5f;
            main.startSpeed = 0.6f;
            main.startSize = 0.35f;
            main.startColor = new Color(0.62f, 0.52f, 0.35f, 0.55f);   // dusty tan, matches the pitch
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // puffs stay put, not dragged along

            _emission = _ps.emission;
            _emission.rateOverTime = 0f;   // driven by Update() below, not a constant rate

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.25f;

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(main.startColor.color, 0f), new GradientColorKey(main.startColor.color, 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var size = _ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.2f));

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            rend.material.SetColor("_BaseColor", Color.white);   // tint comes from colorOverLifetime instead

            _ps.Play();
        }

        void Update()
        {
            if (_player == null) return;
            bool kicking = _player.Grounded && _player.SpeedFraction01 >= fullSpeedThreshold;
            _emission.rateOverTime = kicking ? 14f : 0f;
        }
    }
}
