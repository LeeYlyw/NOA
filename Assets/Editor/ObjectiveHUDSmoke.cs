using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class ObjectiveHUDSmoke
{
    const string Key = "NOA.ObjectiveHUDSmoke";
    static int frames;
    static int phase;
    static readonly string Capture = "Library/ObjectiveHUDPreview.png";

    static ObjectiveHUDSmoke() { EditorApplication.update += Tick; }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || ++frames < 20) return;
        try
        {
            if (phase == 0)
            {
                phase = 1;
                Camera camera = new GameObject("Preview camera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.15f, 0.18f);
                NetworkClient network = new GameObject("Network").AddComponent<NetworkClient>();
                network.offlineMode = true;
                ClueManager manager = new GameObject("Clue manager").AddComponent<ClueManager>();
                ObjectiveHUD hud = manager.GetComponent<ObjectiveHUD>();
                CoopShapePuzzle puzzle = new GameObject("Puzzle").AddComponent<CoopShapePuzzle>();
                MethodInfo update = typeof(ObjectiveHUD).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                Action refresh = () => update.Invoke(hud, null);
                Action<string> expect = value => {
                    if (!UnityEngine.Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.text.Contains(value)))
                        throw new Exception("Missing HUD state: " + value);
                };
                refresh(); expect("0 / 3"); expect("단서를 찾고");
                manager.SetClueCount(2, 3); refresh(); expect("협동 장치로 방을 여세요");
                puzzle.SetPads(new[] { "S_PUZZLE_PADS", "1", "1" });
                puzzle.SetProgress(new[] { "S_PUZZLE_PROGRESS", "2", "3" });
                refresh(); expect("동료와 도형"); expect("1 / 3");
                puzzle.SetSolved(); refresh(); expect("남은 단서"); expect("해제 완료");
                manager.SetClueCount(3, 3); refresh(); expect("임무 완료를 확인");
                if (manager.IsEnding) throw new Exception("HUD completed game before server confirmation");
                manager.ShowEnding(); refresh(); expect("임무 완료");
                Time.timeScale = 1f;
                Text first = UnityEngine.Object.FindFirstObjectByType<Text>();
                if (!first.font.HasCharacter('단')) throw new Exception("Font cannot render Korean");
                if (UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsSortMode.None).Any(g => g.raycastTarget))
                    throw new Exception("HUD intercepts input");
                // Capture the active puzzle state after the state-transition assertions.
                typeof(ClueManager).GetField("isEnding", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, false);
                typeof(CoopShapePuzzle).GetField("solved", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(puzzle, false);
                manager.SetClueCount(2, 3);
                refresh();
                Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                RenderTexture target = new RenderTexture(1280, 720, 24);
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                Texture2D preview = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                preview.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                preview.Apply();
                File.WriteAllBytes(Capture, preview.EncodeToPNG());
                RenderTexture.active = null;
                camera.targetTexture = null;
                UnityEngine.Object.Destroy(preview);
                UnityEngine.Object.Destroy(target);
                frames = 0;
            }
            else if (File.Exists(Capture))
            {
                Debug.Log("[HUD_SMOKE] PASS objective, shared clues, puzzle progress, clear confirmation, Korean font, non-blocking UI");
                Finish(0);
            }
            else if (frames > 300) throw new Exception("HUD screenshot was not captured");
        }
        catch (Exception error) { Debug.LogError("[HUD_SMOKE] FAIL " + error); Finish(1); }
    }

    static void Finish(int code)
    {
        SessionState.SetBool(Key, false);
        EditorApplication.Exit(code);
    }
}
