using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

public class HiderAgent : MonoBehaviour
{
    [Header("References")]
    public Transform seeker;           // Seeker 机器人的 Transform
    public TownMap townMap;
    public NavMeshAgent navAgent;

    [Header("Hiding Settings")]
    public float dangerRadius = 20f;       // Seeker 多近算危险
    public float safeRadius = 40f;         // 安全距离
    public float decisionInterval = 5f;    // 决策间隔
    public float peekInterval = 2f;        // 偷看 Seeker 位置的间隔

    [Header("Decoy Settings")]
    public GameObject[] decoyPrefabs;      // 假宝物预制体数组
    public int maxDecoys = 3;              // 最多放几个假宝物
    public float decoyPlaceRadius = 30f;   // 在多远的范围内放假宝物
    public float decoyMoveInterval = 60f;  // 每隔多久移动一次假宝物

    // 状态
    private enum HiderState
    {
        HIDING,         // 躲藏中
        FLEEING,        // 逃跑中
        PLACING_DECOY,  // 放置假宝物
        RELOCATING      // 换躲藏点
    }

    private HiderState currentState = HiderState.HIDING;
    private List<GameObject> placedDecoys = new List<GameObject>();
    private Vector3 currentHideSpot;
    private float lastSeenSeekerTime;
    private Vector3 lastKnownSeekerPos;

    void Start()
    {
        if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();

        if (seeker == null)
        {
            GameObject seekerObj = GameObject.FindGameObjectWithTag("Player");
            if (seekerObj != null) seeker = seekerObj.transform;
        }

        // 初始：找一个远离 Seeker 的位置躲起来
        currentHideSpot = FindHidingSpot();
        navAgent.SetDestination(currentHideSpot);

        StartCoroutine(DecisionLoop());
        StartCoroutine(DecoyManagementLoop());
    }

    // =====================================================================
    // 核心决策循环
    // =====================================================================
    private IEnumerator DecisionLoop()
    {
        yield return new WaitForSeconds(3f); // 开局等待

        while (true)
        {
            if (seeker == null)
            {
                yield return new WaitForSeconds(decisionInterval);
                continue;
            }

            float distToSeeker = Vector3.Distance(transform.position, seeker.position);
            lastKnownSeekerPos = seeker.position;

            // 判断 Seeker 是否能"看到"自己（简单的视线检测）
            bool seekerCanSeeMe = CanSeekerSeeMe(distToSeeker);

            if (seekerCanSeeMe || distToSeeker < dangerRadius)
            {
                // 危险！逃跑
                currentState = HiderState.FLEEING;
                Vector3 fleeTarget = GetFleePosition();
                navAgent.SetDestination(fleeTarget);

                Debug.Log($"<color=red>[Hider]</color> DANGER! Seeker at {distToSeeker:F1}m. Fleeing!");

                yield return new WaitForSeconds(1f); // 逃跑时决策更频繁
            }
            else if (distToSeeker > safeRadius && placedDecoys.Count < maxDecoys)
            {
                // 安全距离，有机会放假宝物
                if (Random.value < 0.3f) // 30% 概率放假宝物
                {
                    currentState = HiderState.PLACING_DECOY;
                    yield return StartCoroutine(PlaceDecoyCoroutine());
                }
                else
                {
                    currentState = HiderState.HIDING;
                    yield return new WaitForSeconds(decisionInterval);
                }
            }
            else if (!navAgent.pathPending && navAgent.remainingDistance < 1f)
            {
                // 到达躲藏点，考虑换位置
                if (Random.value < 0.2f)
                {
                    currentState = HiderState.RELOCATING;
                    currentHideSpot = FindHidingSpot();
                    navAgent.SetDestination(currentHideSpot);
                    Debug.Log($"<color=blue>[Hider]</color> Relocating to new hiding spot.");
                }
                else
                {
                    currentState = HiderState.HIDING;
                }

                yield return new WaitForSeconds(decisionInterval);
            }
            else
            {
                yield return new WaitForSeconds(peekInterval);
            }
        }
    }

    // =====================================================================
    // 躲藏策略
    // =====================================================================

