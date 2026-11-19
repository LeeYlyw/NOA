using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class OfflineDeathSmoke
{
    const string Key = "NOA.OfflineDeathSmoke";
    static int frames;
    static OfflineDeathSmoke() { EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static PlayerController Player(string name)
    {
        var root = new GameObject(name, typeof(CharacterController));
        var player = root.AddComponent<PlayerController>();
        player.isLocalPlayer = true;
        player.SetHp(100);
        return player;
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || ++frames < 20) return;
        SessionState.SetBool(Key, false);
        try
        {
            var network = new GameObject("Network").AddComponent<NetworkClient>();
            network.offlineMode = false;
            var online = Player("Online player");
            online.TakeDamage(100);
            Check(!ClueManager.instance.IsEnding, "Online death must wait for server result");
            network.offlineMode = true;
            var player = Player("Offline player");
            // Exercise the death event without relying on the manager's Update or assigned transform.
            ClueManager.instance.enabled = false;
            network.localPlayerTransform = null;
            for (int hit = 0; hit < 9; hit++) player.TakeDamage(10);
            Check(!ClueManager.instance.IsEnding, "Nonfatal damage ended match");
            player.TakeDamage(10);
            Check(player.IsDeadState && player.CurrentHp == 0, "Player did not die at zero HP");
            Check(ClueManager.instance.IsEnding && !ClueManager.instance.IsVictory, "Defeat missing");
            Check(Time.timeScale == 0 && Cursor.lockState == CursorLockMode.None, "Game not paused/unlocked");
            Check(UnityEngine.Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.text == "임무 실패"), "Defeat title missing");
            Check(UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Any(c => c.name == "Ending Report" && c.enabled), "Report canvas missing");
            player.TakeDamage(10);
            Check(UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Count(c => c.name == "Ending Report") == 1, "Duplicate report");
            Debug.Log("[OFFLINE_DEATH_SMOKE] PASS nonfatal damage, fatal damage, report, pause, duplicate guard and online isolation");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogError("[OFFLINE_DEATH_SMOKE] FAIL " + error); EditorApplication.Exit(1); }
    }
}
