using UnityEngine;

namespace CalcioStumble
{
    // Centralized tuning (bible: no magic numbers scattered in methods). Edit ONE asset to
    // tune the whole game. GameManager pushes these into players/ball at runtime.
    [CreateAssetMenu(fileName = "GameTuning", menuName = "CalcioStumble/GameTuning")]
    public class GameTuning : ScriptableObject
    {
        [Header("Movement")]
        public float moveSpeed = 8f;         // a bit faster for the bigger field
        public float acceleration = 45f;
        public float deceleration = 55f;
        public float turnSpeed = 720f;

        [Header("Kick")]
        public float kickMinForce = 3f;
        public float kickMaxForce = 9f;
        public float kickUpwardRatio = 0.12f;
        public float kickCooldown = 0.2f;

        [Header("Push / Stun")]
        public float pushRadius = 0.6f;
        public float pushForce = 14f;
        public float stunDuration = 1f;
        public float pushCooldown = 0.4f;

        [Header("Grab")]
        public float restrainMaxDuration = 1.5f;
        public float restrainDistance = 1.1f;
        public float restrainMoveMultiplier = 0.5f;
        public float grabCooldown = 0.6f;
        public float holdThreshold = 0.35f;

        [Header("Possession / Ball")]
        public float possessionRadius = 0.4f;
        public float dribbleAhead = 1.3f;
        public float dribbleSmoothTime = 0.09f;
        public float catchUpGain = 8f;
        public float maxDribbleSpeed = 10f;
        public float velocityEase = 12f;
        public float loseDistance = 2.5f;
        [Tooltip("Hard safety cap on ball speed (anti-tunnelling / readability).")]
        public float maxBallSpeed = 24f;
    }
}
