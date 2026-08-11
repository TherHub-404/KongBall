using UnityEngine;

namespace CalcioStumble
{
    // Attached to every player (controlled or idle). Holds team + controlled flag,
    // and toggles the yellow ground ring for the controlled player.
    public class TeamMember : MonoBehaviour
    {
        public Team team;
        public bool isControlled;

        [Tooltip("Yellow ground ring shown under the controlled player. Assigned dynamically by GameManager.")]
        public GameObject controlledIndicator;

        public void SetControlled(bool value)
        {
            isControlled = value;
            if (controlledIndicator != null) controlledIndicator.SetActive(value);
        }
    }
}
