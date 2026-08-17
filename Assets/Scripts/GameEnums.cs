namespace KongBall
{
    // Team side. Networked as an int on NetPlayer (0 = Blue, 1 = Red).
    public enum Team { Blue, Red }

    // The value IS the number of players the session holds. That is deliberate: the same number is
    // the Fusion PlayerCount, the matchmaking filter, and the count the waiting room waits for, so
    // the three can never disagree.
    public enum MatchMode { OneVsOne = 2, TwoVsTwo = 4 }
}
