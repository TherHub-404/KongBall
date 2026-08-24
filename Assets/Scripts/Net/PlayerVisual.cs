using System;
using UnityEngine;

namespace KongBall
{
    // Loads the rigged FallGuy model at runtime and wires it into RigAnimator — no Editor
    // authoring, matching AGENTS.md #1. Replaces the static mesh + MonkeyAnimator that used to be
    // baked directly into NetPlayer.prefab's "Visual" node, the same way MenuStage.Build already
    // loads Menu/Monkey through Resources instead of a scene reference.
    public class PlayerVisual : MonoBehaviour
    {
        // "stumble" plays once — a fall-backward-then-get-up clip timed to NetPlayer.stunDuration —
        // not a continuous loop, so it can hold its final (standing) pose until IsStumbled clears.
        static readonly string[] LoopStates = { "idle", "walk", "run", "fall", "spin", "celebrate" };

        void Awake()
        {
            var prefab = Resources.Load<GameObject>("Player/FallGuy");
            if (prefab == null) { Debug.LogWarning("[NetPlayer] Resources/Player/FallGuy missing"); return; }

            var model = Instantiate(prefab, transform, false);

            // AddComponent on an ACTIVE GameObject calls Awake() immediately — RigAnimator.Awake()
            // would run BuildGraph() against an empty clips array before the loop below ever gets to
            // fill it in. Deactivating first defers Awake until SetActive(true), by which point clips
            // is already populated.
            model.SetActive(false);
            var clips = Resources.LoadAll<AnimationClip>("Player/FallGuy");
            var rig = model.AddComponent<RigAnimator>();
            rig.clips = new RigAnimator.NamedClip[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                string name = clips[i].name;
                bool loop = Array.IndexOf(LoopStates, name) >= 0;
                // AnimationClipPlayable loops or holds its last frame based on the clip's own
                // wrapMode, not on RigAnimator.NamedClip.loop (that field isn't read anywhere in
                // RigAnimator) — glTFast imports every clip as WrapMode.Loop by default, which would
                // make one-shot states like "jump"/"hit" repeat forever instead of holding.
                clips[i].wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
                // "hit" plays slower on request (a punch that snapped by too fast to read); "walk"
                // and "run" tie their cadence to actual move speed instead of a flat rate — each
                // within its own state now (see RigAnimator's runCrossoverFraction), not one clip
                // scaled across the whole speed range.
                float speed = name == "hit" ? 0.6f : 1f;
                bool followsMovement = name == "walk" || name == "run";
                rig.clips[i] = new RigAnimator.NamedClip
                {
                    state = name, clip = clips[i], loop = loop, speed = speed, speedFollowsMovement = followsMovement
                };
            }
            model.SetActive(true);

            // Measure, don't assume: plant the model's feet on the CharacterController's own floor
            // instead of trusting a fixed pivot/scale that was tuned for a different mesh (same
            // reasoning as MenuStage.Build, which measures Menu/Monkey the same way — see AGENTS.md
            // #10 on the arena that sat mis-scaled for months because nobody measured it).
            var cc = GetComponentInParent<CharacterController>();
            Bounds b = WorldBounds(model);
            float floorWorldY = 0f;
            bool hasFloor = false;
            if (cc != null && b.size.y > 1e-4f)
            {
                model.transform.localScale = Vector3.one * (cc.height / b.size.y);
                b = WorldBounds(model);
                floorWorldY = cc.transform.position.y + cc.center.y - cc.height * 0.5f;
                model.transform.position += new Vector3(0f, floorWorldY - b.min.y, 0f);
                hasFloor = true;
            }

            if (hasFloor)
            {
                var dust = new GameObject("RunDustAnchor");
                dust.transform.SetParent(transform, false);
                dust.transform.position = new Vector3(cc.transform.position.x, floorWorldY + 0.05f, cc.transform.position.z);
                dust.AddComponent<RunDust>();
            }
        }

        static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }
}