    /// <summary>
    /// 找一个好的躲藏点：远离 Seeker + 靠近遮挡物
    /// </summary>
    private Vector3 FindHidingSpot()
    {
        Vector3 bestSpot = transform.position;
        float bestScore = float.MinValue;

        // 随机采样 10 个候选点，选最好的
        for (int i = 0; i < 10; i++)
        {
            Vector3 randomDir = Random.insideUnitSphere * safeRadius;
            randomDir.y = 0;
            Vector3 candidate = transform.position + randomDir;

            NavMeshHit hit;
            if (!NavMesh.SamplePosition(candidate, out hit, 10f, NavMesh.AllAreas))
                continue;

            Vector3 spot = hit.position;
            float distFromSeeker = seeker != null
                ? Vector3.Distance(spot, seeker.position)
                : 50f;

            // 评分：离 Seeker 越远越好 + 靠近建筑/遮挡物加分
            float score = distFromSeeker;

            // 检查该点是否被遮挡（Seeker 看不到）
            if (seeker != null)
            {
                Vector3 dirToSeeker = (seeker.position - spot).normalized;
                if (Physics.Raycast(spot + Vector3.up, dirToSeeker, distFromSeeker))
                {
                    score += 20f; // 被遮挡，加分
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestSpot = spot;
            }
        }

        return bestSpot;
    }

    /// <summary>
    /// 计算逃跑方向：远离 Seeker
    /// </summary>
    private Vector3 GetFleePosition()
    {
        Vector3 awayDir = (transform.position - seeker.position).normalized;
        Vector3 fleeTarget = transform.position + awayDir * dangerRadius * 1.5f;

        // 加入一些随机性，不要直线逃跑
        fleeTarget += new Vector3(Random.Range(-10f, 10f), 0, Random.Range(-10f, 10f));

        NavMeshHit hit;
        if (NavMesh.SamplePosition(fleeTarget, out hit, 20f, NavMesh.AllAreas))
            return hit.position;

        return FindHidingSpot();
    }

    /// <summary>
    /// 简单的视线检测
    /// </summary>
    private bool CanSeekerSeeMe(float distance)
    {
        if (distance > dangerRadius * 1.5f) return false;

        Vector3 dirToMe = (transform.position - seeker.position).normalized;
        float seekerForwardDot = Vector3.Dot(seeker.forward, dirToMe);

        // Seeker 面朝我的方向 + 没有遮挡
        if (seekerForwardDot > 0.5f)
        {
            if (!Physics.Linecast(
                seeker.position + Vector3.up,
                transform.position + Vector3.up))
            {
                return true;
            }
        }
        return false;
    }

    // =====================================================================
    // ★ 假宝物系统
    // =====================================================================

    /// <summary>
    /// 放置假宝物
    /// </summary>
    private IEnumerator PlaceDecoyCoroutine()
    {
        if (decoyPrefabs == null || decoyPrefabs.Length == 0)
        {
            Debug.LogWarning("[Hider] No decoy prefabs assigned!");
            yield break;
        }

        // 在 Seeker 可能经过的路径附近放置
        Vector3 placePos = GetDecoyPlacementPosition();

        NavMeshHit hit;
        if (NavMesh.SamplePosition(placePos, out hit, 10f, NavMesh.AllAreas))
        {
            // 走到放置点
            navAgent.SetDestination(hit.position);

            // 等待到达
            float timeout = 15f;
            while (navAgent.remainingDistance > 2f && timeout > 0)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            // 放置假宝物
            int prefabIdx = Random.Range(0, decoyPrefabs.Length);
            GameObject decoy = Instantiate(
                decoyPrefabs[prefabIdx],
                hit.position + Vector3.up * 0.5f,
                Quaternion.identity
            );

            decoy.tag = "Decoy";
            decoy.name = $"Decoy_{placedDecoys.Count}_{decoyPrefabs[prefabIdx].name}";

            placedDecoys.Add(decoy);

            Debug.Log($"<color=magenta>[Hider]</color> Placed decoy: {decoy.name} at {hit.position}");

            // 放完回去躲
            currentHideSpot = FindHidingSpot();
            navAgent.SetDestination(currentHideSpot);
        }

        currentState = HiderState.HIDING;
    }

    /// <summary>
    /// 选择假宝物放置位置：在 Seeker 可能经过的路线附近
    /// </summary>
    private Vector3 GetDecoyPlacementPosition()
    {
        if (seeker == null)
            return transform.position + Random.insideUnitSphere * decoyPlaceRadius;

        // 在 Seeker 和自己之间的区域放置，诱导 Seeker 走偏
        Vector3 midpoint = (transform.position + seeker.position) / 2f;
        Vector3 offset = new Vector3(
            Random.Range(-15f, 15f), 0, Random.Range(-15f, 15f)
        );

        return midpoint + offset;
    }

    /// <summary>
    /// 定期管理假宝物（移动位置增加迷惑性）
    /// </summary>
    private IEnumerator DecoyManagementLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(decoyMoveInterval);

            if (placedDecoys.Count == 0) continue;

            // 随机移动一个假宝物
            int idx = Random.Range(0, placedDecoys.Count);
            GameObject decoy = placedDecoys[idx];

            if (decoy == null)
            {
                placedDecoys.RemoveAt(idx);
                continue;
            }

            Vector3 newPos = GetDecoyPlacementPosition();
            NavMeshHit hit;
            if (NavMesh.SamplePosition(newPos, out hit, 15f, NavMesh.AllAreas))
            {
                decoy.transform.position = hit.position + Vector3.up * 0.5f;
                Debug.Log($"<color=magenta>[Hider]</color> Moved decoy {decoy.name} to {hit.position}");
            }
        }
    }

    /// <summary>
    /// 被 Seeker 发现时调用（外部调用）
    /// </summary>
    public void OnDiscovered()
    {
        Debug.LogWarning("<color=red>[Hider] I've been found!</color>");
        currentState = HiderState.FLEEING;
        Vector3 fleeTarget = GetFleePosition();
        navAgent.SetDestination(fleeTarget);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, dangerRadius);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, safeRadius);
    }
}
