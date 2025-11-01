using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;

public class Go1Navigator : MonoBehaviour
{
    public G1opAgent agent;
    public NavMeshAgent navAgent;
    public Transform robotBody;

    [Header("导航参数")]
    public float maxForwardSpeed = 1.2f;
    public float maxRotationSpeed = 1f;
    public float arrivalDistance = 0.3f;
    public bool showDebugInfo = true;

    [Header("导航控制")]
    public bool useNavigation = true;
    private Vector3 currentDestination;

    void Start()
    {
        // 检查必要组件
        if (agent == null)
        {
            agent = FindObjectOfType<G1opAgent>();
            if (agent == null)
                Debug.LogError("Go1Navigator: G1opAgent 未找到！");
        }

        if (navAgent == null)
        {
            navAgent = GetComponent<NavMeshAgent>();
            if (navAgent == null)
            {
                Debug.LogError("Go1Navigator: NavMeshAgent 未找到！");
            }
        }

        if (robotBody == null)
        {
            robotBody = transform;
            Debug.LogWarning("Go1Navigator: robotBody 未设置，使用自身Transform");
        }

        // 配置 NavMeshAgent
        if (navAgent != null)
        {
            navAgent.speed = maxForwardSpeed;
            navAgent.angularSpeed = maxRotationSpeed * 100f;
            navAgent.stoppingDistance = arrivalDistance;
            navAgent.autoBraking = true;
        }
    }

    void Update()
    {
        if (useNavigation)
        {
            HandleMouseInput();
            HandleNavigation();
        }
        else
        {
            // 非导航模式下手动控制
            HandleManualControl();
        }

        // 切换导航模式
        if (Input.GetKeyDown(KeyCode.N))
        {
            useNavigation = !useNavigation;
            if (showDebugInfo)
                Debug.Log($"导航模式: {(useNavigation ? "开启" : "关闭")}");
        }
    }

    void HandleManualControl()
    {
        // 在非导航模式下，允许手动控制
        // 这里可以添加手动控制的逻辑，或者保持为空让用户通过其他方式控制
    }

