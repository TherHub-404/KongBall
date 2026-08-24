using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KongBall
{
    // Runtime skeletal animation blending for the rigged FallGuy character, built entirely from a
    // PlayableGraph — no AnimatorController asset, no Editor authoring, matching AGENTS.md #1.
    // PlayerVisual.cs attaches this to the loaded model and fills `clips` from every AnimationClip
    // shipped inside Resources/Player/FallGuy.glb — the seven expected states are idle, run, jump,
    // fall, hit, stumble, spin.
    //
    // Jump/fall/run are read off the MODEL'S OWN transform position, not off NetPlayer's private
    // _grounded/_vY: those two are only ever written by the peer simulating that player (the state
    // authority), so on every other client watching a remote player they would just sit at their
    // default forever. The replicated position is the one thing every client actually has, so
    // velocity is estimated from its own frame-to-frame delta instead — noisier than reading the real
    // value, but correct on every screen this plays on, not only the owner's.
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

        [Header("Landing squash — presentation only, same juice family as Hitstop/MatchCamera.Shake")]
        [Tooltip("How much the model compresses vertically the instant it lands, easing back to " +
                 "normal over squashDuration. 0 disables it.")]
        public float squashAmount = 0.22f;
        public float squashDuration = 0.16f;

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

        // Captured lazily on the first Update rather than in Awake: PlayerVisual sets this model's
        // localScale to fit the CharacterController's height AFTER AddComponent<RigAnimator>() (and
        // therefore after Awake runs), so an Awake-time capture would freeze in the pre-fit scale —
        // the squash would then permanently override the real fitted size instead of riding on top of
        // it. Every frame re-derives from this cached base, never accumulates (AGENTS.md #8).
        Vector3 _baseScale;
        bool _baseScaleCaptured;
        bool _prevAirborne;
        float _squashT = -1f;   // seconds since the last landing; -1 = inactive

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

            if (!_baseScaleCaptured) { _baseScale = transform.localScale; _baseScaleCaptured = true; }

            Vector3 pos = _player.transform.position;
            Vector3 vel = (pos - _lastPos) / dt;
            _lastPos = pos;

            float hSpeed = new Vector2(vel.x, vel.z).magnitude;
            bool airborne = Mathf.Abs(vel.y) > airThreshold;

            // Landing: jump/fall had nothing to sell the moment the model actually touches down, the
            // same gap hitstop/shake used to fill on a hit. A brief squash-and-stretch on the model's
            // own scale needs no new clip and cannot desync anything — it never touches the
            // CharacterController, only how the mesh looks for squashDuration.
            if (_prevAirborne && !airborne) _squashT = 0f;
            _prevAirborne = airborne;

            if (_squashT >= 0f)
            {
                _squashT += dt;
                if (_squashT >= squashDuration) { _squashT = -1f; transform.localScale = _baseScale; }
                else
                {
                    float squash = squashAmount * (1f - _squashT / squashDuration); // eases to 0
                    transform.localScale = Vector3.Scale(_baseScale,
                        new Vector3(1f + squash * 0.5f, 1f - squash, 1f + squash * 0.5f));
                }
            }

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
