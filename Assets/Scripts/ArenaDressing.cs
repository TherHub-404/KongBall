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
    // What is left still holds the old rules:
    //  - nothing here has a collider. The tuned collision boxes in the scene are the only physics.
    //  - the pitch markings derive from the SAME constants as those boxes, so the paint cannot drift
    //    away from where the ball actually bounces.
    public class ArenaDressing : MonoBehaviour
    {
        [Header("Pitch footprint — mirrors the collision boxes")]
        public float halfX = 23.3f;      // end walls
        public float halfZ = 17.4f;      // touchlines

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
            Material pitchMat = HideCollisionBoxes();

            var holder = new GameObject("Scenery").transform;
            holder.SetParent(transform, false);

            BuildSand(holder, pitchMat);
            BuildPitchSurface(holder, pitchMat);
        }

        // The white boxes around the pitch are the SAME objects that carry the colliders, so only
        // their renderers go. Disabling the objects would delete the arena's physics.
        //
        // ArenaVis is deliberately NOT touched any more: it is the arena now. It used to be hidden
        // here, which is how the project ended up with two arenas and only one of them visible.
        //
        // Returns the Ground material, which is a known-good URP material the sand can be built from.
        Material HideCollisionBoxes()
        {
            int n = 0;
            var walls = GameObject.Find("Walls");
            if (walls != null)
                foreach (var r in walls.GetComponentsInChildren<Renderer>(true)) { r.enabled = false; n++; }
            else Debug.LogWarning("[Arena] no 'Walls' object to hide");
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

        // The pitch ships as a tiled grass swatch with no markings, which is why it reads as a green
        // rectangle rather than a football pitch. Painted here instead, at the exact size of the
        // Ground box and from the SAME constants as the collision boxes — so the touchline you see is
        // the touchline the ball bounces off, not an approximation of it.
        void BuildPitchSurface(Transform holder, Material source)
        {
            var ground = GameObject.Find("Ground");
            var gr = ground != null ? ground.GetComponent<Renderer>() : null;
            if (gr == null) { Debug.LogWarning("[Arena] no 'Ground' to paint over"); return; }

            Bounds gb = gr.bounds;
            if (gb.size.x < 1e-3f || gb.size.z < 1e-3f) return;

            var tex = PaintPitch(gb.size.x, gb.size.z);
            if (tex == null) return;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = "PitchSurface";
            quad.transform.SetParent(holder, false);
            // Rotating +90 about X sends the quad's local +Y to world +Z, so U runs along world x and
            // V along world z. That is what lets the markings be laid out in metres below.
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.position = new Vector3(gb.center.x, gb.max.y + 0.01f, gb.center.z);
            quad.transform.localScale = new Vector3(gb.size.x, gb.size.z, 1f);

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
                      + gb.size.x.ToString("0.0") + " x " + gb.size.z.ToString("0.0") + " m");
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
        float Markings(float x, float z, float px)
        {
            float ax = Mathf.Abs(x), az = Mathf.Abs(z);
            float len = halfX * 2f, wid = halfZ * 2f;

            float boxDepth = 16.5f / 105f * len, boxHalf = 40.32f / 68f * wid * 0.5f;
            float areaDepth = 5.5f / 105f * len, areaHalf = 18.32f / 68f * wid * 0.5f;
            float spot = 11f / 105f * len;
            float circle = 9.15f / 68f * wid;

            float ink = 0f;
            // Touchlines and goal lines, drawn just inside the walls.
            if (az <= halfZ + px) ink = Mathf.Max(ink, Edge(ax, halfX, px));
            if (ax <= halfX + px) ink = Mathf.Max(ink, Edge(az, halfZ, px));
            // Halfway line and centre circle.
            if (az <= halfZ) ink = Mathf.Max(ink, Edge(ax, 0f, px));
            ink = Mathf.Max(ink, Edge(Mathf.Sqrt(x * x + z * z), circle, px));
            ink = Mathf.Max(ink, Fill(Mathf.Sqrt(x * x + z * z), 0.16f, px));

            // Penalty area, goal area and penalty spot, mirrored at both ends.
            ink = Mathf.Max(ink, Box(ax, az, halfX - boxDepth, boxHalf, px));
            ink = Mathf.Max(ink, Box(ax, az, halfX - areaDepth, areaHalf, px));
            ink = Mathf.Max(ink, Fill(Mathf.Sqrt((ax - (halfX - spot)) * (ax - (halfX - spot)) + z * z), 0.16f, px));

            // Corner arcs.
            float cd = Mathf.Sqrt((ax - halfX) * (ax - halfX) + (az - halfZ) * (az - halfZ));
            if (ax <= halfX && az <= halfZ) ink = Mathf.Max(ink, Edge(cd, 1f / 105f * len, px));

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
            if (ax >= front - lineWidth && ax <= halfX) ink = Mathf.Max(ink, Edge(az, half, px));
            return ink;
        }
    }
}
