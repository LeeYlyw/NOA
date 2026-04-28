using UnityEngine;
using UnityEngine.AI;

public class MonsterNetworkSetup : MonoBehaviour
{
    [Header("Monster Id")]
    public int monsterId = 1;

    [Header("Authority")]
    public bool hasAuthority;

    [Header("AI Scripts")]
    public MonoBehaviour[] aiScripts;

    [Header("Components")]
    public NavMeshAgent navMeshAgent;
    public RemoteMonster remoteMonster;

    public void SetupMonster(bool isAuthority)
    {
        hasAuthority = isAuthority;

        foreach (MonoBehaviour ai in aiScripts)
        {
            if (ai != null)
                ai.enabled = isAuthority;
        }

        if (navMeshAgent != null)
            navMeshAgent.enabled = isAuthority;

        if (remoteMonster != null)
            remoteMonster.enabled = !isAuthority;
    }
}