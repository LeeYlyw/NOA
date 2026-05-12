using UnityEngine;

// 파일 이름이 반드시 ClueDetector.cs 여야 합니다!
public class ClueDetector : MonoBehaviour
{
    [Header("설정")]
    public AudioSource beepSound;
    public float maxDetectionDist = 15f;

    private PlayerRoleSetup roleSetup;

    void Start()
    {
        // PlayerRoleSetup이 같은 오브젝트에 있는지 확인
        roleSetup = GetComponent<PlayerRoleSetup>();

        if (beepSound != null)
        {
            beepSound.loop = true;
            beepSound.playOnAwake = false;
        }
        else
        {
            Debug.LogError("ClueDetector: AudioSource가 연결되지 않았습니다!");
        }
    }

    void Update()
    {
        // 역할 정보가 없거나, 내 캐릭터가 아니거나, 탐지기(0번)가 아니면 종료
        if (roleSetup == null) return;
        if (roleSetup.ownerClientId != roleSetup.myClientId || roleSetup.ownerClientId != 0) return;

        // 맵에서 "Clue" 태그를 가진 단서들 찾기
        GameObject[] clues = GameObject.FindGameObjectsWithTag("Clue");

        if (clues.Length == 0)
        {
            if (beepSound.isPlaying) beepSound.Stop();
            return;
        }

        float closestDist = float.MaxValue;
        foreach (GameObject clue in clues)
        {
            float dist = Vector3.Distance(transform.position, clue.transform.position);
            if (dist < closestDist) closestDist = dist;
        }

        // 거리 기반 소리 로직
        if (closestDist <= maxDetectionDist)
        {
            if (!beepSound.isPlaying) beepSound.Play();

            // 볼륨: 가까울수록 1, 멀어질수록 0
            float volumeRatio = 1f - (closestDist / maxDetectionDist);
            beepSound.volume = Mathf.Clamp01(volumeRatio);

            // 피치: 가까울수록 고음 (1.0 ~ 3.0)
            float pitchRatio = 1f - (closestDist / maxDetectionDist);
            beepSound.pitch = Mathf.Lerp(1f, 3f, pitchRatio);
        }
        else
        {
            if (beepSound.isPlaying) beepSound.Stop();
        }
    }
}