using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 의자 상호작용은 현재 로컬에서만 처리한다. 서버 동기화는 아직 연결하지 않았다.
[DisallowMultipleComponent]
public class ChairInteraction : MonoBehaviour
{
    private static readonly HashSet<ChairInteraction> Chairs = new HashSet<ChairInteraction>();
    // 여러 의자가 범위에 들어와도 가장 가까운 하나만 F 입력을 받는다.
    private static int lastUseFrame = -1;
    private static int closestFrame = -1;
    private static ChairInteraction closestThisFrame;

    [Min(0.5f)] public float interactionRange = 2.5f;
    [Min(0.3f)] public float pullDistance = 1.8f;
    [Min(0.05f)] public float moveDuration = 0.35f;
    public Transform desk;
    public Vector3 fallbackPullDirection = Vector3.back;

    private Vector3 homeLocalPosition;
    private Vector3 pulledLocalPosition;
    private bool pulled;
    private Coroutine movement;
    private GameObject hiddenItem;

    private void Awake()
    {
        // 배치된 원래 위치를 저장해 두 번째 F 입력 때 정확히 돌아오게 한다.
        homeLocalPosition = transform.localPosition;
    }

    private void Start()
    {
        // 가까운 책상의 반대 방향으로 빼고, 책상이 없으면 의자 뒤쪽을 사용한다.
        if (desk == null) desk = FindNearestDesk();
        Vector3 direction = desk != null ? transform.position - desk.position :
            transform.TransformDirection(fallbackPullDirection);
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) direction = -transform.forward;
        direction.y = 0f;
        direction.Normalize();
        Vector3 worldOffset = direction * pullDistance;
        Vector3 localOffset = transform.parent != null
            ? transform.parent.InverseTransformVector(worldOffset) : worldOffset;
        pulledLocalPosition = homeLocalPosition + localOffset;
    }

    private void OnEnable()
    {
        Chairs.Add(this);
        closestFrame = -1;
    }

    private void OnDisable()
    {
        Chairs.Remove(this);
        closestFrame = -1;
        if (movement != null) StopCoroutine(movement);
        movement = null;
    }

    private void Update()
    {
        // 아이템과 의자가 겹치면 F 홀드 아이템 획득을 우선한다.
        if (!Input.GetKeyDown(KeyCode.F) || lastUseFrame == Time.frameCount ||
            ItemPickupInteraction.BlocksDoorInteraction ||
            CassetteInteraction.BlocksOtherInteraction ||
            BarrelInteraction.BlocksOtherInteraction ||
            BedInteraction.BlocksOtherInteraction || ClosestChair() != this) return;

        lastUseFrame = Time.frameCount;
        pulled = !pulled;
        if (movement != null) StopCoroutine(movement);
        movement = StartCoroutine(MoveTo(pulled ? pulledLocalPosition : homeLocalPosition));
    }

    private IEnumerator MoveTo(Vector3 target)
    {
        Vector3 start = transform.localPosition;
        float elapsed = 0f;
        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / moveDuration));
            transform.localPosition = Vector3.LerpUnclamped(start, target, t);
            yield return null;
        }
        transform.localPosition = target;
        movement = null;
        // 의자가 완전히 빠진 뒤 아이템을 공개한다. 다시 넣어도 숨기지 않는다.
        if (pulled && hiddenItem != null)
        {
            hiddenItem.SetActive(true);
            hiddenItem = null;
        }
    }

    public void SetHiddenItem(GameObject item)
    {
        // 맵 생성기가 선택된 의자에만 비활성 아이템을 연결한다.
        hiddenItem = item;
        if (hiddenItem != null) hiddenItem.SetActive(false);
    }

    // 아이템을 의자의 원래 자리에서 바닥보다 조금 위에 배치한다.
    public Vector3 HiddenItemPosition => transform.parent != null
        ? transform.parent.TransformPoint(homeLocalPosition) + Vector3.up * 0.25f
        : homeLocalPosition + Vector3.up * 0.25f;

    private void OnGUI()
    {
        if (ClosestChair() != this || ItemPickupInteraction.BlocksDoorInteraction ||
            CassetteInteraction.BlocksOtherInteraction ||
            BarrelInteraction.BlocksOtherInteraction ||
            BedInteraction.BlocksOtherInteraction) return;
        InteractionPrompt.Draw(pulled ? "F  의자 넣기" : "F  의자 빼기");
    }

    // 의자 조작에 사용한 F가 근처 문까지 열지 않도록 막는다.
    public static bool BlocksDoorInteraction => lastUseFrame == Time.frameCount ||
        (ClosestChair() != null && !ItemPickupInteraction.BlocksDoorInteraction);

    private static ChairInteraction ClosestChair()
    {
        // OnGUI에서도 호출되므로 가장 가까운 의자는 프레임마다 한 번만 찾는다.
        if (closestFrame == Time.frameCount) return closestThisFrame;
        closestFrame = Time.frameCount;
        closestThisFrame = null;
        NetworkClient network = NetworkClient.Instance;
        if (network == null || network.localPlayerTransform == null ||
            (!network.offlineMode && (!network.GameStarted || !network.IsConnected)) ||
            Cursor.lockState != CursorLockMode.Locked ||
            (ClueManager.instance != null && ClueManager.instance.IsEnding)) return null;

        Transform player = network.localPlayerTransform;
        PlayerController controller = player.GetComponent<PlayerController>();
        if (controller == null || !controller.isLocalPlayer || controller.IsDeadState) return null;

        ChairInteraction closest = null;
        float nearest = float.PositiveInfinity;
        foreach (ChairInteraction chair in Chairs)
        {
            if (chair == null || !chair.isActiveAndEnabled) continue;
            Vector3 home = chair.transform.parent != null
                ? chair.transform.parent.TransformPoint(chair.homeLocalPosition)
                : chair.homeLocalPosition;
            // 의자를 뺀 뒤에도 원래 자리 근처에서 F로 다시 넣을 수 있다.
            float distance = Mathf.Min((chair.transform.position - player.position).sqrMagnitude,
                (home - player.position).sqrMagnitude);
            if (distance > chair.interactionRange * chair.interactionRange || distance >= nearest) continue;
            nearest = distance;
            closest = chair;
        }
        closestThisFrame = closest;
        return closestThisFrame;
    }

    private Transform FindNearestDesk()
    {
        Transform room = transform.parent;
        if (room == null) return null;
        Transform nearest = null;
        float best = 3f * 3f;
        foreach (Transform candidate in room.GetComponentsInChildren<Transform>(true))
        {
            if (candidate == transform || candidate.IsChildOf(transform)) continue;
            string name = candidate.name;
            if (name.IndexOf("Desk", StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("Table", StringComparison.OrdinalIgnoreCase) < 0) continue;
            float distance = (candidate.position - transform.position).sqrMagnitude;
            if (distance >= best) continue;
            best = distance;
            nearest = candidate;
        }
        return nearest;
    }
}
