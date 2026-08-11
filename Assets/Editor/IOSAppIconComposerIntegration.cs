#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;
using System.IO;

/// <summary>
/// iOS post process: integrates an Apple Icon Composer .icon bundle (Xcode 26+) as the app icon.
/// - Finds a .icon bundle under Assets/AppIcon
/// - Copies it into the root of the generated Xcode project (skipping Unity .meta files)
/// - Adds it to the Xcode project + main target
/// - Sets ASSETCATALOG_COMPILER_APPICON_NAME + CFBundleIconName, and removes Unity's CFBundleIcons.
/// Adapted from KevinOttenVR/UnityIconComposer (MIT), with .meta-skipping copy.
/// </summary>
public static class IOSIconComposerIcon
{
    private const string SourceIconFolderRelativePath = "Assets/AppIcon";

    [PostProcessBuild(100)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS)
            return;

        string iconFolderFullPath = Path.GetFullPath(SourceIconFolderRelativePath);
        if (!Directory.Exists(iconFolderFullPath))
        {
            Debug.LogWarning($"[IconComposer] Source folder not found: {iconFolderFullPath}");
            return;
        }

        string iconFullPath, iconFileName, iconNameWithoutExt;
        if (!TryFindIconBundle(iconFolderFullPath, out iconFullPath, out iconFileName, out iconNameWithoutExt))
        {
            Debug.LogWarning($"[IconComposer] No .icon bundle found in: {iconFolderFullPath}");
            return;
        }

        Debug.Log($"[IconComposer] Using .icon bundle: {iconFullPath}");

        string destPath = Path.Combine(pathToBuiltProject, iconFileName);
        if (Directory.Exists(destPath)) Directory.Delete(destPath, true);
        CopyDirectory(iconFullPath, destPath);
        Debug.Log($"[IconComposer] Copied bundle to: {destPath}");

        string projPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        var proj = new PBXProject();
        proj.ReadFromFile(projPath);

        string mainTarget      = proj.GetUnityMainTargetGuid();
        string frameworkTarget = proj.GetUnityFrameworkTargetGuid();

        string relPathInProj = iconFileName;
        string fileGuid = proj.FindFileGuidByProjectPath(relPathInProj);
        if (string.IsNullOrEmpty(fileGuid))
        {
            fileGuid = proj.AddFile(relPathInProj, relPathInProj, PBXSourceTree.Source);
            Debug.Log($"[IconComposer] Added {iconFileName} to the Xcode project.");
        }
        proj.AddFileToBuild(mainTarget, fileGuid);

        proj.SetBuildProperty(mainTarget,      "ASSETCATALOG_COMPILER_APPICON_NAME", iconNameWithoutExt);
        proj.SetBuildProperty(frameworkTarget, "ASSETCATALOG_COMPILER_APPICON_NAME", iconNameWithoutExt);
        proj.SetBuildProperty(mainTarget, "INFOPLIST_KEY_CFBundleIconName", iconNameWithoutExt);
        proj.WriteToFile(projPath);

        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        if (File.Exists(plistPath))
        {
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("CFBundleIconName", iconNameWithoutExt);
            if (plist.root.values.ContainsKey("CFBundleIcons"))
                plist.root.values.Remove("CFBundleIcons");
            File.WriteAllText(plistPath, plist.WriteToString());
            Debug.Log($"[IconComposer] Info.plist: CFBundleIconName => {iconNameWithoutExt}");
        }
    }

    private static bool TryFindIconBundle(string folder, out string fullPath, out string fileName, out string nameWithoutExt)
    {
        fullPath = fileName = nameWithoutExt = null;
        string[] dirCandidates = Directory.GetDirectories(folder, "*.icon", SearchOption.TopDirectoryOnly);
        string[] fileCandidates = Directory.GetFiles(folder, "*.icon", SearchOption.TopDirectoryOnly);
        string chosen = null;
        if (dirCandidates.Length > 0) { System.Array.Sort(dirCandidates); chosen = dirCandidates[0]; }
        else if (fileCandidates.Length > 0) { System.Array.Sort(fileCandidates); chosen = fileCandidates[0]; }
        if (string.IsNullOrEmpty(chosen)) return false;
        fullPath = chosen;
        fileName = Path.GetFileName(chosen);
        nameWithoutExt = Path.GetFileNameWithoutExtension(chosen);
        return true;
    }

    // Copies a directory tree, SKIPPING Unity .meta files (they must not end up inside the .icon bundle).
    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            if (file.EndsWith(".meta")) continue;
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }
}
#endif
