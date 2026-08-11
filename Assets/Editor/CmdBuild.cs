using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CalcioStumble
{
    // Command-line build entry point so the iOS Xcode project can be regenerated headlessly
    // (batchmode) without the Editor GUI being open. Invoke with:
    //   Unity -quit -batchmode -projectPath <proj> -executeMethod CalcioStumble.CmdBuild.IOS -logFile -
    public static class CmdBuild
    {
        // --- iOS / TestFlight config (team 9Z8F9Q282X = LORENZO MILANO, has the Distribution cert) ---
        const string BundleId = "com.kong.kongball";
        const string TeamId   = "9Z8F9Q282X";

        const string IconPath = "Assets/AppIcon/KongBallIcon1024.png";

        // Sets bundle id + signing team + app icon, then builds. Run headless via -executeMethod.
        public static void IOSConfigured()
        {
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, BundleId);
            PlayerSettings.iOS.appleDeveloperTeamID = TeamId;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true; // manage in Xcode/xcodebuild at archive time
            SetupIcon();
            AssetDatabase.SaveAssets();
            Debug.LogFormat("CMDBUILD config bundle={0} team={1}", BundleId, TeamId);
            IOS();
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
