using UnityEngine;

namespace KongBall
{
    // Trigger volume at a goal mouth. When the physical ball enters, the ball's authority
    // (the master) scores. scoringTeam = the team that scores by putting the ball in THIS goal.
    [RequireComponent(typeof(Collider))]
    public class NetGoal : MonoBehaviour
    {
        [Tooltip("Team that SCORES when the ball enters this goal. 0 = Blue, 1 = Red.")]
        public int scoringTeam;

        // Goal detection moved to NetBall (coordinate-based on the ball authority) for robustness —
        // physics triggers tunnel when the ball is a kinematic proxy on the non-authority client.
        // This component is kept only as a visual/marker; it no longer scores.
    }
}
