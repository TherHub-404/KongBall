using System.Collections.Generic;
using UnityEngine;

namespace KongBall
{
    // Turns the primitive arena into a Caribbean beach: the pitch in the middle, a bamboo rail and
    // thatched stands around it, palms and vegetation beyond, sand out to the horizon.
    //
    // Built at runtime from models in Resources, for two reasons:
    //  - glTF models are ScriptedImporter sub-assets whose internal ids only exist once Unity has
    //    imported them, so they cannot be referenced from the scene by editing YAML;
    //  - every position derives from the SAME constants as the collision boxes, so the scenery
    //    cannot drift out of alignment with the pitch. That drift is what made the ball look like it
    //    floated and the player look like he sank.
    //
    // Two rules hold everywhere below:
    //  - nothing here has a collider. The 58 tuned collision boxes already in the scene are the only
    //    physics, and they are left exactly as they are.
    //  - no model's size or pivot is ever assumed. Both are measured off the instantiated copy and
    //    the copy is then seated against the numbers above. Assuming a pivot is precisely what put
    //    the ball half a diameter above its own collider.
    public class ArenaDressing : MonoBehaviour
    {
        [Header("Pitch footprint — mirrors the collision boxes")]
        public float halfX = 23.3f;      // end walls
        public float halfZ = 13.3f;      // touchlines
        public float wallHeight = 2.5f;
        public float goalHalfZ = 3.7f;   // gap in the end walls: GL_x_top/bot start at exactly z=+-3.7

        [Header("Stands")]
        public float railSegment = 3f;   // nominal; the real width is derived so segments tile exactly
        public float standHeight = 3f;
        public float standGap = 0.5f;    // clear space between rail and stand fronts

        [Header("Vegetation")]
        public int palms = 26;
        public int ferns = 34;
        public int rocks = 16;
        public int vines = 10;
        public float beltNear = 1.5f;    // metres beyond the stands where planting starts
        public float beltFar = 30f;
        public float sandRadius = 140f;
        public int seed = 20260816;

        // The ferns arrived with an unwanted base under them. Sinking them slightly buries it in the
        // sand and leaves only the foliage, which is the part worth keeping.
        [Header("Corrections for the generated models")]
        public float fernSink = 0.18f;   // fraction of height pushed underground

        const string Root = "Arena/";

        readonly List<Vector4> _canopies = new List<Vector4>();   // xyz = palm crown centre, w = radius

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
            Material pitchMat = TrimPitch();

            var holder = new GameObject("Scenery").transform;
            holder.SetParent(transform, false);

            var rng = new System.Random(seed);
            BuildSand(holder, pitchMat);
            float standOut = BuildStands(holder, Load("Stand"));
            BuildRail(holder, Load("Wall_Bamboo"));

            // Planting starts just outside the stands, so the belt of green hugs the arena instead of
            // leaving a bare ring of sand between the two.
            var keepOut = new Vector2(halfX + standOut + 2f, halfZ + standOut + 2f);
            Plant(holder, Load("Palm"),  palms, 6.5f, 9f,   0f,        keepOut, 1.6f, rng, true);
            Plant(holder, Load("Ferns"), ferns, 0.9f, 1.7f, fernSink,  keepOut, 1.3f, rng, false);
            // Kept low: at two metres the pale stone read as a balloon rather than a boulder.
            Plant(holder, Load("Rock"),  rocks, 0.4f, 1.0f, 0.12f,     keepOut, 1.5f, rng, false);
            HangVines(holder, Load("Vines"), rng);

            // One draw call per model instead of one per copy — the difference between this being
            // affordable on a phone and not. Skipped when the imported meshes are not readable,
            // which is the glTF importer default and would only produce errors.
            if (MeshesAreReadable(holder.gameObject)) StaticBatchingUtility.Combine(holder.gameObject);
        }

        static GameObject Load(string n)
        {
            var go = Resources.Load<GameObject>(Root + n);
            if (go == null) Debug.LogWarning("[Arena] missing Resources/" + Root + n);
            return go;
        }

        // The white boxes around the pitch are the SAME objects that carry the colliders, so only
        // their renderers go. Disabling the objects would delete the arena's physics.
        //
        // The pitch itself is the Ground box: its textured top face at y=0 is what has always been
        // on screen. ArenaVis, the glTF slab underneath it, ships 56 x 53 m — it ran nine metres past
        // the touchline and put the stands on grass — so that is the one that goes. Returns the
        // Ground material, which is a known-good URP material the sand can be built from.
        Material TrimPitch()
        {
            int n = 0;
            var walls = GameObject.Find("Walls");
            if (walls != null)
                foreach (var r in walls.GetComponentsInChildren<Renderer>(true)) { r.enabled = false; n++; }
            else Debug.LogWarning("[Arena] no 'Walls' object to hide");

            var vis = GameObject.Find("ArenaVis");
            if (vis != null)
            {
                var vr = vis.GetComponent<Renderer>();
                if (vr != null) { vr.enabled = false; n++; }
            }
            Debug.Log("[Arena] hidden " + n + " renderers (colliders untouched)");

            var ground = GameObject.Find("Ground");
            var gr = ground != null ? ground.GetComponent<Renderer>() : null;
            return gr != null ? gr.sharedMaterial : null;
        }

