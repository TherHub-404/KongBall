namespace CalcioStumble
{
    public enum Team { Blue, Red }

    // Network-friendly explicit states (phase 2 will sync these).
    // Normal   = full control
    // Stumbled = knocked down by a push, temporarily inert + invulnerable
    // Restrained = held by an opponent, temporarily inert + invulnerable
    public enum PlayerState { Normal, Stumbled, Restrained }

    // What the (controlled) player is actively DOING. Kept orthogonal to PlayerState
    // (which is the condition imposed on the player). One enum instead of boolean-soup.
    public enum PlayerAction { None, Aiming, Grabbing }

    public enum MatchState { Title, Playing, GoalPause, Finished }
}
