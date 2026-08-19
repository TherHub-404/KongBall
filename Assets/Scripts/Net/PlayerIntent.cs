using UnityEngine;

namespace KongBall
{
    // What a player wants to do this tick, in world space, whoever wanted it.
    //
    // It exists so that NetPlayer has exactly ONE consumer of intent and never asks where the values
    // came from. Everything below this struct — acceleration, turning, possession, push, grab, kick —
    // is therefore shared by construction, which is the only way a bot can be held to the same rules
    // as a person. If a bot ever needs a value a human cannot produce, the design is wrong.
    public struct PlayerIntent
    {
        // WORLD direction, length 0..1. The joystick is screen-relative and is resolved against the
        // camera before it gets here, because the camera belongs to the human who has one; a bot has
        // none and names a world direction outright.
        public Vector3 Move;

        // The one contextual button: kick while carrying the ball, push or grab while not.
        public bool Action;

        // Where a kick goes when Action is RELEASED while carrying, and how hard. A zero direction
        // means "straight ahead". Read only on the release tick, ignored otherwise.
        public Vector3 KickDir;
        public float KickPower;   // 0..1
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
