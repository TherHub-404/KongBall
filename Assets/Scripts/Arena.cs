using UnityEngine;

namespace KongBall
{
    // The shape of the playing area, in one place, because four different things have to agree on
    // it exactly: the wall the ball bounces off, the paint the player sees, the out-of-bounds check
    // that teleports the ball back to the centre, and the camera. They used to be four separate sets
    // of numbers — boxes in the scene, a rectangle in ArenaDressing, a hardcoded "|z| > 16" in
    // NetBall and a pair of half-extents in MatchCamera — and the moment the pitch was widened the
    // ball started resetting itself while it was still in play.
    //
    // The shape is MEASURED off Arena.glb, not chosen. The model's inner boundary was sampled every
    // degree below 3 m of height, and the table below is that boundary minus 0.8 m of clearance for
    // the wall. It is not a rectangle because the arena is not a rectangle: the first version of
    // this file fitted the largest rounded rectangle that fits inside, and left up to EIGHT METRES
    // of visible ground outside the corners that the player could see and could not walk on.
    //
    // Three things are done to the raw measurement, all of them cutting inward, never outward:
    //  - a running minimum over +-3 degrees, so a single rock does not make a spike;
    //  - mirroring on both axes, keeping the smallest of the four — the arena is genuinely lopsided,
    //    and a pitch with one wing wider than the other is not a pitch;
    //  - a cap on how fast the radius may change, so the wall cannot turn a corner sharp enough to
    //    make a bounce look like a bug.
    //
    // If Arena.glb is ever replaced this table is wrong and has to be measured again. The measuring
    // is scripted: see the pull request that introduced it.
    public static class Arena
    {
        // --- The model, and where it sits ---------------------------------------------------------
        // Uniform on purpose. The model spent months in the scene at (28, 3, 28) — non-uniform,
        // squashing a 15 m arena into a 1.15 m pancake, which is why it had been written off.
        public const float ModelScale = 41f;
        // Lifts the model's GROUND PLANE to y = 0. Not its lowest vertex, which sits 0.4 m lower.
        public const float ModelY = 7.34f;
        // The interior is not centred on the model's origin: one stand comes several metres further
        // in than the others. Shifting the model is what lets the pitch stay centred on the origin,
        // which everything else in the game assumes.
        public const float ModelX = 0f;
        public const float ModelZ = -2.5f;

        // --- The touchline ------------------------------------------------------------------------
        // One quadrant, from +x (index 0) round to +z (last index), every 5 degrees. The other three
        // quadrants are this one mirrored, which is what makes the two halves of the pitch identical
        // by construction rather than by luck.
        static readonly float[] Quadrant =
        {
            26.48f, 26.56f, 26.83f, 26.13f, 26.13f, 26.13f, 27.16f, 26.72f, 24.97f, 24.40f,
            24.10f, 23.61f, 23.61f, 23.30f, 22.95f, 22.65f, 22.19f, 22.11f, 22.11f,
        };

        public static readonly float HalfX = Quadrant[0];                    // to the goal line
        public static readonly float HalfZ = Quadrant[Quadrant.Length - 1];  // to the touchline

        public const float WallHeight = 10f;      // tall enough that a lobbed ball cannot leave
        public const float WallThickness = 0.6f;

        // --- The goals ------------------------------------------------------------------------------
        // Here rather than on NetBall because the wall has to leave a hole exactly where the goal is,
        // and the ball has to be judged in exactly that hole. Two copies of "where the goal is" is
        // one copy too many.
        public static readonly float GoalLineX = HalfX - 1.8f;   // x the ball must cross to score
        public const float GoalHalfZ = 3.4f;                     // goal mouth half-width
        public const float GoalHeight = 3f;                      // above this it is over the bar

        // Where the wall stops so the ball can reach the goal. Matches the OUTER face of the goal
        // pocket's side walls, not the mouth between them: overlapping the pocket by a few
        // centimetres costs nothing, and a gap between wall and pocket is a hole the ball leaves
        // through once every hundred matches, which is the worst kind of bug to be told about.
        public const float GoalMouthHalfZ = 3.5f;

        // Distance from the centre to the touchline in the direction of (x, z). Reads the quadrant
        // table with the direction folded into the first quadrant, so the mirroring costs nothing.
        public static float Radius(float x, float z)
        {
            float ax = Mathf.Abs(x), az = Mathf.Abs(z);
            if (ax < 1e-6f && az < 1e-6f) return Quadrant[0];
            int last = Quadrant.Length - 1;
            float t = Mathf.Atan2(az, ax) / (Mathf.PI * 0.5f) * last;
            int i = Mathf.Clamp((int)t, 0, last - 1);
            return Mathf.Lerp(Quadrant[i], Quadrant[i + 1], t - i);
        }

        // Negative inside the pitch, positive outside, in metres along the radius. Measured radially
        // rather than perpendicular to the touchline: the boundary never turns fast enough for the
        // difference to matter, and this way one table answers every question asked of it.
        public static float Distance(float x, float z)
        {
            return Mathf.Sqrt(x * x + z * z) - Radius(x, z);
        }

        public static bool Contains(Vector3 p, float slack = 0f)
        {
            return Distance(p.x, p.z) < slack;
        }

        // Pulls a point back onto the pitch along its own radius, leaving it inset metres inside the
        // touchline. A no-op for a point already inside, which is the common case.
        public static Vector3 PushInside(Vector3 p, float inset = 0f)
        {
            float d = Distance(p.x, p.z);
            if (d <= -inset) return p;
            float len = Mathf.Sqrt(p.x * p.x + p.z * p.z);
            if (len < 1e-4f) return p;
            float k = (Radius(p.x, p.z) - inset) / len;
            return new Vector3(p.x * k, p.y, p.z * k);
        }

        // The touchline as a closed polygon, one point per table entry all the way round.
        //
        // Returned as points rather than as boxes so the caller decides what to build from them —
        // ArenaDressing turns consecutive pairs into wall segments, and the same list would serve a
        // line renderer or a debug draw without knowing anything about physics.
        public static Vector3[] Outline()
        {
            int n = (Quadrant.Length - 1) * 4;
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                float ux = Mathf.Cos(a), uz = Mathf.Sin(a);
                float r = Radius(ux, uz);
                pts[i] = new Vector3(ux * r, 0f, uz * r);
            }
            return pts;
        }
    }
}