    void HandleMouseInput()
    {
        // 检查鼠标点击，更新目标点和路径
        if (Input.GetMouseButtonDown(0))
        {
            // 检查是否点击在UI上
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                if (showDebugInfo) Debug.Log("点击在UI上，忽略导航");
                return;
            }

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, Mathf.Infinity))
            {
                // 检查点击点是否在NavMesh上
                NavMeshHit navHit;
                float maxDistance = 5.0f; // 增加检测距离

                if (NavMesh.SamplePosition(hit.point, out navHit, maxDistance, NavMesh.AllAreas))
                {
                    SetDestination(navHit.position);
                }
                else
                {
                    if (showDebugInfo)
                        Debug.LogWarning($"点击位置 {hit.point} 不在可导航区域！");
                }
            }
            else
            {
                if (showDebugInfo)
                    Debug.LogWarning("射线未击中任何物体！");
            }
        }
    }

    public void SetDestination(Vector3 destination)
    {
        if (navAgent != null)
        {
            navAgent.ResetPath();
            navAgent.SetDestination(destination);
            currentDestination = destination;

            if (showDebugInfo)
                Debug.Log($"新目标点设置: {destination}");
        }
    }

    void HandleNavigation()
    {
        // 检查必要组件
        if (agent == null || navAgent == null || robotBody == null)
        {
            if (showDebugInfo)
                Debug.LogError("Go1Navigator: 必要组件未设置！");
            return;
        }

        // 路径检查
        if (!navAgent.hasPath || navAgent.pathStatus != NavMeshPathStatus.PathComplete)
        {
            // 没有路径或路径无效时停止
            agent.vr = 0f;
            agent.wr = 0f;
            agent.cr = 0.2f; // 默认步长
            return;
        }

        Vector3 robotPos = robotBody.position;
        Vector3 targetPoint = navAgent.destination;

        // 到达检测
        float distToTarget = Vector3.Distance(robotPos, targetPoint);
        if (distToTarget < arrivalDistance)
        {
            agent.vr = 0f;
            agent.wr = 0f;
            if (showDebugInfo)
                Debug.Log("到达目标点！");
            return;
        }

        // 获取当前路径点（使用下一个拐点作为临时目标）
        Vector3 currentTarget = GetNextPathCorner(robotPos);

        // 计算导航控制参数
        Vector3 toTarget = (currentTarget - robotPos).normalized;
        Vector3 robotForward = robotBody.forward;

        // 计算机器人朝向与目标方向的夹角
        float angle = Vector3.SignedAngle(robotForward, toTarget, Vector3.up);

        // 水平距离计算
        Vector2 robotXZ = new Vector2(robotPos.x, robotPos.z);
        Vector2 targetXZ = new Vector2(currentTarget.x, currentTarget.z);
        float horizontalDist = Vector2.Distance(robotXZ, targetXZ);

        // 智能速度控制
        float angleThreshold = 30f;
        float distanceThreshold = 2f;

        // 根据角度偏差调整速度
        if (Mathf.Abs(angle) > angleThreshold)
        {
            // 角度偏差大时，主要进行旋转，少量前进
            agent.wr = Mathf.Clamp(angle * 0.02f, -maxRotationSpeed, maxRotationSpeed);
            agent.vr = Mathf.Clamp(horizontalDist * 0.1f, 0, maxForwardSpeed * 0.2f);
        }
        else
        {
            // 角度偏差小时，主要前进，微调旋转
            agent.wr = Mathf.Clamp(angle * 0.01f, -maxRotationSpeed * 0.5f, maxRotationSpeed * 0.5f);
            agent.vr = Mathf.Clamp(horizontalDist * 0.5f, 0, maxForwardSpeed);
        }

        // 根据距离调整步长
        if (horizontalDist < distanceThreshold)
        {
            // 接近目标时使用较小步长
            agent.cr = Mathf.Lerp(0.1f, 0.3f, horizontalDist / distanceThreshold);
        }
        else
        {
            // 远距离时使用正常步长
            agent.cr = 0.5f;
        }

        // 接近目标时减速
        if (horizontalDist < 1f)
        {
            agent.vr *= 0.7f;
        }

        // 防抖动
        if (Mathf.Abs(agent.vr) < 0.05f) agent.vr = 0f;
        if (Mathf.Abs(agent.wr) < 0.05f) agent.wr = 0f;

        if (showDebugInfo && Time.frameCount % 30 == 0) // 每30帧输出一次调试信息
        {
            Debug.Log($"目标: {currentTarget}, 距离: {horizontalDist:F2}, 角度差: {angle:F1}°");
            Debug.Log($"控制指令 - vr: {agent.vr:F2}, wr: {agent.wr:F2}, cr: {agent.cr:F2}");
        }
    }

    Vector3 GetNextPathCorner(Vector3 robotPos)
    {
        if (navAgent.path == null || navAgent.path.corners.Length == 0)
            return navAgent.destination;

        // 寻找下一个未到达的路径拐点
        for (int i = 0; i < navAgent.path.corners.Length; i++)
        {
            float distToCorner = Vector3.Distance(robotPos, navAgent.path.corners[i]);
            if (distToCorner > 0.5f) // 寻找距离超过0.5m的拐点
            {
                return navAgent.path.corners[i];
            }
        }

        // 如果所有拐点都很近，返回最终目标
        return navAgent.destination;
    }

    // 在Scene视图中绘制调试信息
    void OnDrawGizmosSelected()
    {
        if (!showDebugInfo) return;

        // 绘制当前目标点
        if (navAgent != null && navAgent.hasPath)
        {
            // 绘制路径
            Gizmos.color = Color.blue;
            for (int i = 0; i < navAgent.path.corners.Length - 1; i++)
            {
                Gizmos.DrawLine(navAgent.path.corners[i], navAgent.path.corners[i + 1]);
                Gizmos.DrawSphere(navAgent.path.corners[i], 0.1f);
            }

            // 绘制目标点
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(navAgent.destination, 0.2f);

            // 绘制下一个路径点
            Vector3 nextCorner = GetNextPathCorner(transform.position);
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(nextCorner, 0.15f);
        }

        // 绘制机器人朝向
        if (robotBody != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawRay(robotBody.position, robotBody.forward * 2f);
        }
    }

    // 公共方法，用于外部控制
    public void EnableNavigation(bool enable)
    {
        useNavigation = enable;
        if (!enable)
        {
            // 禁用导航时停止机器人
            if (agent != null)
            {
                agent.vr = 0f;
                agent.wr = 0f;
            }
            if (navAgent != null)
            {
                navAgent.ResetPath();
            }
        }
    }

    public bool IsNavigationActive()
    {
        return useNavigation && navAgent != null && navAgent.hasPath;
    }

    public Vector3 GetCurrentDestination()
    {
        return currentDestination;
    }
}
