using UnityEngine;
using System.Collections;

public class PlayerStealth : MonoBehaviour
{
    public bool isStealth = false;
    public float stealthDuration = 5f; // 은신 지속 시간 (5초)

    [Header("Stealth Target")]
    [Tooltip("플레이어 자식에 있는 'model' 오브젝트를 여기에 드래그해서 넣어주세요.")]
    public GameObject playerModel; //  껐다 켤 자식 모델 오브젝트

    public void ActivateStealth()
    {
        // 1. 이미 은신 중이면 중복 실행 막기
        if (isStealth)
        {
            Debug.LogWarning("이미 은신 중입니다!");
            return;
        }

        // 2. 인스펙터에 model 오브젝트를 안 넣었을 때를 위한 안전장치
        if (playerModel == null)
        {
            Debug.LogError("PlayerStealth :: 'Player Model' 슬롯이 비어있습니다! 자식의 model 오브젝트를 연결해주세요.");
            return;
        }

        // 은신 루틴 시작
        StartCoroutine(StealthRoutine());
    }

    IEnumerator StealthRoutine()
    {
        isStealth = true;
        Debug.Log("은신 시작! (귀신이 플레이어를 감지하지 못하는 기믹을 여기에 연결하세요)");

        //  [해결] 자식 model 오브젝트를 비활성화해서 눈에서 감추기
        playerModel.SetActive(false);

        // 5초 동안 은신 유지
        yield return new WaitForSeconds(stealthDuration);

        //  [해결] 5초 뒤 자식 model 오브젝트를 다시 활성화해서 짠! 하고 나타나기
        if (playerModel != null)
        {
            playerModel.SetActive(true);
        }

        isStealth = false;
        Debug.Log("은신 해제!");
    }
}