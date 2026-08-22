using UnityEngine;

namespace KongBall
{
    // Paints the pitch and lays the sand under it. Everything the eye reads as "the arena" — stands,
    // rail, palms, rocks, vines — is the ArenaVis model in the scene, which carries all of it baked
    // into one mesh.
    //
    // This class used to build that scenery too, tiling a bamboo rail and a stand over 280 m of
    // perimeter and planting 86 props. It did that because ArenaVis had been written off as "does
    // not fit the pitch" — and it did not, because the scene scaled it (28, 3, 28): non-uniform,
    // squashing a 15 m arena into a 1.15 m pancake. At a uniform scale it fits, so ~180 instanced
    // copies of two repeated models became one mesh and one draw call.
    //
    // It also builds the wall the ball bounces off. That used to be 52 boxes placed by hand in the
    // scene, next to a pitch painted by this file from its own copy of the bounds — two descriptions
    // of one pitch, free to disagree, and they did. Both now come from Arena, so they cannot.
    //
    // The old rule still holds: the decoration itself never carries a collider. The only physics
    // here is the wall, and it is built from the touchline, not drawn near it.
    public class ArenaDressing : MonoBehaviour
    {
        // La forma del campo NON sta qui: sta in Arena, perche' la devono usare identica anche i
        // muri e il controllo di palla fuori. Qui restano solo i parametri di come si dipinge.
        [Header("Sand")]
        public float sandRadius = 140f;

        [Header("Pitch surface")]
        public int pitchTexWidth = 1024;
        public int mownStripes = 10;
        public float lineWidth = 0.14f;

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
            PlaceArena();

            var holder = new GameObject("Scenery").transform;
            holder.SetParent(transform, false);

            Material pitchMat = GroundMaterial();
            BuildWalls();
            BuildSand(holder, pitchMat);
            BuildPitchSurface(holder, pitchMat);
        }

        // The model is placed from the constants rather than trusted to whatever the scene says.
        // The scene carries the same numbers so the Editor view is right, but if the two ever
        // disagree the code wins — a pitch painted to one shape inside an arena standing somewhere
        // else is the failure this whole file exists to prevent.
        static void PlaceArena()
        {
            var vis = GameObject.Find("ArenaVis");
            if (vis == null) { Debug.LogWarning("[Arena] no 'ArenaVis' in the scene"); return; }
            vis.transform.localPosition = new Vector3(Arena.ModelX, Arena.ModelY, Arena.ModelZ);
            vis.transform.localScale = Vector3.one * Arena.ModelScale;
        }

