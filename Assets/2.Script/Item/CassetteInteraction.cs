using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 카세트와 보상은 현재 오프라인에서만 처리한다. 서버 동기화는 아직 연결하지 않았다.
[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public class CassetteInteraction : MonoBehaviour
{
    private static readonly HashSet<CassetteInteraction> Cassettes = new HashSet<CassetteInteraction>();
    private static int lastUseFrame = -1;
    private static int closestFrame = -1;
    private static CassetteInteraction closestThisFrame;

    [Min(0.5f)] public float interactionRange = 2.5f;
    [Min(0.1f)] public float rewardAfterSeconds = 3f;
    [Min(0.3f)] public float rewardDistance = 0.9f;
    [Range(0f, 1f)] public float playbackVolume = 0.8f;

    private AudioSource speaker;
    private GameObject reward;
    private float playedSeconds;
    private bool playing;

    private void Awake()
    {
        speaker = GetComponent<AudioSource>();
        speaker.playOnAwake = false;
        speaker.Stop();
        speaker.loop = true;
        speaker.volume = playbackVolume;
        speaker.spatialBlend = 1f;
        speaker.rolloffMode = AudioRolloffMode.Linear;
        speaker.minDistance = 3f;
        speaker.maxDistance = 18f;
        speaker.dopplerLevel = 0f;
    }

    private void OnEnable()
    {
        Cassettes.Add(this);
        closestFrame = -1;
    }

    private void OnDisable()
    {
        Cassettes.Remove(this);
        closestFrame = -1;
        if (speaker != null) speaker.Stop();
        playing = false;
        playedSeconds = 0f;
    }

    public void SetReward(GameObject item)
    {
        reward = item;
        if (reward != null) reward.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F) && lastUseFrame != Time.frameCount &&
            !ItemPickupInteraction.BlocksDoorInteraction &&
            !CabinetInteraction.BlocksOtherInteraction && ClosestCassette() == this)
        {
            lastUseFrame = Time.frameCount;
            playing = !playing;
            if (playing)
            {
                // 모델 축 대신 조작한 플레이어 쪽을 앞면으로 잡는다.
                PositionRewardTowardPlayer();
                if (speaker.clip != null) speaker.Play();
            }
            else
            {
                speaker.Stop();
                playedSeconds = 0f;
            }
        }

        if (!playing || reward == null || !speaker.isPlaying) return;
        playedSeconds += Time.deltaTime;
        if (playedSeconds < rewardAfterSeconds) return;

        // 끄지 않고 3초간 재생했을 때만 한 번 공개한다.
        reward.SetActive(true);
        reward = null;
    }

    private void PositionRewardTowardPlayer()
    {
        if (reward == null) return;
        Transform player = NetworkClient.Instance.localPlayerTransform;
        Vector3 direction = player.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) direction = transform.parent != null
            ? transform.parent.forward : Vector3.forward;
        direction.y = 0f;
        direction.Normalize();

        Vector3 target = transform.position + direction * rewardDistance;
        if (NavMesh.SamplePosition(target, out NavMeshHit floor, 1.5f, NavMesh.AllAreas))
            target.y = floor.position.y + 0.25f;
        else
            target.y = transform.parent != null ? transform.parent.position.y + 0.25f : 0.25f;
        reward.transform.position = target;
    }

    private void OnGUI()
    {
        if (ClosestCassette() != this || ItemPickupInteraction.BlocksDoorInteraction ||
            CabinetInteraction.BlocksOtherInteraction) return;
        InteractionPrompt.Draw(playing ? "F  카세트 끄기" : "F  카세트 켜기");
    }

    public static bool BlocksOtherInteraction => lastUseFrame == Time.frameCount ||
        (ClosestCassette() != null && !ItemPickupInteraction.BlocksDoorInteraction);

    private static CassetteInteraction ClosestCassette()
    {
        if (closestFrame == Time.frameCount) return closestThisFrame;
        closestFrame = Time.frameCount;
        closestThisFrame = null;

        NetworkClient network = NetworkClient.Instance;
        if (network == null || !network.offlineMode || network.localPlayerTransform == null ||
            Cursor.lockState != CursorLockMode.Locked ||
            (ClueManager.instance != null && ClueManager.instance.IsEnding)) return null;
        Transform player = network.localPlayerTransform;
        PlayerController controller = player.GetComponent<PlayerController>();
        if (controller == null || !controller.isLocalPlayer || controller.IsDeadState) return null;

        float nearest = float.PositiveInfinity;
        foreach (CassetteInteraction cassette in Cassettes)
        {
            if (cassette == null || !cassette.isActiveAndEnabled || cassette.speaker == null ||
                cassette.speaker.clip == null) continue;
            float distance = (cassette.transform.position - player.position).sqrMagnitude;
            if (distance > cassette.interactionRange * cassette.interactionRange || distance >= nearest) continue;
            nearest = distance;
            closestThisFrame = cassette;
        }
        return closestThisFrame;
    }
}
