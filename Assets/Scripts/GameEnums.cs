namespace KongBall
{
    // Team side. Networked as an int on NetPlayer (0 = Blue, 1 = Red).
    public enum Team { Blue, Red }

    // The value IS the number of HUMANS the session holds. That is deliberate: the same number is the
    // Fusion PlayerCount and the matchmaking filter, so the two can never disagree.
    //
    // It is no longer the number the waiting room waits for, because a practice match holds one human
    // and starts with two players on the pitch — see NetLauncher.RequiredBodies.
    public enum MatchMode { Practice = 1, OneVsOne = 2, TwoVsTwo = 4 }
}
