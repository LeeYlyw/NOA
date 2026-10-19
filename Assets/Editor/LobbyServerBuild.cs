#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

// Ship the native server beside the Windows player so players only launch NOA.
public class LobbyServerBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 0;
    const string Source = "Server/GameServer/x64/Debug/GameServer.exe";
    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == UnityEditor.BuildTarget.StandaloneWindows64 && !File.Exists(Source))
            throw new BuildFailedException("Build Server/GameServer/GameServer.vcxproj (x64 Debug) before building NOA.");
    }
    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != UnityEditor.BuildTarget.StandaloneWindows64) return;
        string directory = Path.Combine(Path.GetDirectoryName(report.summary.outputPath), "Server");
        Directory.CreateDirectory(directory);
        File.Copy(Source, Path.Combine(directory, "GameServer.exe"), true);
        Debug.Log("[Lobby] Bundled GameServer.exe with Windows player.");
    }
}
#endif
