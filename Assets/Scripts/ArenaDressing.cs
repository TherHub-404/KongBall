using System.Collections.Generic;
using UnityEngine;

namespace KongBall
{
    // Builds the scenery around the pitch at runtime, from a handful of models loaded out of
    // Resources. Written this way for two reasons:
    //
    //  - the models are glTF assets whose sub-objects only exist after Unity imports them, so they
    //    cannot be wired into the scene by editing YAML from outside the Editor;
    //  - every position below is derived from the SAME constants as the collision boxes, so the
    //    scenery cannot drift out of alignment with the pitch. That drift is exactly what made the
    //    ball look like it was floating and the player look like he was sinking.
    //
    // Nothing here has a collider. The 58 tuned collision boxes already in the scene stay untouched:
    // this is decoration laid over them, never the thing you bounce off.
    public class ArenaDressing : MonoBehaviour
    {
        [Header("Pitch footprint (must match the collision boxes)")]
        public float halfX = 23.3f;    // goal-line walls
        public float halfZ = 13.3f;    // touchlines
        public float wallHeight = 2.5f;
        public float goalHalfZ = 3.4f; // gap in the end walls for the goal mouth

        [Header("Layout")]
        public float railSegmentWidth = 3f;
        public float standDepth = 4f;      // how far behind the rail the stands sit
        public int palmCount = 26;
        public float palmInner = 30f;      // palms live outside this radius…
        public float palmOuter = 52f;      // …and inside this one
        public int fernCount = 34;
        public int rockCount = 14;
        public int vineCount = 8;
        public int seed = 20260816;

        const string Root = "Arena/";

        void Start()
        {
            var rail  = Load("Wall_Bamboo");
            var stand = Load("Stand");
            var palm  = Load("Palm");
            var fern  = Load("Ferns");
            var rock  = Load("Rock");
            var vine  = Load("Vines");

            var holder = new GameObject("ArenaDressing").transform;
            holder.SetParent(transform, false);

            var rng = new System.Random(seed);

            BuildPerimeter(holder, rail, stand);
            ScatterRing(holder, palm, palmCount, palmInner, palmOuter, 6.5f, 8.5f, rng);
            ScatterRing(holder, fern, fernCount, halfX + 6f, palmOuter, 0.8f, 1.6f, rng);
            ScatterRing(holder, rock, rockCount, halfX + 8f, palmOuter + 6f, 0.9f, 2.2f, rng);
            ScatterRing(holder, vine, vineCount, palmInner, palmOuter, 2.5f, 4f, rng);

            // One draw call per distinct model instead of one per copy.
            StaticBatchingUtility.Combine(holder.gameObject);
        }

        static GameObject Load(string name)
        {
            var go = Resources.Load<GameObject>(Root + name);
            if (go == null) Debug.LogWarning("[Arena] missing model Resources/" + Root + name);
            return go;
        }

        // Rail along the touchlines and the end walls, with the goal mouths left open, and a row of
        // stands behind it. Both follow the measured footprint rather than eyeballed numbers.
        void BuildPerimeter(Transform holder, GameObject rail, GameObject stand)
        {
            if (rail == null && stand == null) return;

            foreach (var side in new[] { -1f, 1f })
            {
                // Touchlines: continuous.
                for (float x = -halfX; x < halfX; x += railSegmentWidth)
                {
                    float cx = x + railSegmentWidth * 0.5f;
                    Place(holder, rail, new Vector3(cx, 0f, side * halfZ), 0f, railSegmentWidth, wallHeight);
                    Place(holder, stand, new Vector3(cx, 0f, side * (halfZ + standDepth)),
                          side > 0 ? 180f : 0f, railSegmentWidth, 0f);
                }

                // End walls: skip the goal mouth.
                for (float z = -halfZ; z < halfZ; z += railSegmentWidth)
                {
                    float cz = z + railSegmentWidth * 0.5f;
                    if (Mathf.Abs(cz) < goalHalfZ + railSegmentWidth * 0.5f) continue;
                    Place(holder, rail, new Vector3(side * halfX, 0f, cz), 90f, railSegmentWidth, wallHeight);
                    Place(holder, stand, new Vector3(side * (halfX + standDepth), 0f, cz),
                          side > 0 ? 270f : 90f, railSegmentWidth, 0f);
                }
            }
        }

        // Random placement in a ring around the pitch, never inside it.
        void ScatterRing(Transform holder, GameObject prefab, int count, float inner, float outer,
                         float minH, float maxH, System.Random rng)
        {
            if (prefab == null) return;
            for (int i = 0; i < count; i++)
            {
                // Spread by angle so the ring stays even instead of clumping.
                float ang = (i / (float)count) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.25f;
                float r = Mathf.Lerp(inner, outer, (float)rng.NextDouble());
                var pos = new Vector3(Mathf.Cos(ang) * r * 1.35f, 0f, Mathf.Sin(ang) * r);
                if (Mathf.Abs(pos.x) < halfX + 3f && Mathf.Abs(pos.z) < halfZ + 3f) continue; // keep off the pitch
                Place(holder, prefab, pos, (float)rng.NextDouble() * 360f,
                      0f, Mathf.Lerp(minH, maxH, (float)rng.NextDouble()));
            }
        }

        // Instantiates one copy, scaled so it ends up the size we want in world units and sitting on
        // the ground. Generated models arrive at arbitrary scale with arbitrary pivots, so both are
        // derived from the actual mesh bounds instead of assumed.
        static void Place(Transform holder, GameObject prefab, Vector3 pos, float yaw,
                          float targetWidth, float targetHeight)
        {
            if (prefab == null) return;

            var go = Instantiate(prefab, holder);
            var b = WorldBounds(go);
            if (b.size.y > 1e-4f)
            {
                float s = targetHeight > 0f ? targetHeight / b.size.y
                        : targetWidth  > 0f ? targetWidth  / Mathf.Max(b.size.x, b.size.z)
                        : 1f;
                go.transform.localScale = Vector3.one * s;
                b = WorldBounds(go);
            }
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            // Drop it so its lowest point rests on y=0, whatever its pivot happens to be.
            b = WorldBounds(go);
            float lift = go.transform.position.y - b.min.y;
            go.transform.position = pos + Vector3.up * lift;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }
    }
}
