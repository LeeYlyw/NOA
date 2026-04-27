using UnityEngine;
using UnityEngine.UI;

public class Slot : MonoBehaviour
{
    public ItemData item;
    public Image iconImage;

    // 아이템 데이터를 슬롯에 등록
    public void SetItem(ItemData newItem)
    {
        item = newItem;
        iconImage.sprite = item.icon;
        iconImage.enabled = true;
    }

    // 슬롯 비우기
    public void ClearSlot()
    {
        item = null;
        iconImage.sprite = null;
        iconImage.enabled = false;
    }

    // [중요] 버튼 클릭 시 실행될 함수
    public void OnClickSlot()
    {
        if (item == null) return; // 아이템이 없으면 실행 안 함

        // 1. 아이템 타입 확인
        if (item.type == ItemData.ItemType.Heal)
        {
            PlayerController player = FindObjectOfType<PlayerController>();

            if (player != null)
            {
                player.HealToFull();
                Debug.Log("회복 아이템 사용 완료!");

                ClearSlot();
            }
            else
            {
                Debug.LogError("PlayerController를 찾을 수 없습니다!");
            }
        }

        // Slot.cs 의 OnClickSlot 내부
        else if (item.type == ItemData.ItemType.Stealth) // 은신 타입일 때
        {

            // 씬에서 플레이어의 PlayerStealth 스크립트를 찾습니다.
            PlayerStealth stealth = FindObjectOfType<PlayerStealth>();

            if (stealth != null)
            {
                stealth.ActivateStealth(); // 은신 발동! (투명화 & 괴물 무시)
                Debug.Log("연막탄 투척! 은신합니다.");
                ClearSlot(); // 사용 후 아이템 삭제
            }
            else
            {
                Debug.LogError("플레이어에게 PlayerStealth 스크립트가 없습니다!");
            }
        }
        // Slot.cs 의 OnClickSlot 내부 마지막 부분에 추가
        else if (item.type == ItemData.ItemType.Teleport)
        {
            // 1. 맵에서 이동할 지점(TeleportPoint)을 찾습니다.
            GameObject targetPoint = GameObject.FindGameObjectWithTag("TeleportPoint");

            if (targetPoint != null)
            {
                // 2. 플레이어를 찾습니다.
                GameObject player = GameObject.FindGameObjectWithTag("Player");

                if (player != null)
                {
                    // [중요] 캐릭터 컨트롤러가 켜져 있으면 좌표 이동이 안 될 수 있으므로 잠시 끕니다.
                    CharacterController cc = player.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;

                    // 3. 위치 이동!
                    player.transform.position = targetPoint.transform.position;

                    // 다시 켭니다.
                    if (cc != null) cc.enabled = true;

                    Debug.Log("순간이동 완료!");
                    ClearSlot(); // 아이템 소모
                }
            }
            else
            {
                Debug.LogError("맵에 'TeleportPoint' 태그를 가진 오브젝트가 없습니다!");
            }
        }
        else if (item.type == ItemData.ItemType.Resurrection)
        {
            // 1. 씬에 있는 PlayerController라는 '컴포넌트'를 찾아서 'pc'라는 변수에 담습니다.
            PlayerController pc = FindObjectOfType<PlayerController>();

            if (pc != null)
            {
                // --- [추가] 플레이어가 죽었을 때만 아이템 사용 가능하게 제한 ---
                if (pc.IsDead()) // IsDead()라는 체크용 함수가 필요합니다. (아래 2번 참고)
                {
                    pc.Revive();
                    Debug.Log("부활 아이템 사용 성공!");
                    ClearSlot();
                }
                else
                {
                    // 아직 살아있다면 아이템을 쓰지 않고 로그만 띄웁니다.
                    Debug.Log("플레이어가 아직 살아있어서 부활 아이템을 사용할 수 없습니다.");
                }
            }
        }
    }
}