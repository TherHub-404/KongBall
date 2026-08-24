using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KongBall
{
    // Runtime skeletal animation blending for a RIGGED character, built entirely from a PlayableGraph
    // — no AnimatorController asset, no Editor authoring, matching AGENTS.md #1 ("nobody here opens
    // the Unity Editor"). This is scaffolding: nothing attaches it to a prefab yet, and the clip list
    // is empty until a real rigged model exists (see the PR — no ripped or unlicensed model goes in
    // here, ever). It reads the exact same NetPlayer state MonkeyAnimator.cs already reads for the
    // current rig-less mesh; once a real rig is wired in, the two can live side by side per model, or
    // this one can replace MonkeyAnimator on the day the real model ships.
    //
    // Whoever plugs in the first clip set: assign the six entries below (idle, run, jump, fall, hit,
    // stumble) in code or via the inspector on whatever prefab ends up carrying this, and the
    // crossfade/state logic already works — nothing here has been felt on a phone, because there is
    // nothing to feel it with yet.
    public class RigAnimator : MonoBehaviour
    {
        [System.Serializable]
        public struct NamedClip
        {
            public string state;
            public AnimationClip clip;
            public bool loop;
            // Playback rate on the underlying AnimationClipPlayable — 0 would freeze the clip on its
            // first frame, so every caller must set this explicitly; there is no implicit "1 if
            // unset" here.
            public float speed;
            // When true, the EFFECTIVE speed each frame is speed * the player's current move-speed
            // fraction (clamped), not the flat value — "run" uses this so the leg/arm cycle visibly
            // quickens as the player actually gets up to speed, instead of always cycling at one
            // fixed cadence regardless of how fast they're really moving.
            public bool speedFollowsMovement;
        }

        [Tooltip("Fill in once a rigged model + clips exist. Expected states: idle, run, jump, fall, " +
                 "hit, stumble, spin — anything not present just doesn't play (no throw), same 'a " +
                 "screen without X is poor, one that throws is worse' rule as Ui.cs.")]
        public NamedClip[] clips = new NamedClip[0];

        [Tooltip("How fast a state's weight ramps to 1 when it becomes active. Not measured — start " +
                 "here and adjust once there is a real clip to look at.")]
        public float crossfadeSpeed = 8f;

        [Header("Same thresholds MonkeyAnimator.cs uses, kept independent so either can be tuned " +
                 "without touching the other")]
        public float airThreshold = 1.3f;
        public float runRefSpeed = 6f;

        NetPlayer _player;
        Animator _animatorTarget;   // required by AnimationPlayableOutput as the bind target; carries
                                     // no RuntimeAnimatorController — the graph below is the real driver
        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        AnimationClipPlayable[] _clipPlayables;
        readonly Dictionary<string, int> _slotOf = new Dictionary<string, int>();
        float[] _weights;
        string _current;
        int _lastKickSeq;
        Vector3 _lastPos;
        bool _graphValid;

        void Awake()
        {
            _player = GetComponentInParent<NetPlayer>();
            _lastPos = transform.position;

            _animatorTarget = GetComponent<Animator>();
            if (_animatorTarget == null) _animatorTarget = gameObject.AddComponent<Animator>();
            _animatorTarget.runtimeAnimatorController = null; // the graph drives everything, not Mecanim

            BuildGraph();
        }

        void BuildGraph()
        {
            if (clips == null || clips.Length == 0) return; // nothing to blend yet — stay inert

            _graph = PlayableGraph.Create("RigAnimator:" + name);
            var output = AnimationPlayableOutput.Create(_graph, "Output", _animatorTarget);

            _mixer = AnimationMixerPlayable.Create(_graph, clips.Length, false);
            _weights = new float[clips.Length];
            _clipPlayables = new AnimationClipPlayable[clips.Length];

            for (int i = 0; i < clips.Length; i++)
            {
                var entry = clips[i];
                if (entry.clip == null) continue;
                var clipPlayable = AnimationClipPlayable.Create(_graph, entry.clip);
                clipPlayable.SetApplyFootIK(false);
                clipPlayable.SetSpeed(entry.speed);
                _graph.Connect(clipPlayable, 0, _mixer, i);
                _mixer.SetInputWeight(i, 0f);
                _slotOf[entry.state] = i;
                _clipPlayables[i] = clipPlayable;
            }

            output.SetSourcePlayable(_mixer);
            _graph.Play();
            _graphValid = true;
        }

        void OnDestroy()
        {
            if (_graphValid) _graph.Destroy();
        }

        void Update()
        {
            if (!_graphValid || _player == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 pos = _player.transform.position;
            Vector3 vel = (pos - _lastPos) / dt;
            _lastPos = pos;

            float hSpeed = new Vector2(vel.x, vel.z).magnitude;
            bool airborne = Mathf.Abs(vel.y) > airThreshold;

            if (_player.KickSeq != _lastKickSeq) { _lastKickSeq = _player.KickSeq; CrossFadeTo("hit"); }
            else if (_player.IsStumbled) CrossFadeTo("stumble");
            else if (_player.IsSpinning) CrossFadeTo("spin");
            else if (airborne) CrossFadeTo(vel.y > 0f ? "jump" : "fall");
            else if (hSpeed > runRefSpeed * 0.15f) CrossFadeTo("run");
            else CrossFadeTo("idle");

            for (int i = 0; i < _weights.Length; i++)
            {
                float target = (_current != null && _slotOf.TryGetValue(_current, out int slot) && slot == i) ? 1f : 0f;
                _weights[i] = Mathf.MoveTowards(_weights[i], target, crossfadeSpeed * dt);
                _mixer.SetInputWeight(i, _weights[i]);

                // Phone-test feedback: running felt the same regardless of actual speed — the cycle
                // has to visibly quicken as the player builds up to a full sprint, not play back at
                // one fixed cadence the whole time. Clamped so it never fully freezes at a near-stop.
                if (clips[i].speedFollowsMovement)
                {
                    float mul = Mathf.Clamp(_player.SpeedFraction01, 0.4f, 1.15f);
                    _clipPlayables[i].SetSpeed(clips[i].speed * mul);
                }
            }
        }

        // Falls back to whatever is already playing when the requested state has no clip assigned —
        // a missing clip should read as "nothing changed", never as a snap to silence.
        void CrossFadeTo(string state)
        {
            if (state == _current) return;
            if (!_slotOf.ContainsKey(state)) return;
            _current = state;
        }
    }
}
