using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace KongBall
{
    // Runs automatically after every iOS build. Declares that the app uses no non-exempt
    // encryption (ITSAppUsesNonExemptEncryption = false) so App Store Connect SKIPS the manual
    // "Export Compliance / encryption" question and the build goes straight to TestFlight.
    public static class IOSPostProcess
    {
        [PostProcessBuild(999)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;
            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            plist.WriteToFile(plistPath);
            Debug.Log("CMDBUILD ITSAppUsesNonExemptEncryption=false written to Info.plist");
        }
    }

    // Command-line build entry point so the iOS Xcode project can be regenerated headlessly
    // (batchmode) without the Editor GUI being open. Invoke with:
    //   Unity -quit -batchmode -projectPath <proj> -executeMethod KongBall.CmdBuild.IOS -logFile -
    public static class CmdBuild
    {
        // --- iOS / TestFlight config (team 9Z8F9Q282X = LORENZO MILANO, has the Distribution cert) ---
        const string BundleId = "com.kong.kongball";
        const string TeamId   = "9Z8F9Q282X";

        const string IconPath = "Assets/AppIcon/KongBallIcon1024.png";
        const string LogoPath = "Assets/Resources/Menu/Logo.png";

        // The same yellow as MenuStage.Background, so the launch screen and the menu behind it are one
        // continuous colour instead of two different yellows meeting at a cut.
        static readonly Color SplashYellow = new Color(0.97f, 0.78f, 0.24f);

        // Sets bundle id + signing team + app icon, then builds. Run headless via -executeMethod.
        public static void IOSConfigured()
        {
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, BundleId);
            PlayerSettings.iOS.appleDeveloperTeamID = TeamId;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true; // manage in Xcode/xcodebuild at archive time
            // App icon is provided by the Apple Icon Composer .icon bundle (Assets/AppIcon/KongBall.icon)
            // via IOSIconComposerIcon post-processor. The legacy PNG SetupIcon() is intentionally skipped.
            SetupSplashScreen();
            WireArtModels();
            EnsureGltfShadersIncluded();
            AssetDatabase.SaveAssets();
            Debug.LogFormat("CMDBUILD config bundle={0} team={1}", BundleId, TeamId);
            IOS();
        }

        // Launch screen: the KongBall wordmark on the menu's yellow, in place of the default dark grey
        // "Made with Unity".
        //
        // Whether Unity's own logo can be dropped depends on the licence, and this build runs on
        // Personal — historically that forced it on, and Unity 6 is the release that made it optional.
        // Rather than depend on which is true here, the request is made and the result is logged: if it
        // is honoured the launch screen is ours alone, and if it is not, DrawMode.UnityLogoBelow keeps
        // our wordmark as the subject with Unity's mark underneath. Either way the background and the
        // logo are ours, which is the part that was asked for.
        static void SetupSplashScreen()
        {
            var logo = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
            if (logo == null)
            {
                Debug.LogWarning("CMDBUILD splash: no sprite at " + LogoPath + " — leaving the splash alone");
                return;
            }

            PlayerSettings.SplashScreen.show = true;              // we want a splash: ours
            PlayerSettings.SplashScreen.showUnityLogo = false;    // granted or refused by the licence
            PlayerSettings.SplashScreen.backgroundColor = SplashYellow;
            PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.UnityLogoBelow;
            PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Static;
            PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(logo, 2f) };

            Debug.LogFormat("CMDBUILD splash: logo set, unity logo requested off -> actually {0}",
                            PlayerSettings.SplashScreen.showUnityLogo ? "STILL ON (licence)" : "off");
        }

        // Art models generated with Meshy are imported by glTFast as ScriptedImporter sub-assets,
        // whose internal file IDs only exist once Unity has imported them. That makes it impossible
        // to point a prefab at a freshly added model by editing YAML alone, so the wiring is done
        // here instead: at build time the Editor is running and can resolve the sub-assets by type.
        //
        // Each entry says "this prefab child should display this model", and the mesh is rescaled to
        // the gameplay size, because a generated model arrives at whatever scale the generator chose.
        struct ArtBinding
        {
            public string ModelPath;     // .glb to take the mesh and material from
            public string PrefabPath;    // prefab to update
            public string ChildName;     // child holding the MeshFilter / MeshRenderer
            public float TargetSize;     // desired world-space diameter of the largest axis
        }

        static readonly ArtBinding[] Bindings =
        {
            // NetBall.radius is 0.5, so the ball must render one unit across.
            new ArtBinding { ModelPath = "Assets/Models/Ball_Patchwork.glb",
                             PrefabPath = "Assets/Prefabs/NetBall.prefab",
                             ChildName = "Visual", TargetSize = 1.0f },
        };

        static void WireArtModels()
        {
            foreach (var b in Bindings)
            {
                if (!File.Exists(b.ModelPath)) { Debug.Log("CMDBUILD art: skipping missing " + b.ModelPath); continue; }

                Mesh mesh = null; Material mat = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(b.ModelPath))
                {
                    if (mesh == null && o is Mesh m) mesh = m;
                    if (mat == null && o is Material mm) mat = mm;
                }
                if (mesh == null) { Debug.LogWarning("CMDBUILD art: no mesh inside " + b.ModelPath); continue; }

                var contents = PrefabUtility.LoadPrefabContents(b.PrefabPath);
                try
                {
                    var child = contents.transform.Find(b.ChildName);
                    if (child == null) { Debug.LogWarning("CMDBUILD art: no child '" + b.ChildName + "' in " + b.PrefabPath); continue; }

                    var mf = child.GetComponent<MeshFilter>();
                    var mr = child.GetComponent<MeshRenderer>();
                    if (mf == null || mr == null) { Debug.LogWarning("CMDBUILD art: " + b.ChildName + " has no MeshFilter/MeshRenderer"); continue; }

                    mf.sharedMesh = mesh;
                    if (mat != null) mr.sharedMaterial = mat;

                    // Normalise: generated models come at arbitrary scale and are not always centred
                    // on their pivot, which would show up as a ball floating beside its own collider.
                    Vector3 size = mesh.bounds.size;
                    float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                    if (largest > 1e-4f)
                    {
                        float s = b.TargetSize / largest;
                        child.localScale = Vector3.one * s;
                        child.localPosition = -mesh.bounds.center * s;
                    }

                    PrefabUtility.SaveAsPrefabAsset(contents, b.PrefabPath);
                    Debug.LogFormat("CMDBUILD art: {0} -> {1}/{2} (mesh '{3}', {4} tris, scale {5:0.000})",
                                    b.ModelPath, b.PrefabPath, b.ChildName, mesh.name,
                                    mesh.triangles.Length / 3, child.localScale.x);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
        }

        // The arena, goals, monkey and ball all take their materials from .glb files imported by
        // glTFast's ScriptedImporter. Those materials are generated sub-assets, and the shaders they
        // use live inside the package — a dependency the build's shader stripping does not reliably
        // follow, so on device the meshes end up with no shader and simply are not drawn.
        //
        // Collect the shaders from the actual imported materials (no shader-name guessing, so this
        // keeps working if glTFast renames them) and pin them into Always Included Shaders, together
        // with the URP shaders that only runtime-created materials use.
        static void EnsureGltfShadersIncluded()
        {
            var wanted = new List<Shader>();
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(".gltf", System.StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    var mat = obj as Material;
                    if (mat == null || mat.shader == null) continue;
                    Debug.LogFormat("CMDBUILD glTF material '{0}' ({1}) uses shader '{2}'",
                                    mat.name, path, mat.shader.name);
                    if (!wanted.Contains(mat.shader)) wanted.Add(mat.shader);
                }
            }

            // ArenaDressing builds the sand plane at runtime, so its material has no asset in the
            // project to keep its shader alive. Without this the sand ships shaderless and the whole
            // ground renders magenta.
            foreach (var name in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit" })
            {
                var sh = Shader.Find(name);
                if (sh == null) { Debug.LogWarning("CMDBUILD shader not found: " + name); continue; }
                if (!wanted.Contains(sh)) wanted.Add(sh);
            }

            if (wanted.Count == 0) { Debug.Log("CMDBUILD no glTF materials found"); return; }

            var graphicsSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettings == null || graphicsSettings.Length == 0)
            {
                Debug.LogWarning("CMDBUILD could not open GraphicsSettings");
                return;
            }

            var so = new SerializedObject(graphicsSettings[0]);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            var already = new HashSet<Object>();
            for (int i = 0; i < list.arraySize; i++)
                already.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);

            int added = 0;
            foreach (var sh in wanted)
            {
                if (already.Contains(sh)) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = sh;
                added++;
                Debug.LogFormat("CMDBUILD pinned shader '{0}' into Always Included Shaders", sh.name);
            }
            if (added > 0) so.ApplyModifiedProperties();
            Debug.LogFormat("CMDBUILD glTF shaders: {0} distinct, {1} newly pinned", wanted.Count, added);
        }

        // Assign the 1024 source to every iOS icon slot (Unity downsamples). This is what puts the
        // App Store "marketing" 1024 icon into the generated asset catalog (else App Store Connect
        // rejects the upload with error 91111 "Missing app icon").
        static void SetupIcon()
        {
            AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (tex == null) { Debug.LogWarning("CMDBUILD icon not found at " + IconPath); return; }
            var kinds = PlayerSettings.GetSupportedIconKindsForPlatform(BuildTargetGroup.iOS);
            foreach (var kind in kinds)
            {
                var icons = PlayerSettings.GetPlatformIcons(BuildTargetGroup.iOS, kind);
                for (int i = 0; i < icons.Length; i++) icons[i].SetTexture(tex);
                PlayerSettings.SetPlatformIcons(BuildTargetGroup.iOS, kind, icons);
            }
            Debug.LogFormat("CMDBUILD icon set for {0} iOS icon kinds", kinds.Length);
        }

        public static void IOS()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/iOS",
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.LogFormat("CMDBUILD result={0} errors={1} time={2}s", s.result, s.totalErrors, (int)s.totalTime.TotalSeconds);
            EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
