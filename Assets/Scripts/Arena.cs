using UnityEngine;

namespace KongBall
{
    // The shape of the playing area, in one place, because three different things have to agree on
    // it exactly: the walls the ball bounces off, the paint the player sees, and the out-of-bounds
    // check that teleports the ball back to the centre. They used to be three separate sets of
    // numbers — a rectangle in the scene, a rectangle in ArenaDressing, and a hardcoded
    // "|z| > 16" in NetBall — and the moment the pitch was widened the ball started resetting
    // itself while still in play.
    //
    // The numbers are MEASURED off Arena.glb, not chosen: the model's inner boundary was sampled
    // every degree below 3 m of height, and this is the largest rounded rectangle that fits inside
    // it with 0.8 m of clearance (half the wall thickness plus air). That is why the corners are so
    // round — the arena's interior really is that shape. Anything more square leaves the corners
    // empty; anything bigger puts the wall inside the stands.
    //
    // If Arena.glb is ever replaced, these numbers are wrong and have to be measured again. The
    // measuring is scripted: see the pull request that introduced this file.
    public static class Arena
    {
        // --- The model, and where it sits ---------------------------------------------------------
        // Uniform on purpose. The model spent months in the scene at (28, 3, 28) — non-uniform,
        // squashing a 15 m arena into a 1.15 m pancake, which is why it had been written off.
        public const float ModelScale = 41f;
        // Lifts the model's GROUND PLANE to y = 0. Not its lowest vertex, which sits 0.4 m lower.
        public const float ModelY = 7.34f;
        // The interior is not centred on the model's origin: a stand comes 8 m further in on one
        // side than the other. Shifting the model is what lets the pitch stay centred on the origin,
        // which everything else in the game assumes.
        public const float ModelX = 1f;
        public const float ModelZ = -2f;

        // --- The playing area ---------------------------------------------------------------------
        public const float HalfX = 25.1f;
        public const float HalfZ = 20.74f;
        public const float CornerRadius = 16.18f;

        public const float WallHeight = 10f;      // tall enough that a lobbed ball cannot leave
        public const float WallThickness = 0.6f;

        // --- The goals ------------------------------------------------------------------------------
        // Here rather than on NetBall because the wall has to leave a hole exactly where the goal
        // is, and the ball has to be judged in exactly that hole. Two copies of "where the goal is"
        // is one copy too many.
        public const float GoalLineX = 23.3f;     // x the ball must cross to score
        public const float GoalHalfZ = 3.4f;      // goal mouth half-width
        public const float GoalHeight = 3f;       // above this it is over the bar

        // Where the wall stops so the ball can reach the goal. Matches the OUTER face of the goal
        // pocket's side walls, not the mouth between them: overlapping the pocket by a few
        // centimetres costs nothing, and a gap between wall and pocket is a hole the ball leaves
        // through once every hundred matches, which is the worst kind of bug to be told about.
        public const float GoalMouthHalfZ = 3.5f;

        // Signed distance to the touchline: negative inside, positive outside, in metres. Exact for
        // a rounded rectangle, which is what makes it usable for all three jobs — the wall is built
        // where this is zero, the paint is drawn where it is near zero, and "in play" is where it is
        // negative.
        public static float Distance(float x, float z)
        {
            float qx = Mathf.Abs(x) - (HalfX - CornerRadius);
            float qz = Mathf.Abs(z) - (HalfZ - CornerRadius);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                       Mathf.Max(qz, 0f) * Mathf.Max(qz, 0f));
            return outside + Mathf.Min(Mathf.Max(qx, qz), 0f) - CornerRadius;
        }

        public static bool Contains(Vector3 p, float slack = 0f)
        {
            return Distance(p.x, p.z) < slack;
        }

        // Slides a point back onto the pitch along the shortest way in, leaving it inset metres
        // inside the touchline. A no-op for a point already inside, which is the common case.
        //
        // One gradient step is enough: for a rounded rectangle the gradient IS the direction of the
        // nearest boundary point everywhere except exactly on the centre lines, where the distance
        // is zero anyway.
        public static Vector3 PushInside(Vector3 p, float inset = 0f)
        {
            float d = Distance(p.x, p.z);
            if (d <= -inset) return p;
            const float e = 0.05f;
            float gx = Distance(p.x + e, p.z) - Distance(p.x - e, p.z);
            float gz = Distance(p.x, p.z + e) - Distance(p.x, p.z - e);
            float len = Mathf.Sqrt(gx * gx + gz * gz);
            if (len < 1e-5f) return p;
            float k = (d + inset) / len;
            return new Vector3(p.x - gx * k, p.y, p.z - gz * k);
        }

        // The touchline as a closed polygon, walked in order: the four straight runs joined by four
        // quarter circles. arcSteps is per corner.
        //
        // Returned as points rather than as boxes so the caller decides what to build from them —
        // ArenaDressing turns consecutive pairs into wall segments, and the same list would serve a
        // line renderer or a debug draw without knowing anything about physics.
        public static Vector3[] Outline(int arcSteps = 12)
        {
            float sx = HalfX - CornerRadius;    // where the straight runs end
            float sz = HalfZ - CornerRadius;
            var pts = new Vector3[4 * (arcSteps + 1)];
            int n = 0;
            // Four quadrants, each: the corner arc, ending on the straight run that follows it.
            for (int q = 0; q < 4; q++)
            {
                float cx = (q == 0 || q == 3) ? sx : -sx;
                float cz = (q == 0 || q == 1) ? sz : -sz;
                float from = q * 90f;           // 0 deg = +x, counter-clockwise
                for (int i = 0; i <= arcSteps; i++)
                {
                    float a = (from + 90f * i / arcSteps) * Mathf.Deg2Rad;
                    pts[n++] = new Vector3(cx + Mathf.Cos(a) * CornerRadius, 0f,
                                           cz + Mathf.Sin(a) * CornerRadius);
                }
            }
            return pts;
        }
    }
}
