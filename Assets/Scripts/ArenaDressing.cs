using UnityEngine;

namespace KongBall
{
    // Turns the primitive arena into a Caribbean beach: green pitch in the middle, bamboo rail and
    // thatched stands around it, palms and vegetation beyond, sand out to the horizon.
    //
    // Built at runtime from models in Resources, for two reasons:
    //  - glTF models are ScriptedImporter sub-assets whose internal ids only exist once Unity has
    //    imported them, so they cannot be referenced from the scene by editing YAML;
    //  - every position derives from the SAME constants as the collision boxes, so the scenery
    //    cannot drift out of alignment with the pitch. That drift is what made the ball look like it
    //    floated and the player look like he sank.
    //
    // Nothing here has a collider, and nothing here is ever the surface you bounce off: the 58 tuned
    // collision boxes already in the scene are left exactly as they are.
    public class ArenaDressing : MonoBehaviour
    {
        [Header("Pitch footprint — mirrors the collision boxes")]
        public float halfX = 23.3f;      // end walls
        public float halfZ = 13.3f;      // touchlines
        public float wallHeight = 2.5f;
        public float goalHalfZ = 3.4f;   // gap in the end walls for the goal mouth

        [Header("Layout")]
        public float railSegment = 3f;
        public float standDepth = 5f;
        public int palms = 26;
        public int ferns = 34;
        public int rocks = 16;
        public int vines = 10;
        public float scatterInner = 30f;
        public float scatterOuter = 55f;
        public float sandRadius = 140f;
        public int seed = 20260816;

        // The ferns arrived with an unwanted base under them. Sinking them slightly buries it in the
        // sand and leaves only the foliage, which is the part worth keeping.
        [Header("Corrections for the generated models")]
        public float fernSink = 0.18f;   // fraction of height pushed underground
        public float vineHangHeight = 4.5f;

        const string Root = "Arena/";

        // Installs itself: the scene cannot be edited from outside Unity, and this keeps the
        // decoration independent of NetLauncher instead of bolting it onto the networking bootstrap.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<ArenaDressing>() != null) return;
            new GameObject("ArenaDressing").AddComponent<ArenaDressing>();
        }

        void Start()
        {
            HidePrimitiveWalls();

            var holder = new GameObject("Scenery").transform;
            holder.SetParent(transform, false);

            var rng = new System.Random(seed);
            BuildSand(holder);
            BuildPerimeter(holder, Load("Wall_Bamboo"), Load("Stand"));
            Scatter(holder, Load("Palm"),  palms, 6.5f, 9f,   rng, 0f);
            Scatter(holder, Load("Ferns"), ferns, 0.9f, 1.7f, rng, fernSink);
            Scatter(holder, Load("Rock"),  rocks, 0.8f, 2.2f, rng, 0.1f);
            HangVines(holder, Load("Vines"), rng);

            // One draw call per model instead of one per copy — the difference between this being
            // affordable on a phone and not.
            StaticBatchingUtility.Combine(holder.gameObject);
        }

        static GameObject Load(string n)
        {
            var go = Resources.Load<GameObject>(Root + n);
            if (go == null) Debug.LogWarning("[Arena] missing Resources/" + Root + n);
            return go;
        }

        // The white boxes around the pitch are the SAME objects that carry the colliders, so only
        // their renderers go. Disabling the objects would delete the arena's physics.
        void HidePrimitiveWalls()
        {
            var walls = GameObject.Find("Walls");
            if (walls == null) { Debug.LogWarning("[Arena] no 'Walls' object to hide"); return; }
            int n = 0;
            foreach (var r in walls.GetComponentsInChildren<Renderer>(true)) { r.enabled = false; n++; }
            Debug.Log("[Arena] hidden " + n + " primitive wall renderers (colliders untouched)");
        }

