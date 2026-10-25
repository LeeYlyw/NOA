using UnityEngine;

// Spatial cues follow accepted gameplay state; no Animator, bone or motion edits.
public class MonsterPresentation : MonoBehaviour
{
    public AudioClip discoveryClip;
    public AudioClip searchClip;
    public AudioClip attackClip;
    public AudioClip footstepClip;
    AudioSource voice;
    RemoteMonster remote;
    MonsterChase vision;
    SoundMonsterAI sound;
    MimicLaptop mimic;
    string lastState = "Idle";
    float lastAttack;
    float lastAlert = -10;
    float lastSearch = -10;
    Vector3 previous;
    float walked;

    void Awake()
    {
        remote = GetComponent<RemoteMonster>();
        vision = GetComponent<MonsterChase>();
        sound = GetComponent<SoundMonsterAI>();
        mimic = GetComponent<MimicLaptop>();
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 1;
        voice.rolloffMode = AudioRolloffMode.Linear;
        voice.minDistance = 2; voice.maxDistance = 18;
        voice.dopplerLevel = 0;
        previous = transform.position;
    }

    void LateUpdate()
    {
        NetworkClient network = NetworkClient.Instance;
        bool online = network != null && !network.offlineMode;
        if (online && !network.GameStarted) { previous = transform.position; return; }
        string state = mimic != null ? mimic.PresentationState : online && remote != null ? remote.PresentationState :
            vision != null ? vision.PresentationState : sound != null ? sound.PresentationState : "Idle";
        float attack = mimic != null ? mimic.PresentationAttackSequence : online && remote != null ? remote.PresentationAttackSequence :
            vision != null ? vision.PresentationAttackTime : sound != null ? sound.PresentationAttackTime : 0;
        bool aggressive = state == "Chase" || state == "Attack" || state == "Warning" || state == "Repeating";
        bool wasAggressive = lastState == "Chase" || lastState == "Attack" || lastState == "Warning" || lastState == "Repeating";
        if (aggressive && !wasAggressive && Time.time - lastAlert > 3)
        { Play(discoveryClip, "alert", 0.45f); lastAlert = Time.time; }
        if ((state == "Search" || state == "Investigate") && state != lastState && Time.time - lastSearch > 4)
        { Play(searchClip, "search", 0.2f); lastSearch = Time.time; }
        if (attack > lastAttack) Play(attackClip, "attack", 0.4f);
        lastAttack = attack; lastState = state;
        float distance = Vector3.Distance(previous, transform.position);
        previous = transform.position;
        if (distance > 3) { walked = 0; return; } // Spawn/teleport is not a footstep.
        walked += distance;
        if (mimic == null && walked >= (aggressive ? 1.2f : 1f))
        { Play(footstepClip, "step", aggressive ? 0.28f : 0.16f); walked = 0; }
    }

    void Play(AudioClip clip, string fallback, float volume)
    {
        NetworkClient network = NetworkClient.Instance;
        Transform listener = network != null ? network.localPlayerTransform : null;
        if (listener != null)
        {
            Vector3 eye = listener.position + Vector3.up * 1.3f;
            if (Vector3.Distance(eye, transform.position) > voice.maxDistance) return;
            if (Physics.Linecast(transform.position + Vector3.up, eye, out RaycastHit hit,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                !hit.transform.IsChildOf(listener) && !hit.transform.IsChildOf(transform)) volume *= 0.25f;
        }
        voice.PlayOneShot(clip != null ? clip : GamePresentation.Ensure().Tone(fallback), volume);
    }
}
