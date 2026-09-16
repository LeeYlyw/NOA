using System.Collections;
using UnityEngine;

public class DoorInteraction : MonoBehaviour
{
    [Header("UI & References")]
    public GameObject interactionUI;
    public Transform doorHinge;

    [Header("Door Settings")]
    public float openAngle = 90f;
    public float rotateSpeed = 2f;

    private bool isNear = false;
    private bool isOpen = false;
    private Coroutine doorCoroutine;

    // 추가된 부분: 문의 닫힌 상태와 열린 상태의 회전값을 미리 저장
    private Quaternion closedRotation;
    private Quaternion openRotation;

    void Start()
    {
        if (interactionUI != null) interactionUI.SetActive(false);

        // 현재 문의 회전값을 '닫힌 상태'로 저장
        closedRotation = doorHinge.localRotation;

        // 닫힌 상태에서 Y축으로 openAngle만큼 회전한 값을 '열린 상태'로 계산
        openRotation = closedRotation * Quaternion.Euler(0, openAngle, 0);
    }

    void Update()
    {
        if (isNear && Input.GetKeyDown(KeyCode.F))
        {
            isOpen = !isOpen;
            Debug.Log($"[동작 테스트] F키 입력 성공! isOpen 상태 : {isOpen}");

            if (doorCoroutine != null) StopCoroutine(doorCoroutine);

            Quaternion targetRotation = isOpen ? openRotation : closedRotation;

            // 회전축이 연결되어 있는지 확인하는 안전장치
            if (doorHinge != null)
            {
                Debug.Log($"[동작 테스트] 회전 시작! 목표 회전값 : {targetRotation.eulerAngles}");
                doorCoroutine = StartCoroutine(RotateDoor(targetRotation));
            }
            else
            {
                Debug.LogError("[에러] Door Hinge가 연결되지 않았습니다! 인스펙터 창을 확인하세요.");
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isNear = true;
            if (interactionUI != null) interactionUI.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isNear = false;
            if (interactionUI != null) interactionUI.SetActive(false);
        }
    }

    private IEnumerator RotateDoor(Quaternion targetRotation)
    {
        Quaternion startRotation = doorHinge.localRotation;
        float time = 0;

        while (time < 1f)
        {
            time += Time.deltaTime * rotateSpeed;
            doorHinge.localRotation = Quaternion.Slerp(startRotation, targetRotation, time);
            yield return null;
        }

        doorHinge.localRotation = targetRotation;
        Debug.Log("[동작 테스트] 문 회전 완료!");
    }

}