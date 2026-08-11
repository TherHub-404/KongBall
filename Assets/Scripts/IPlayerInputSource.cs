using UnityEngine;

namespace CalcioStumble
{
    // Abstraction between input origin and gameplay. Phase 1: LocalInputSource
    // (keyboard/touch). Phase 2: a NetworkBehaviour can implement this unchanged.
    // The action button is exposed as a raw HELD state; PlayerController derives
    // tap vs hold (and press/release edges) from it, so the same logic works for
    // touch, keyboard and, later, replicated network input.
    public interface IPlayerInputSource
    {
        Vector2 GetMove();      // planar move + aim, components in [-1,1]
        bool GetActionHeld();   // true while the action button is held down
        Vector2 GetAimDelta();  // screen-pixel drag from the action button press origin (kick aiming)
    }
}