        // A wide sand plane just under the pitch, so the world does not end in grey void. Sits below
        // the playing surface and carries no collider: the floor stays the Ground box.
        void BuildSand(Transform holder)
        {
            var sand = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(sand.GetComponent<Collider>());
            sand.name = "Sand";
            sand.transform.SetParent(holder, false);
            sand.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            sand.transform.position = new Vector3(0f, -0.05f, 0f);
            sand.transform.localScale = new Vector3(sandRadius * 2f, sandRadius * 2f, 1f);

            var mr = sand.GetComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(mr.sharedMaterial) { color = new Color(0.93f, 0.87f, 0.71f) };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void BuildPerimeter(Transform holder, GameObject rail, GameObject stand)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                for (float x = -halfX; x < halfX; x += railSegment)
                {
                    float cx = x + railSegment * 0.5f;
                    Place(holder, rail, new Vector3(cx, 0f, side * halfZ), 0f, railSegment, wallHeight, 0f);
                    Place(holder, stand, new Vector3(cx, 0f, side * (halfZ + standDepth)),
                          side > 0f ? 180f : 0f, railSegment * 1.4f, 0f, 0f);
                }

                for (float z = -halfZ; z < halfZ; z += railSegment)
                {
                    float cz = z + railSegment * 0.5f;
                    if (Mathf.Abs(cz) < goalHalfZ + railSegment * 0.5f) continue;   // leave the goal mouth open
                    Place(holder, rail, new Vector3(side * halfX, 0f, cz), 90f, railSegment, wallHeight, 0f);
                    Place(holder, stand, new Vector3(side * (halfX + standDepth), 0f, cz),
                          side > 0f ? 270f : 90f, railSegment * 1.4f, 0f, 0f);
                }
            }
        }

        // Scattered in an ellipse around the pitch, never on it.
        void Scatter(Transform holder, GameObject prefab, int count, float minH, float maxH,
                     System.Random rng, float sink)
        {
            if (prefab == null) return;
            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.3f;
                float r = Mathf.Lerp(scatterInner, scatterOuter, (float)rng.NextDouble());
                var pos = new Vector3(Mathf.Cos(ang) * r * 1.4f, 0f, Mathf.Sin(ang) * r);
                if (Mathf.Abs(pos.x) < halfX + standDepth + 4f && Mathf.Abs(pos.z) < halfZ + standDepth + 4f) continue;
                Place(holder, prefab, pos, (float)rng.NextDouble() * 360f,
                      0f, Mathf.Lerp(minH, maxH, (float)rng.NextDouble()), sink);
            }
        }

        // Vines hang from an anchor at the top, so unlike everything else they are positioned by
        // their HIGHEST point rather than dropped onto the ground.
        void HangVines(Transform holder, GameObject prefab, System.Random rng)
        {
            if (prefab == null) return;
            for (int i = 0; i < vines; i++)
            {
                float ang = (i / (float)vines) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.4f;
                float r = Mathf.Lerp(scatterInner, scatterOuter, (float)rng.NextDouble());
                var pos = new Vector3(Mathf.Cos(ang) * r * 1.4f, 0f, Mathf.Sin(ang) * r);

                var go = Instantiate(prefab, holder);
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                var b = WorldBounds(go);
                if (b.size.y > 1e-4f)
                {
                    go.transform.localScale = Vector3.one * (Mathf.Lerp(2.5f, 4f, (float)rng.NextDouble()) / b.size.y);
                    b = WorldBounds(go);
                }
                float drop = b.max.y - go.transform.position.y;
                go.transform.position = pos + Vector3.up * (vineHangHeight - drop);
            }
        }

        // Generated models arrive at arbitrary scale with arbitrary pivots, so both are measured off
        // the instantiated copy rather than assumed. Assuming a pivot is exactly what put the ball
        // half a diameter above its own collider.
        static void Place(Transform holder, GameObject prefab, Vector3 pos, float yaw,
                          float targetWidth, float targetHeight, float sink)
        {
            if (prefab == null) return;

            var go = Instantiate(prefab, holder);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var b = WorldBounds(go);
            float basis = targetHeight > 0f ? b.size.y : Mathf.Max(b.size.x, b.size.z);
            float target = targetHeight > 0f ? targetHeight : targetWidth;
            if (basis > 1e-4f && target > 0f)
            {
                go.transform.localScale = Vector3.one * (target / basis);
                b = WorldBounds(go);
            }

            // Drop it so its lowest point rests on the ground, whatever the pivot happens to be.
            float lift = go.transform.position.y - b.min.y;
            go.transform.position = pos + Vector3.up * (lift - b.size.y * sink);
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
