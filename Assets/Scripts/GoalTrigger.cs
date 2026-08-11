using UnityEngine;

namespace CalcioStumble
{
    // Trigger placed behind a goal line. The scoring team is the opponent of the
    // team that defends this goal.
    public class GoalTrigger : MonoBehaviour
    {
        [Tooltip("Team that DEFENDS this goal. The scoring team is the opposite one.")]
        public Team defendingTeam;

        bool _armed = true;

        void OnTriggerEnter(Collider other)
        {
            if (!_armed) return;
            BallController ball = other.GetComponent<BallController>();
            if (ball == null) ball = other.GetComponentInParent<BallController>();
            if (ball == null) return;

            _armed = false;
            Team scorer = defendingTeam == Team.Blue ? Team.Red : Team.Blue;
            if (GameManager.Instance != null) GameManager.Instance.OnGoal(scorer);
            Invoke(nameof(Rearm), 1f);
        }

        void Rearm() { _armed = true; }
    }
}
