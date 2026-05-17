using UnityEngine;

public class ClueDetector : MonoBehaviour
{
    [Header("설정")]
    public AudioSource beepSound;
    public float maxDetectionDist = 15f;

    private PlayerRoleSetup roleSetup;

    void Start()
    {
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
        if (!CanUseDetector())
        {
            StopBeep();
            return;
        }

        GameObject[] clues = GameObject.FindGameObjectsWithTag("Clue");

        if (clues.Length == 0)
        {
            StopBeep();
            return;
        }

        float closestDist = float.MaxValue;

        foreach (GameObject clue in clues)
        {
            float dist = Vector3.Distance(transform.position, clue.transform.position);

            if (dist < closestDist)
                closestDist = dist;
        }

        if (closestDist <= maxDetectionDist)
        {
            if (beepSound != null && !beepSound.isPlaying)
                beepSound.Play();

            float ratio = 1f - (closestDist / maxDetectionDist);

            if (beepSound != null)
            {
                beepSound.volume = Mathf.Clamp01(ratio);
                beepSound.pitch = Mathf.Lerp(1f, 3f, ratio);
            }
        }
        else
        {
            StopBeep();
        }
    }

    private bool CanUseDetector()
    {
        if (roleSetup == null)
            return false;

        // 감지자 역할이 아니면 사용 불가
        if (!roleSetup.IsDetector)
            return false;

        // 내 클라이언트에서 내가 조작하는 감지자일 때만 소리 재생
        // 이걸 빼면 탐색자 클라이언트에서도 상대 감지자의 소리가 들릴 수 있음
        if (!roleSetup.IsLocalOwner)
            return false;

        return true;
    }

    private void StopBeep()
    {
        if (beepSound != null && beepSound.isPlaying)
            beepSound.Stop();
    }
}