        // A wide sand plane just under the pitch, so the world does not end in grey void. Sits below
        // the playing surface and carries no collider: the floor stays the Ground box.
        //
        // Built from the Ground material rather than the primitive's own. CreatePrimitive hands back
        // the built-in Standard material, which has no shader under URP — that is what turned the
        // whole ground magenta on device.
        void BuildSand(Transform holder, Material source)
        {
            var sand = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(sand.GetComponent<Collider>());
            sand.name = "Sand";
            sand.transform.SetParent(holder, false);
            sand.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            sand.transform.position = new Vector3(0f, -0.05f, 0f);
            sand.transform.localScale = new Vector3(sandRadius * 2f, sandRadius * 2f, 1f);

            var mr = sand.GetComponent<MeshRenderer>();
            mr.sharedMaterial = SandMaterial(source);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Material SandMaterial(Material source)
        {
            Material m;
            if (source != null)
            {
                // A copy of the pitch material is guaranteed to have a shader that survived the
                // build, since the pitch is visibly drawing. Drop its grass texture and tint it.
                m = new Material(source);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            }
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) { Debug.LogWarning("[Arena] no URP shader for the sand"); return null; }
                m = new Material(sh);
            }
            m.color = new Color(0.93f, 0.87f, 0.71f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            return m;
        }

        // The rail runs corner to corner in a whole number of segments, each stretched to the exact
        // width needed to close the run. Stepping by a nominal width instead left the last segment
        // hanging 1.4 m past the corner and pushed the goal opening off centre.
        void BuildRail(Transform holder, GameObject rail)
        {
            if (rail == null) return;

            foreach (float side in new[] { -1f, 1f })
            {
                Run(holder, rail, -halfX, halfX, true, side * halfZ, 0f);          // touchline
                Run(holder, rail, -halfZ, -goalHalfZ, false, side * halfX, 90f);   // end wall, near post
                Run(holder, rail, goalHalfZ, halfZ, false, side * halfX, 90f);     // end wall, far post
            }
        }

        void Run(Transform holder, GameObject prefab, float from, float to, bool alongX,
                 float fixedCoord, float yaw)
        {
            float length = to - from;
            if (length <= 0.01f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(length / railSegment));
            float w = length / n;

            for (int i = 0; i < n; i++)
            {
                float c = from + w * (i + 0.5f);
                var pos = alongX ? new Vector3(c, 0f, fixedCoord) : new Vector3(fixedCoord, 0f, c);
                var go = Spawn(holder, prefab);
                Bounds b = WorldBounds(go);
                if (b.size.x < 1e-4f || b.size.y < 1e-4f) { Destroy(go); continue; }

                // Uniform on height and depth, stretched only along the run, so segments butt exactly
                // together whatever length they have to cover. A few per cent on bamboo poles is
                // invisible; a gap between segments is not.
                float u = wallHeight / b.size.y;
                go.transform.localScale = new Vector3(w / b.size.x, u, u);
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                Seat(go, pos, 0f);
            }
        }

        // Stands sit immediately behind the rail and tile without overlapping — they were 4.2 m wide
        // laid on a 3 m step, so every copy grew into its neighbour. Returns how far they reach out
        // from the touchline, which is what the planting keeps clear of.
        float BuildStands(Transform holder, GameObject stand)
        {
            if (stand == null) return standGap;

            var probe = Spawn(holder, stand);
            Bounds pb = WorldBounds(probe);
            Destroy(probe);
            if (pb.size.y < 1e-4f || pb.size.x < 1e-4f) return standGap;

            float u = standHeight / pb.size.y;
            float width = pb.size.x * u;
            float depth = pb.size.z * u;
            float out_ = standGap + depth;

            // End rows first, then the touchline rows long enough to close the corners over them.
            float endX = halfX + standGap + depth * 0.5f;
            float endHalfZ = halfZ + standGap;
            foreach (float side in new[] { -1f, 1f })
                StandRow(holder, stand, u, width, -endHalfZ, endHalfZ, false,
                         side * endX, side > 0f ? 270f : 90f);

            float touchZ = halfZ + standGap + depth * 0.5f;
            float touchHalfX = halfX + standGap + depth;
            foreach (float side in new[] { -1f, 1f })
                StandRow(holder, stand, u, width, -touchHalfX, touchHalfX, true,
                         side * touchZ, side > 0f ? 180f : 0f);

            Debug.Log("[Arena] stands " + width.ToString("0.00") + " x " + depth.ToString("0.00")
                      + " m, front " + standGap + " m behind the rail");
            return out_;
        }

