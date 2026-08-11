using UnityEngine;
using UnityEngine.UI;

namespace CalcioStumble
{
    // Swaps the action-button icon based on context:
    //  - controlled player OWNS the ball  -> KICK icon
    //  - otherwise                        -> GRAB icon
    public class ActionButtonIcon : MonoBehaviour
    {
        public Image iconImage;
        public Sprite kickSprite;
        public Sprite grabSprite;

        bool _lastHasBall;
        bool _init;

        void Update()
        {
            var gm = GameManager.Instance;
            bool hasBall = gm != null && gm.Ball != null && gm.Controlled != null
                           && gm.Ball.Owner == gm.Controlled;

            if (!_init || hasBall != _lastHasBall)
            {
                _init = true;
                _lastHasBall = hasBall;
                if (iconImage != null) iconImage.sprite = hasBall ? kickSprite : grabSprite;
            }
        }
    }
}
