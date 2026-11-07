using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class DedicatedClientBuild
{
    [MenuItem("NOA/Build Windows Client")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before building.");
        string directory = Path.GetFullPath("Builds/WindowsClient");
        Directory.CreateDirectory(directory);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/1.Scenes/MapGeneration_Test.unity" },
            locationPathName = Path.Combine(directory, "NOA.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("NOA client build failed: " + report.summary.result);
        Debug.Log("[NOA] Windows client ready: " + directory);
    }
}
