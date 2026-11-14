using UnityEngine;
using UnityEngine.Rendering;

// Visibility is local presentation only; the server continues to own AI and damage.
public class MonsterCloak : MonoBehaviour
{
    public float revealDistance = 8f;
    Renderer[] visuals;
    Material[][] originals;
    Material[][] cloaked;
    ShadowCastingMode[] shadows;
    Material shimmer;
    bool revealed;

    void Start()
    {
        Shader shader = Resources.Load<Shader>("MonsterCloak");
        if (shader == null) { Debug.LogError("[MonsterCloak] Shimmer shader missing."); enabled = false; return; }
        shimmer = new Material(shader);
        visuals = GetComponentsInChildren<Renderer>(true);
        originals = new Material[visuals.Length][];
        cloaked = new Material[visuals.Length][];
        shadows = new ShadowCastingMode[visuals.Length];
        for (int i = 0; i < visuals.Length; i++)
        {
            originals[i] = visuals[i].sharedMaterials;
            shadows[i] = visuals[i].shadowCastingMode;
            cloaked[i] = new Material[originals[i].Length];
            for (int slot = 0; slot < cloaked[i].Length; slot++) cloaked[i][slot] = shimmer;
        }
        revealed = true;
        SetRevealed(false);
    }

    void LateUpdate()
    {
        NetworkClient network = NetworkClient.Instance;
        Transform viewer = network != null ? network.localPlayerTransform : null;
        bool nearby = viewer != null && (viewer.position - transform.position).sqrMagnitude <= revealDistance * revealDistance;
        SetRevealed(nearby);
    }

    void SetRevealed(bool value)
    {
        if (visuals == null || revealed == value) return;
        revealed = value;
        for (int i = 0; i < visuals.Length; i++)
        {
            if (visuals[i] == null) continue;
            visuals[i].sharedMaterials = value ? originals[i] : cloaked[i];
            visuals[i].shadowCastingMode = value ? shadows[i] : ShadowCastingMode.Off;
        }
    }

    void OnDisable() { SetRevealed(true); }
    void OnDestroy() { SetRevealed(true); if (shimmer != null) Destroy(shimmer); }
}
