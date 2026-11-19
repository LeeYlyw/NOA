using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
    Vector3 previousPosition;
    float motionStrength = 0.25f;
    Camera viewerCamera;
    static readonly int MotionStrength = Shader.PropertyToID("_MotionStrength");

    void Start()
    {
        Shader shader = Resources.Load<Shader>("MonsterCloak");
        if (shader == null) { Debug.LogError("[MonsterCloak] Shimmer shader missing."); enabled = false; return; }
        shimmer = new Material(shader);
        previousPosition = transform.position;
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
        if (viewer != null && (viewerCamera == null || !viewerCamera.transform.IsChildOf(viewer)))
        {
            viewerCamera = viewer.GetComponentInChildren<Camera>();
            if (viewerCamera != null) viewerCamera.GetUniversalAdditionalCameraData().requiresColorTexture = true;
        }
        float speed = (transform.position - previousPosition).magnitude / Mathf.Max(Time.deltaTime, 0.001f);
        previousPosition = transform.position;
        motionStrength = Mathf.MoveTowards(motionStrength, Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(speed / 3.8f)), Time.deltaTime * 3f);
        if (shimmer != null) shimmer.SetFloat(MotionStrength, motionStrength);
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
