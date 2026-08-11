using UnityEngine;
using UnityEngine.InputSystem;

namespace CalcioStumble
{
    // Centralized local input. Keyboard via the new Input System (Editor testing);
    // touch fed directly by the on-screen VirtualJoystick / VirtualActionButton.
    public class LocalInputSource : MonoBehaviour, IPlayerInputSource
    {
        InputAction _move;
        InputAction _action;   // Space, held-state
        InputAction _jump;     // Left Shift / J (editor); touch via QueueJump

        Vector2 _touchMove;
        bool _touchActionHeld;
        Vector2 _aimDelta;
        Vector2 _lookDelta;   // camera-orbit drag (consumed by MatchCamera each frame)
        bool _jumpQueued;     // edge-triggered, consumed by the player controller

        void Awake()
        {
            _move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");

            _action = new InputAction("Action", InputActionType.Button);
            _action.AddBinding("<Keyboard>/space");

            _jump = new InputAction("Jump", InputActionType.Button);
            _jump.AddBinding("<Keyboard>/leftShift");
            _jump.AddBinding("<Keyboard>/j");
        }

        void OnEnable() { _move.Enable(); _action.Enable(); _jump.Enable(); }
        void OnDisable() { _move.Disable(); _action.Disable(); _jump.Disable(); }

        void Update()
        {
            if (_jump.WasPressedThisFrame()) _jumpQueued = true;
        }

        // ---- jump (edge) ----
        public void QueueJump() { _jumpQueued = true; }              // touch button calls this
        public bool ConsumeJump() { if (_jumpQueued) { _jumpQueued = false; return true; } return false; }

        // ---- touch controls ----
        public void SetTouchMove(Vector2 v)
        {
            if (v.sqrMagnitude > 1f) v = v.normalized;
            _touchMove = v;
        }
        public void SetTouchActionHeld(bool held) { _touchActionHeld = held; }
        public void SetAimDelta(Vector2 d) { _aimDelta = d; }
        public Vector2 GetAimDelta() => _aimDelta;

        // camera-orbit look: accumulate drag, camera consumes each frame
        public void AddLookDelta(Vector2 d) { _lookDelta += d; }
        public Vector2 ConsumeLookDelta() { var v = _lookDelta; _lookDelta = Vector2.zero; return v; }

        // ---- IPlayerInputSource ----
        public Vector2 GetMove()
        {
            Vector2 kb = _move.ReadValue<Vector2>();
            Vector2 v = _touchMove.sqrMagnitude > kb.sqrMagnitude ? _touchMove : kb;
            if (v.sqrMagnitude > 1f) v = v.normalized;
            return v;
        }

        public bool GetActionHeld()
        {
            return _touchActionHeld || _action.IsPressed();
        }
    }
}
