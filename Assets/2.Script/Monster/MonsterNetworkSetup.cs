using UnityEngine;
using UnityEngine.AI;

public class MonsterNetworkSetup : MonoBehaviour
{
    [Header("Monster Id")]
    public int monsterId = 1;

    [Header("Components")]
    public NavMeshAgent navMeshAgent;
    public RemoteMonster remoteMonster;
    public MonoBehaviour[] aiScripts;

    void Start()
    {
        // NetworkClient의 offlineMode 확인
        bool isOffline = (NetworkClient.Instance != null && NetworkClient.Instance.offlineMode);

        if (isOffline)
        {
            SetupOfflineMode();
        }
        else
        {
            SetupServerAuthoritativeMode();
        }
    }

    // 싱글 테스트 시: 유니티 자체 AI 구동
    void SetupOfflineMode()
    {
        if (aiScripts != null)
        {
            foreach (MonoBehaviour ai in aiScripts)
            {
                if (ai != null) ai.enabled = true;
            }
        }

        if (navMeshAgent != null) navMeshAgent.enabled = true;
        if (remoteMonster != null) remoteMonster.enabled = false;

        Debug.Log($"[MonsterNetworkSetup] 몬스터 ID({monsterId}) :: 싱글 테스트용 로컬 AI 모드 활성화");
    }

    // 멀티플레이 시: C++ 서버 데이터 수신 전용 모드
    void SetupServerAuthoritativeMode()
    {
        if (aiScripts != null)
        {
            foreach (MonoBehaviour ai in aiScripts)
            {
                if (ai != null) ai.enabled = false;
            }
        }

        if (navMeshAgent != null) navMeshAgent.enabled = false;
        if (remoteMonster != null) remoteMonster.enabled = true;

        Debug.Log($"[MonsterNetworkSetup] 몬스터 ID({monsterId}) :: C++ 서버 주도 모드 설정 완료");
    }

    public void SetupMonster(bool isAuthority)
    {
        // [수정] 매개변수를 무시하던 버그 수정
        if (isAuthority)
        {
            // 권한이 있다면 내가 직접 AI를 굴림 (Master Client 역할)
            SetupOfflineMode();
        }
        else
        {
            // 권한이 없다면 서버(또는 방장)가 보내는 Transform 데이터만 받음
            SetupServerAuthoritativeMode();
        }
    }
}