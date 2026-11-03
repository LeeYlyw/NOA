#if UNITY_EDITOR
using System.IO;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

// Ship the native server beside the Windows player so players only launch NOA.
public class LobbyServerBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 0;
    static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    static string Source => Path.Combine(ProjectRoot, "Server", "x64", "Release", "GameServer.exe");
    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != UnityEditor.BuildTarget.StandaloneWindows64) return;
        string script = Path.Combine(ProjectRoot, "Server", "BuildServer.ps1");
        if (!File.Exists(script)) throw new BuildFailedException("Server/BuildServer.ps1 was not found.");
        var info = new ProcessStartInfo("powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"")
        {
            WorkingDirectory = ProjectRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (Process process = Process.Start(info))
        {
            var output = new StringBuilder();
            var errors = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) errors.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(120000))
            {
                process.Kill();
                throw new BuildFailedException("Release server build timed out.");
            }
            process.WaitForExit();
            if (process.ExitCode != 0 || !File.Exists(Source))
                throw new BuildFailedException("Release server build failed.\n" + output + "\n" + errors);
            UnityEngine.Debug.Log("[Lobby] Built Release server for this Windows player.\n" + output);
        }
    }
    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != UnityEditor.BuildTarget.StandaloneWindows64) return;
        string directory = Path.Combine(Path.GetDirectoryName(report.summary.outputPath), "Server");
        Directory.CreateDirectory(directory);
        File.Copy(Source, Path.Combine(directory, "GameServer.exe"), true);
        UnityEngine.Debug.Log("[Lobby] Bundled Release GameServer.exe with Windows player.");
    }
}
#endif