        // The wall the ball bounces off, built along the touchline so it cannot disagree with the
        // paint. It used to be 52 boxes placed by hand in the scene, which is exactly how the paint
        // and the physics came to describe two different pitches.
        //
        // One tall run rather than a low wall plus an invisible high one: nothing here is drawn, so
        // a single 10 m box per segment does both jobs. Segments overlap slightly, because a
        // hairline gap between two colliders is a hole the ball will eventually find.
        static void BuildWalls()
        {
            var root = new GameObject("ArenaWalls").transform;
            var pts = Arena.Outline();
            int n = 0;
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 a = pts[i], b = pts[(i + 1) % pts.Length];

                // The two straight runs behind the goals are the goal mouths: the wall has to open
                // there or the ball bounces off thin air a metre short of the net. Everything else
                // is built whole.
                bool fondo = Mathf.Abs(a.x) > Arena.HalfX - 0.01f && Mathf.Abs(b.x) > Arena.HalfX - 0.01f;
                if (fondo)
                {
                    float lato = Mathf.Sign(a.x);
                    float bordo = Arena.HalfZ - Arena.CornerRadius;        // where the arc takes over
                    if (bordo > Arena.GoalMouthHalfZ)
                    {
                        n += Segment(root, n, new Vector3(lato * Arena.HalfX, 0f, Arena.GoalMouthHalfZ),
                                              new Vector3(lato * Arena.HalfX, 0f, bordo), 0f, Arena.WallHeight);
                        n += Segment(root, n, new Vector3(lato * Arena.HalfX, 0f, -bordo),
                                              new Vector3(lato * Arena.HalfX, 0f, -Arena.GoalMouthHalfZ), 0f, Arena.WallHeight);
                    }
                    // Above the crossbar the mouth closes again. A shot that goes over is a miss,
                    // and a miss should come back into play — not sail into the stands and get
                    // teleported to the centre spot by the out-of-bounds net.
                    n += Segment(root, n, new Vector3(lato * Arena.HalfX, 0f, -Arena.GoalMouthHalfZ),
                                          new Vector3(lato * Arena.HalfX, 0f, Arena.GoalMouthHalfZ),
                                          Arena.GoalHeight, Arena.WallHeight);
                    continue;
                }
                n += Segment(root, n, a, b, 0f, Arena.WallHeight);
            }
            Debug.Log("[Arena] " + n + " wall segments built from the touchline, goal mouths open");
        }

        static int Segment(Transform root, int n, Vector3 a, Vector3 b, float da, float a2)
        {
            Vector3 along = b - a;
            float len = along.magnitude;
            float alt = a2 - da;
            if (len < 1e-3f || alt < 1e-3f) return 0;

            var go = new GameObject("Wall_" + n);
            go.transform.SetParent(root, false);
            go.transform.position = (a + b) * 0.5f + Vector3.up * (da + alt * 0.5f);
            go.transform.rotation = Quaternion.LookRotation(along / len, Vector3.up);

            var box = go.AddComponent<BoxCollider>();
            // Longer than the gap it fills: two colliders meeting exactly edge to edge leave a seam
            // a fast ball can pass through between two physics steps.
            box.size = new Vector3(Arena.WallThickness, alt, len + 0.3f);
            return 1;
        }

        // A known-good URP material to build the sand and the pitch from: the Ground box is visibly
        // drawing, so whatever shader it has survived the build. CreatePrimitive hands back the
        // built-in Standard material instead, which has no shader under URP — that is what turned
        // the whole ground magenta on device.
        static Material GroundMaterial()
        {
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

        // The pitch ships as a tiled grass swatch with no markings, which is why it reads as a green
        // rectangle rather than a football pitch. Painted here instead, at the exact size of the
        // Ground box and from the SAME constants as the collision boxes — so the touchline you see is
        // the touchline the ball bounces off, not an approximation of it.
        void BuildPitchSurface(Transform holder, Material source)
        {
            // Sized to the WHOLE Ground box, not to the pitch. The pitch is a rounded rectangle
            // and the floor is square, so if the quad were only as big as the pitch, the strip of
            // Ground sticking out around it would show as a green border with a rounded green
            // island inside. Painting the whole floor — grass inside the touchline, sand outside —
            // makes the shape read as the pitch and the rest as beach, with no seam.
            var ground = GameObject.Find("Ground");
            var gr = ground != null ? ground.GetComponent<Renderer>() : null;
            if (gr == null) { Debug.LogWarning("[Arena] no 'Ground' to paint over"); return; }
            Bounds gb = gr.bounds;
            if (gb.size.x < 1e-3f || gb.size.z < 1e-3f) return;

            float larghezza = gb.size.x, profondita = gb.size.z;
            var tex = PaintPitch(larghezza, profondita);
            if (tex == null) return;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = "PitchSurface";
            quad.transform.SetParent(holder, false);
            // Rotating +90 about X sends the quad's local +Y to world +Z, so U runs along world x and
            // V along world z. That is what lets the markings be laid out in metres below.
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.position = new Vector3(gb.center.x, gb.max.y + 0.01f, gb.center.z);
            quad.transform.localScale = new Vector3(larghezza, profondita, 1f);

            Material m = source != null ? new Material(source) : SandMaterial(null);
            if (m == null) { Destroy(quad); return; }
            if (m.HasProperty("_BaseMap"))
            {
                m.SetTexture("_BaseMap", tex);
                m.SetTextureScale("_BaseMap", Vector2.one);
                m.SetTextureOffset("_BaseMap", Vector2.zero);
            }
            if (m.HasProperty("_MainTex"))
            {
                m.SetTexture("_MainTex", tex);
                m.SetTextureScale("_MainTex", Vector2.one);
                m.SetTextureOffset("_MainTex", Vector2.zero);
            }
            m.color = Color.white;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);

            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Debug.Log("[Arena] pitch painted " + tex.width + "x" + tex.height + " over "
                      + larghezza.ToString("0.0") + " x " + profondita.ToString("0.0") + " m");
        }

        Texture2D PaintPitch(float worldW, float worldD)
        {
            int w = Mathf.Clamp(pitchTexWidth, 256, 2048);
            int h = Mathf.Clamp(Mathf.RoundToInt(w * worldD / worldW), 128, 2048);

            var dark = new Color(0.13f, 0.36f, 0.13f);
            var light = new Color(0.20f, 0.49f, 0.18f);
            var chalk = new Color(0.92f, 0.95f, 0.92f);

            float mx = worldW / w, mz = worldD / h;
            float px = Mathf.Max(mx, mz);          // one texel, in metres — the edge softening width
            float stripe = worldW / Mathf.Max(1, mownStripes);

            var pixels = new Color32[w * h];
            for (int j = 0; j < h; j++)
            {
                float z = (j + 0.5f) * mz - worldD * 0.5f;
                for (int i = 0; i < w; i++)
                {
                    float x = (i + 0.5f) * mx - worldW * 0.5f;

                    // Outside the touchline the quad is sand, in the same colour as the plane
                    // underneath it. The pitch is a rounded rectangle and the quad is square, so
                    // without this the corners would be four green wedges lying on the beach.
                    float fuori = Arena.Distance(x, z);
                    if (fuori > lineWidth)
                    {
                        var sabbia = new Color(0.93f, 0.87f, 0.71f);
                        float grana = Mathf.PerlinNoise(x * 1.3f + 5f, z * 1.3f + 2f) * 0.06f;
                        sabbia *= 0.97f + grana;
                        sabbia.a = 1f;
                        pixels[j * w + i] = sabbia;
                        continue;
                    }

                    // Mown stripes, the thing that actually makes turf read as a pitch.
                    float band = Mathf.Repeat((x + worldW * 0.5f) / stripe, 1f) < 0.5f ? 0f : 1f;
                    Color c = Color.Lerp(dark, light, band);

                    // Two octaves of blotching so it is grass and not a flat fill.
                    float n = Mathf.PerlinNoise(x * 0.4f + 11f, z * 0.4f + 7f) * 0.16f
                            + Mathf.PerlinNoise(x * 2.7f + 3f, z * 2.7f + 19f) * 0.09f;
                    c *= 0.88f + n;

                    float ink = Markings(x, z, px);
                    if (ink > 0.001f) c = Color.Lerp(c, chalk, ink);

                    c.a = 1f;
                    pixels[j * w + i] = c;
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            tex.anisoLevel = 4;
            return tex;
        }

        // Every measurement is the real proportion of a football pitch, rescaled from 105 x 68 m onto
        // the arena's own 2*halfX by 2*halfZ playing area.
        // The touchline is no longer four straight lines: it is the outline of the shape in Arena,
        // corners included, so the paint and the wall are the same curve by construction. Everything
        // else — halfway line, centre circle, the two boxes — is laid out in metres off the same
        // half-extents, which is what keeps it looking like a pitch and not like a decal.
        //
        // The corner arcs of a real pitch are gone: on a shape whose corners are already a 16 m
        // radius they would be an arc drawn on an arc.
        float Markings(float x, float z, float px)
        {
            float ax = Mathf.Abs(x), az = Mathf.Abs(z);
            float len = Arena.HalfX * 2f, wid = Arena.HalfZ * 2f;

            float boxDepth = 16.5f / 105f * len, boxHalf = 40.32f / 68f * wid * 0.5f;
            float areaDepth = 5.5f / 105f * len, areaHalf = 18.32f / 68f * wid * 0.5f;
            float spot = 11f / 105f * len;
            float circle = 9.15f / 68f * wid;

            float ink = 0f;
            // The touchline: where the distance to the boundary is zero.
            ink = Mathf.Max(ink, Edge(Arena.Distance(x, z), 0f, px));
            // Halfway line and centre circle.
            if (az <= Arena.HalfZ) ink = Mathf.Max(ink, Edge(ax, 0f, px));
            ink = Mathf.Max(ink, Edge(Mathf.Sqrt(x * x + z * z), circle, px));
            ink = Mathf.Max(ink, Fill(Mathf.Sqrt(x * x + z * z), 0.16f, px));

            // Penalty area, goal area and penalty spot, mirrored at both ends.
            ink = Mathf.Max(ink, Box(ax, az, Arena.HalfX - boxDepth, boxHalf, px));
            ink = Mathf.Max(ink, Box(ax, az, Arena.HalfX - areaDepth, areaHalf, px));
            ink = Mathf.Max(ink, Fill(Mathf.Sqrt((ax - (Arena.HalfX - spot)) * (ax - (Arena.HalfX - spot)) + z * z), 0.16f, px));

            return Mathf.Clamp01(ink);
        }

        // A stroke centred on `at`, and a filled disc, both softened over one texel so the lines do
        // not crawl when the camera moves.
        float Edge(float d, float at, float px)
        {
            return Mathf.Clamp01((lineWidth * 0.5f - Mathf.Abs(d - at)) / Mathf.Max(px, 1e-4f) + 0.5f);
        }

        static float Fill(float d, float r, float px)
        {
            return Mathf.Clamp01((r - d) / Mathf.Max(px, 1e-4f) + 0.5f);
        }

        // Three sides of a rectangle: the fourth is the goal line, already drawn.
        float Box(float ax, float az, float front, float half, float px)
        {
            float ink = 0f;
            if (az <= half + lineWidth) ink = Mathf.Max(ink, Edge(ax, front, px));
            if (ax >= front - lineWidth && ax <= Arena.HalfX) ink = Mathf.Max(ink, Edge(az, half, px));
            return ink;
        }
    }
}
