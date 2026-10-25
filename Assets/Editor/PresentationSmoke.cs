using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PresentationSmoke
{
    const string Key = "NOA.PresentationSmoke";
    static int frames;
    static PresentationSmoke() { EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    static void Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || ++frames < 20) return;
        SessionState.SetBool(Key, false);
        try
        {
            NetworkClient network = new GameObject("Network").AddComponent<NetworkClient>();
            network.offlineMode = true;
            ClueManager clues = new GameObject("Clues").AddComponent<ClueManager>();
            GamePresentation feedback = GamePresentation.Ensure();
            GameObject root = new GameObject("Puzzle");
            CoopShapePuzzle puzzle = root.AddComponent<CoopShapePuzzle>();
            foreach (string name in new[] { "Blue description pad", "Orange selection pad", "Sigil locked room gate" })
            {
                GameObject child = GameObject.CreatePrimitive(PrimitiveType.Cube);
                child.name = name; child.transform.SetParent(root.transform);
            }
            PuzzlePresentation presentation = root.AddComponent<PuzzlePresentation>();
            presentation.enabled = false;
            Invoke(presentation, "Start");
            Invoke(presentation, "Update");
            int baseline = feedback.NotificationCount;
            puzzle.SetPads(new[] { "pads", "1", "0" }); Invoke(presentation, "Update");
            Check(feedback.NotificationCount == baseline + 1, "Pad entry cue missing");
            Invoke(presentation, "Update");
            Check(feedback.NotificationCount == baseline + 1, "Pad cue repeated");
            puzzle.SetPads(new[] { "pads", "1", "1" }); Invoke(presentation, "Update");
            Check(feedback.NotificationCount == baseline + 2, "Both-ready cue missing");
            puzzle.SetProgress(new[] { "progress", "2", "3" }); Invoke(presentation, "Update");
            Check(feedback.NotificationCount == baseline + 3, "Correct-answer cue missing");
            puzzle.SetWrong(); Invoke(presentation, "Update");
            Check(feedback.NotificationCount == baseline + 4, "Wrong-answer cue missing");
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            root.transform.Find("Blue description pad").GetComponent<Renderer>().GetPropertyBlock(block);
            Check(block.GetColor("_BaseColor").r > 0.8f, "Wrong-answer pad flash missing");
            puzzle.SetSolved(); Invoke(presentation, "Update");
            Check(feedback.NotificationCount == baseline + 5, "Solved cue missing");
            Invoke(presentation, "Update");
            Check(feedback.NotificationCount == baseline + 5, "Solved cue repeated");
            clues.SetClueCount(1, 3); Invoke(feedback, "Update");
            Check(feedback.NotificationCount == baseline + 6, "Shared clue cue missing");
            Invoke(feedback, "Update"); Check(feedback.NotificationCount == baseline + 6, "Clue cue repeated");

            GameObject monster = new GameObject("Monster");
            RemoteMonster remote = monster.AddComponent<RemoteMonster>();
            MonsterPresentation monsterFx = monster.AddComponent<MonsterPresentation>();
            network.offlineMode = false;
            typeof(NetworkClient).GetProperty("GameStarted").SetValue(network, true);
            remote.PresentationState = "Chase";
            remote.SetAnimationState(3, true, false, 1);
            Invoke(monsterFx, "LateUpdate");
            Check((string)typeof(MonsterPresentation).GetField("lastState", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(monsterFx) == "Chase", "Server state not presented");
            Check((float)typeof(MonsterPresentation).GetField("lastAttack", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(monsterFx) == 1f, "Attack sequence not consumed");
            Check(monster.GetComponents<AudioSource>().Length == 1, "Duplicate monster audio sources");
            Check(feedback.Tone("step") == feedback.Tone("step"), "Audio clip cache failed");
            float[] data = new float[feedback.Tone("alert").samples]; feedback.Tone("alert").GetData(data, 0);
            foreach (float sample in data) Check(!float.IsNaN(sample) && Mathf.Abs(sample) <= 1, "Invalid audio sample");
            network.offlineMode = true;
            clues.SetClueCount(3, 3); clues.ShowEnding(); Invoke(feedback, "Update");
            Check(feedback.NotificationCount == baseline + 7, "Completion cue missing while paused");
            Time.timeScale = 1;
            Camera camera = new GameObject("Preview Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.1f, 0.13f, 0.15f);
            foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
            foreach (CanvasGroup group in feedback.GetComponentsInChildren<CanvasGroup>()) group.alpha = 1;
            Invoke(clues.GetComponent<ObjectiveHUD>(), "Update");
            RenderTexture target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes("Library/PresentationPreview.png", image.EncodeToPNG());
            RenderTexture.active = null; camera.targetTexture = null;
            UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(target);
            Debug.Log("[PRESENTATION_SMOKE] PASS puzzle cues and deduplication, shared clue, pause-safe clear, server monster state, cached audio and valid samples");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogError("[PRESENTATION_SMOKE] FAIL " + error); EditorApplication.Exit(1); }
    }
}
