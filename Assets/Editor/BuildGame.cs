using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BuildGame
{
    private const string ScenePath = "Assets/Main.unity";

    public static void BuildWindows()
    {
        Prepare(false);
        Build(BuildTarget.StandaloneWindows64, ReadOutput("Builds/Windows/PixelAirBattle.exe"));
    }

    public static void BuildAndroid()
    {
        Prepare(true);
        PlayerSettings.bundleVersion = "1.0.1";
        PlayerSettings.Android.bundleVersionCode = 2;
        EditorUserBuildSettings.buildAppBundle = false;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        Build(BuildTarget.Android, ReadOutput("Builds/Android/PixelAirBattle.apk"));
    }

    private static void Prepare(bool android)
    {
        ArtImportSettings.RefreshArt();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        PlayerSettings.companyName = "Codex Studio";
        PlayerSettings.productName = "Pixel Air Battle";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.codex.pixelairbattle");
        PlayerSettings.bundleVersion = "1.0.0";
        PlayerSettings.Android.bundleVersionCode = 1;
        PlayerSettings.defaultScreenWidth = 540;
        PlayerSettings.defaultScreenHeight = 960;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;
        QualitySettings.vSyncCount = 1;
    }

    private static string ReadOutput(string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "-buildOutput") return Path.GetFullPath(args[i + 1]);
        return Path.GetFullPath(fallback);
    }

    private static void Build(BuildTarget target, string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = output,
            target = target,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Build failed: " + report.summary.result);
        Debug.Log($"BUILD_OK {output} ({report.summary.totalSize} bytes)");
    }
}
