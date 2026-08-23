using UnityEngine;

namespace KongBall
{
    // What a player wants to do this tick, in world space, whoever wanted it.
    //
    // It exists so that NetPlayer has exactly ONE consumer of intent and never asks where the values
    // came from. Everything below this struct — acceleration, turning, ball hits, push, grab — is
    // therefore shared by construction, which is the only way a bot can be held to the same rules
    // as a person. If a bot ever needs a value a human cannot produce, the design is wrong.
    public struct PlayerIntent
    {
        // WORLD direction, length 0..1. The joystick is screen-relative and is resolved against the
        // camera before it gets here, because the camera belongs to the human who has one; a bot has
        // none and names a world direction outright.
        public Vector3 Move;

        // The one contextual button: hit the ball when it is in range, push or grab an opponent
        // otherwise. THE BALL IS NEVER POSSESSED — there is no "release to shoot" any more, a hit
        // fires the instant Action goes from released to pressed while the ball is close. Direction
        // and power are no longer part of the intent: NetPlayer reads them off Move/facing and a
        // fixed constant, because there is no carry window left in which to aim one (CORE_GAMEPLAY_
        // RESET section 07).
        public bool Action;
    }

    // Something that plays without a person behind it. This interface is the one name the rest of the
    // game knows from Assets/Scripts/Bots — it lives out here, with the player contract it belongs
    // to, so that the folder holds implementations only.
    public interface IPlayerBrain
    {
        PlayerIntent Think(NetPlayer me, float dt);

        // Edge-triggered, and deliberately NOT a field on the intent: it is polled at the moment the
        // jump is actually used, so a press made while stunned survives the stun instead of being
        // swallowed by whichever tick happened to read it first.
        bool ConsumeJump();
    }
}