        void StandRow(Transform holder, GameObject prefab, float u, float width,
                      float from, float to, bool alongX, float fixedCoord, float yaw)
        {
            float length = to - from;
            int n = Mathf.Max(1, Mathf.RoundToInt(length / width));
            float w = length / n;

            for (int i = 0; i < n; i++)
            {
                float c = from + w * (i + 0.5f);
                var pos = alongX ? new Vector3(c, 0f, fixedCoord) : new Vector3(fixedCoord, 0f, c);
                var go = Spawn(holder, prefab);
                Bounds b = WorldBounds(go);
                if (b.size.x < 1e-4f) { Destroy(go); continue; }

                go.transform.localScale = new Vector3(w / b.size.x, u, u);
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                Seat(go, pos, 0f);
            }
        }

        // Planted along a ray out of the centre, starting where that ray leaves the stands rather
        // than at a fixed radius. Rejecting points that landed on the arena instead meant the belt
        // thinned out wherever the arena was widest — and left twelve metres of empty sand.
        void Plant(Transform holder, GameObject prefab, int count, float minH, float maxH, float sink,
                   Vector2 keepOut, float stretch, System.Random rng, bool recordCanopy)
        {
            if (prefab == null) return;
            for (int i = 0; i < count; i++)
            {
                float ang = (i + (float)rng.NextDouble() * 0.7f) / count * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(ang) * stretch, Mathf.Sin(ang)).normalized;

                float exit = Mathf.Min(Mathf.Abs(dir.x) > 1e-3f ? keepOut.x / Mathf.Abs(dir.x) : 1e6f,
                                       Mathf.Abs(dir.y) > 1e-3f ? keepOut.y / Mathf.Abs(dir.y) : 1e6f);
                float t = exit + Mathf.Lerp(beltNear, beltFar, (float)rng.NextDouble());
                var pos = new Vector3(dir.x * t, 0f, dir.y * t);

                float h = Mathf.Lerp(minH, maxH, (float)rng.NextDouble());
                var go = Spawn(holder, prefab);
                Bounds b = WorldBounds(go);
                if (b.size.y < 1e-4f) { Destroy(go); continue; }

                go.transform.localScale = Vector3.one * (h / b.size.y);
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                Seat(go, pos, sink);

                if (recordCanopy)
                {
                    Bounds c = WorldBounds(go);
                    _canopies.Add(new Vector4(c.center.x, c.max.y, c.center.z,
                                              Mathf.Max(c.size.x, c.size.z) * 0.5f));
                }
            }
        }

        // Vines hang from the palms, not from thin air, and are seated by their HIGHEST point rather
        // than dropped onto the ground.
        void HangVines(Transform holder, GameObject prefab, System.Random rng)
        {
            if (prefab == null || _canopies.Count == 0) return;
            for (int i = 0; i < vines; i++)
            {
                Vector4 c = _canopies[(i * 7 + 3) % _canopies.Count];
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = c.w * Mathf.Lerp(0.35f, 0.8f, (float)rng.NextDouble());
                float top = c.y - Mathf.Lerp(0.4f, 1.2f, (float)rng.NextDouble());

                var go = Spawn(holder, prefab);
                Bounds b = WorldBounds(go);
                if (b.size.y < 1e-4f) { Destroy(go); continue; }

                go.transform.localScale = Vector3.one * (Mathf.Lerp(2.5f, 4f, (float)rng.NextDouble()) / b.size.y);
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

                b = WorldBounds(go);
                var target = new Vector3(c.x + Mathf.Cos(a) * r, 0f, c.z + Mathf.Sin(a) * r);
                go.transform.position += new Vector3(target.x - b.center.x,
                                                     top - b.max.y,
                                                     target.z - b.center.z);
            }
        }

        static GameObject Spawn(Transform holder, GameObject prefab)
        {
            var go = Instantiate(prefab, holder);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go;
        }

        // Centres the copy on pos in x/z and rests its lowest point on pos.y, whatever the pivot
        // happens to be. Measured after scale and rotation, so it is right for a rail rotated 90
        // degrees just as it is for a palm.
        static void Seat(GameObject go, Vector3 pos, float sink)
        {
            Bounds b = WorldBounds(go);
            go.transform.position += new Vector3(pos.x - b.center.x,
                                                 pos.y - b.min.y - b.size.y * sink,
                                                 pos.z - b.center.z);
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }

        static bool MeshesAreReadable(GameObject root)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && !mf.sharedMesh.isReadable) return false;
            return true;
        }
    }
}
