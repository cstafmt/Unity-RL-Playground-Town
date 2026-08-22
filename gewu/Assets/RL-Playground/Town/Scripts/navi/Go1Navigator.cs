//using UnityEngine;
//using UnityEngine.AI;
//using UnityEngine.EventSystems;

//public class Go1Navigator : MonoBehaviour
//{
//    public G1opAgent agent;
//    public NavMeshAgent navAgent;
//    public Transform robotBody;

//    [Header("导航参数")]
//    public float maxForwardSpeed = 1.2f;
//    public float maxRotationSpeed = 1f;
//    public float arrivalDistance = 0.3f;
//    public bool showDebugInfo = true;

//    [Header("导航控制")]
//    public bool useNavigation = true;
//    private Vector3 currentDestination;

//    void Start()
//    {
//        // 检查必要组件
//        if (agent == null)
//        {
//            agent = FindObjectOfType<G1opAgent>();
//            if (agent == null)
//                Debug.LogError("Go1Navigator: G1opAgent 未找到！");
//        }

//        if (navAgent == null)
//        {
//            navAgent = GetComponent<NavMeshAgent>();
//            if (navAgent == null)
//            {
//                Debug.LogError("Go1Navigator: NavMeshAgent 未找到！");
//            }
//        }

//        if (robotBody == null)
//        {
//            robotBody = transform;
//            Debug.LogWarning("Go1Navigator: robotBody 未设置，使用自身Transform");
//        }

//        // 配置 NavMeshAgent
//        if (navAgent != null)
//        {
//            navAgent.speed = maxForwardSpeed;
//            navAgent.angularSpeed = maxRotationSpeed * 100f;
//            navAgent.stoppingDistance = arrivalDistance;
//            navAgent.autoBraking = true;
//        }
//    }

//    void Update()
//    {
//        if (useNavigation)
//        {
//            HandleMouseInput();
//            HandleNavigation();
//        }
//        else
//        {
//            // 非导航模式下手动控制
//            HandleManualControl();
//        }

//        // 切换导航模式
//        if (Input.GetKeyDown(KeyCode.N))
//        {
//            useNavigation = !useNavigation;
//            if (showDebugInfo)
//                Debug.Log($"导航模式: {(useNavigation ? "开启" : "关闭")}");
//        }
//    }

//    void HandleManualControl()
//    {
//        // 在非导航模式下，允许手动控制
//        // 这里可以添加手动控制的逻辑，或者保持为空让用户通过其他方式控制
//    }

//    void HandleMouseInput()
//    {
//        // 检查鼠标点击，更新目标点和路径
//        if (Input.GetMouseButtonDown(0))
//        {
//            // 检查是否点击在UI上
//            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
//            {
//                if (showDebugInfo) Debug.Log("点击在UI上，忽略导航");
//                return;
//            }

//            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
//            RaycastHit hit;

//            if (Physics.Raycast(ray, out hit, Mathf.Infinity))
//            {
//                // 检查点击点是否在NavMesh上
//                NavMeshHit navHit;
//                float maxDistance = 5.0f; // 增加检测距离

//                if (NavMesh.SamplePosition(hit.point, out navHit, maxDistance, NavMesh.AllAreas))
//                {
//                    SetDestination(navHit.position);
//                }
//                else
//                {
//                    if (showDebugInfo)
//                        Debug.LogWarning($"点击位置 {hit.point} 不在可导航区域！");
//                }
//            }
//            else
//            {
//                if (showDebugInfo)
//                    Debug.LogWarning("射线未击中任何物体！");
//            }
//        }
//    }

//    public void SetDestination(Vector3 destination)
//    {
//        if (navAgent != null)
//        {
//            navAgent.ResetPath();
//            navAgent.SetDestination(destination);
//            currentDestination = destination;

//            if (showDebugInfo)
//                Debug.Log($"新目标点设置: {destination}");
//        }
//    }

//    void HandleNavigation()
//    {
//        // 检查必要组件
//        if (agent == null || navAgent == null || robotBody == null)
//        {
//            if (showDebugInfo)
//                Debug.LogError("Go1Navigator: 必要组件未设置！");
//            return;
//        }

//        // 路径检查
//        if (!navAgent.hasPath || navAgent.pathStatus != NavMeshPathStatus.PathComplete)
//        {
//            // 没有路径或路径无效时停止
//            agent.vr = 0f;
//            agent.wr = 0f;
//            agent.cr = 0.2f; // 默认步长
//            return;
//        }

//        Vector3 robotPos = robotBody.position;
//        Vector3 targetPoint = navAgent.destination;

//        // 到达检测
//        float distToTarget = Vector3.Distance(robotPos, targetPoint);
//        if (distToTarget < arrivalDistance)
//        {
//            agent.vr = 0f;
//            agent.wr = 0f;
//            if (showDebugInfo)
//                Debug.Log("到达目标点！");
//            return;
//        }

//        // 获取当前路径点（使用下一个拐点作为临时目标）
//        Vector3 currentTarget = GetNextPathCorner(robotPos);

//        // 计算导航控制参数
//        Vector3 toTarget = (currentTarget - robotPos).normalized;
//        Vector3 robotForward = robotBody.forward;

//        // 计算机器人朝向与目标方向的夹角
//        float angle = Vector3.SignedAngle(robotForward, toTarget, Vector3.up);

//        // 水平距离计算
//        Vector2 robotXZ = new Vector2(robotPos.x, robotPos.z);
//        Vector2 targetXZ = new Vector2(currentTarget.x, currentTarget.z);
//        float horizontalDist = Vector2.Distance(robotXZ, targetXZ);

//        // 智能速度控制
//        float angleThreshold = 30f;
//        float distanceThreshold = 2f;

//        // 根据角度偏差调整速度
//        if (Mathf.Abs(angle) > angleThreshold)
//        {
//            // 角度偏差大时，主要进行旋转，少量前进
//            agent.wr = Mathf.Clamp(angle * 0.02f, -maxRotationSpeed, maxRotationSpeed);
//            agent.vr = Mathf.Clamp(horizontalDist * 0.1f, 0, maxForwardSpeed * 0.2f);
//        }
//        else
//        {
//            // 角度偏差小时，主要前进，微调旋转
//            agent.wr = Mathf.Clamp(angle * 0.01f, -maxRotationSpeed * 0.5f, maxRotationSpeed * 0.5f);
//            agent.vr = Mathf.Clamp(horizontalDist * 0.5f, 0, maxForwardSpeed);
//        }

//        // 根据距离调整步长
//        if (horizontalDist < distanceThreshold)
//        {
//            // 接近目标时使用较小步长
//            agent.cr = Mathf.Lerp(0.1f, 0.3f, horizontalDist / distanceThreshold);
//        }
//        else
//        {
//            // 远距离时使用正常步长
//            agent.cr = 0.5f;
//        }

//        // 接近目标时减速
//        if (horizontalDist < 1f)
//        {
//            agent.vr *= 0.7f;
//        }

//        // 防抖动
//        if (Mathf.Abs(agent.vr) < 0.05f) agent.vr = 0f;
//        if (Mathf.Abs(agent.wr) < 0.05f) agent.wr = 0f;

//        if (showDebugInfo && Time.frameCount % 30 == 0) // 每30帧输出一次调试信息
//        {
//            Debug.Log($"目标: {currentTarget}, 距离: {horizontalDist:F2}, 角度差: {angle:F1}°");
//            Debug.Log($"控制指令 - vr: {agent.vr:F2}, wr: {agent.wr:F2}, cr: {agent.cr:F2}");
//        }
//    }

//    Vector3 GetNextPathCorner(Vector3 robotPos)
//    {
//        if (navAgent.path == null || navAgent.path.corners.Length == 0)
//            return navAgent.destination;

//        // 寻找下一个未到达的路径拐点
//        for (int i = 0; i < navAgent.path.corners.Length; i++)
//        {
//            float distToCorner = Vector3.Distance(robotPos, navAgent.path.corners[i]);
//            if (distToCorner > 0.5f) // 寻找距离超过0.5m的拐点
//            {
//                return navAgent.path.corners[i];
//            }
//        }

//        // 如果所有拐点都很近，返回最终目标
//        return navAgent.destination;
//    }

//    // 在Scene视图中绘制调试信息
//    void OnDrawGizmosSelected()
//    {
//        if (!showDebugInfo) return;

//        // 绘制当前目标点
//        if (navAgent != null && navAgent.hasPath)
//        {
//            // 绘制路径
//            Gizmos.color = Color.blue;
//            for (int i = 0; i < navAgent.path.corners.Length - 1; i++)
//            {
//                Gizmos.DrawLine(navAgent.path.corners[i], navAgent.path.corners[i + 1]);
//                Gizmos.DrawSphere(navAgent.path.corners[i], 0.1f);
//            }

//            // 绘制目标点
//            Gizmos.color = Color.red;
//            Gizmos.DrawSphere(navAgent.destination, 0.2f);

//            // 绘制下一个路径点
//            Vector3 nextCorner = GetNextPathCorner(transform.position);
//            Gizmos.color = Color.yellow;
//            Gizmos.DrawSphere(nextCorner, 0.15f);
//        }

//        // 绘制机器人朝向
//        if (robotBody != null)
//        {
//            Gizmos.color = Color.green;
//            Gizmos.DrawRay(robotBody.position, robotBody.forward * 2f);
//        }
//    }

//    // 公共方法，用于外部控制
//    public void EnableNavigation(bool enable)
//    {
//        useNavigation = enable;
//        if (!enable)
//        {
//            // 禁用导航时停止机器人
//            if (agent != null)
//            {
//                agent.vr = 0f;
//                agent.wr = 0f;
//            }
//            if (navAgent != null)
//            {
//                navAgent.ResetPath();
//            }
//        }
//    }

//    public bool IsNavigationActive()
//    {
//        return useNavigation && navAgent != null && navAgent.hasPath;
//    }

//    public Vector3 GetCurrentDestination()
//    {
//        return currentDestination;
//    }
//}
//using UnityEngine;
//using UnityEngine.AI;
//using UnityEngine.EventSystems;

//public class Go1Navigator : MonoBehaviour
//{
//    [Header("核心组件")]
//    public G1opAgent agent;       // 物理驱动脚本
//    public NavMeshAgent navAgent; // 路径计算核心
//    public Transform robotBody;   // 机器人真身

//    [Header("调试控制")]
//    public bool enableMouseDebug = false; // 是否允许鼠标点击控制(调试用)
//    public bool showDebugInfo = true;

//    [Header("运动参数")]
//    public float maxForwardSpeed = 0.8f; // 限制最大速度，太快容易翻
//    public float maxRotationSpeed = 1.0f;
//    public float stopDistance = 0.3f;

//    private bool isNavigating = false;

//    void Start()
//    {
//        // 1. 自动获取组件
//        if (agent == null) agent = FindObjectOfType<G1opAgent>();
//        if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();
//        if (robotBody == null) robotBody = transform;

//        // 2. 关键配置：让 NavMeshAgent 变为纯计算组件，不控制 Transform
//        if (navAgent != null)
//        {
//            navAgent.updatePosition = false; // 禁止自动移动
//            navAgent.updateRotation = false; // 禁止自动旋转
//            navAgent.stoppingDistance = stopDistance;
//        }
//    }

//    void Update()
//    {
//        // 0. 鼠标调试逻辑 (仅在开启时生效)
//        if (enableMouseDebug)
//        {
//            HandleMouseInput();
//        }

//        // 1. 安全检查
//        if (agent == null || navAgent == null) return;

//        // 2. 【核心】位置同步
//        // 告诉 NavMeshAgent 机器人现在实际在哪里，否则它会基于上次的位置瞎算
//        if (robotBody != null)
//        {
//            navAgent.nextPosition = robotBody.position;
//        }

//        // 3. 导航逻辑执行
//        if (isNavigating)
//        {
//            FollowPath();
//        }
//    }

//    /// <summary>
//    /// 供外部 (RobotNavigationController) 调用设置目标
//    /// </summary>
//    public void SetDestination(Vector3 targetPos)
//    {
//        if (navAgent == null) return;

//        navAgent.ResetPath();
//        navAgent.SetDestination(targetPos);
//        navAgent.isStopped = false;
//        isNavigating = true;

//        if (showDebugInfo) Debug.Log($"[Go1Navigator] 收到新目标: {targetPos}");
//    }

//    /// <summary>
//    /// 供外部调用停止
//    /// </summary>
//    public void Stop()
//    {
//        isNavigating = false;
//        if (navAgent != null && navAgent.isOnNavMesh)
//        {
//            navAgent.isStopped = true;
//            navAgent.ResetPath();
//        }
//        // 立即刹车
//        if (agent != null)
//        {
//            agent.vr = 0f;
//            agent.wr = 0f;
//        }
//    }

//    /// <summary>
//    /// 检查是否到达 (供外部查询)
//    /// </summary>
//    public bool HasArrived()
//    {
//        if (!isNavigating) return true;
//        if (navAgent.pathPending) return false;

//        if (navAgent.remainingDistance <= stopDistance)
//        {
//            return true;
//        }
//        return false;
//    }

//    void HandleMouseInput()
//    {
//        if (Input.GetMouseButtonDown(0))
//        {
//            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

//            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
//            if (Physics.Raycast(ray, out RaycastHit hit))
//            {
//                SetDestination(hit.point);
//            }
//        }
//    }

//    void FollowPath()
//    {
//        // 如果路径无效或计算中，等待
//        if (navAgent.pathPending || navAgent.path.corners.Length < 2)
//        {
//            // 保持原地不动
//            agent.vr = 0f;
//            agent.wr = 0f;
//            agent.cr = 0.5f; // 保持站立姿态
//            return;
//        }

//        // --- 仿照 Go2 的拐点逻辑 ---
//        Vector3 robotPos = robotBody.position;
//        int cornerIndex = 1; // 0 是当前位置，1 是下一个拐点
//        Vector3 targetPoint = navAgent.path.corners[cornerIndex];

//        // 距离检测：如果离目标很近，就停止
//        if (navAgent.remainingDistance < stopDistance)
//        {
//            isNavigating = false;
//            agent.vr = 0f;
//            agent.wr = 0f;
//            if (showDebugInfo) Debug.Log("到达目的地 (Logic Stop)");
//            return;
//        }

//        // --- 运动控制计算 ---
//        Vector3 toTarget = (targetPoint - robotPos).normalized;
//        Vector3 robotForward = robotBody.forward;

//        // 计算角度差 (带符号)
//        float angle = Vector3.Angle(robotForward, toTarget);
//        float sign = Mathf.Sign(Vector3.Cross(robotForward, toTarget).y);
//        float signedAngle = angle * sign;

//        // PID 参数
//        float kp_rotate = 0.03f;
//        float kp_forward = 0.5f;

//        // 旋转控制 (wr)
//        // Go1 的 wr 范围通常在 -1 到 1 之间
//        float targetWr = signedAngle * kp_rotate;
//        agent.wr = Mathf.Clamp(targetWr, -1.0f, 1.0f);

//        // 前进控制 (vr)
//        // 如果角度偏差太大(>20度)，先原地转，不要跑太快
//        float forwardMultiplier = 1.0f;
//        if (Mathf.Abs(signedAngle) > 20f)
//        {
//            forwardMultiplier = 0.1f; // 减速转向
//        }

//        // 根据距离和倍率计算速度
//        float distToCorner = Vector3.Distance(robotPos, targetPoint);
//        float targetVr = distToCorner * kp_forward * forwardMultiplier;
//        agent.vr = Mathf.Clamp(targetVr, 0f, maxForwardSpeed);

//        // 步态参数 (Go1 需要 cr > 0 才能动)
//        agent.cr = 0.5f;

//        // Debug
//        if (showDebugInfo && Time.frameCount % 60 == 0)
//        {
//            Debug.Log($"Navigating... Dist: {navAgent.remainingDistance:F2}, Angle: {signedAngle:F1}, vr: {agent.vr:F2}");
//        }
//    }

//    void OnDrawGizmos()
//    {
//        if (showDebugInfo && navAgent != null && navAgent.hasPath)
//        {
//            Gizmos.color = Color.green;
//            for (int i = 0; i < navAgent.path.corners.Length - 1; i++)
//            {
//                Gizmos.DrawLine(navAgent.path.corners[i], navAgent.path.corners[i + 1]);
//            }
//            Gizmos.DrawSphere(navAgent.destination, 0.2f);
//        }
//    }
//}
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;

public class Go1Navigator : MonoBehaviour
{
    [Header("核心组件")]
    public G1moeAgent agent;
    public NavMeshAgent navAgent;
    public Transform robotBody;

    [Header("调试控制")]
    public bool enableMouseDebug = false;
    public bool showDebugInfo = true;

    [Header("运动参数")]
    public float maxForwardSpeed = 1.0f;
    public float maxRotationSpeed = 1.2f;
    public float stopDistance = 0.5f;
    public float slowDownDistance = 1.5f;

    // 平滑参数
    private float currentVr = 0f;
    private float currentWr = 0f;
    private float vrVelocity = 0f;
    private float wrVelocity = 0f;
    public float speedSmoothTime = 0.3f;

    private bool isNavigating = false;

    void Start()
    {
        if (agent == null) agent = FindObjectOfType<G1moeAgent>();
        if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();

        // 确保获取的是 ArticulationBody 的根节点
        if (robotBody == null && agent != null) robotBody = agent.transform;

        if (navAgent != null)
        {
            navAgent.updatePosition = false;
            navAgent.updateRotation = false;
            navAgent.stoppingDistance = stopDistance;
        }

        if (agent != null)
        {
            agent.keyboard = false;
            agent.train = false;
        }
    }

    void Update()
    {
        // 0. 物理健康检查 (防止 Invalid AABB)
        if (!IsPhysicsValid()) return;

        // 1. 鼠标调试
        if (enableMouseDebug) HandleMouseInput();

        if (agent == null || navAgent == null || robotBody == null) return;

        // 2. 【核心】位置同步 (加入保护)
        // 只有当物理位置合法时，才同步给 NavMeshAgent
        Vector3 currentPos = robotBody.position;
        if (IsValidVector(currentPos))
        {
            // 限制同步频率或距离，避免每帧微小抖动
            if (Vector3.Distance(navAgent.nextPosition, currentPos) > 0.01f)
            {
                navAgent.nextPosition = currentPos;
            }
        }

        // 3. 导航逻辑
        if (isNavigating)
        {
            FollowPath();
        }
    }

    /// <summary>
    /// 检查物理状态是否健康，如果爆炸了则重置
    /// </summary>
    bool IsPhysicsValid()
    {
        if (robotBody == null) return false;

        Vector3 pos = robotBody.position;

        // 检查 NaN 或 Infinity
        if (!IsValidVector(pos))
        {
            Debug.LogError("检测到物理系统崩溃 (NaN/Infinity)！正在重置 Agent...");
            ResetAgentPhysics();
            return false;
        }

        // 检查是否掉出地图 (例如 Y < -50)
        if (pos.y < -50f || pos.y > 500f)
        {
            Debug.LogWarning("机器人掉出世界，重置中...");
            ResetAgentPhysics();
            return false;
        }

        return true;
    }

    bool IsValidVector(Vector3 v)
    {
        return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
               !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }

    void ResetAgentPhysics()
    {
        // 调用 Agent 的重置逻辑
        if (agent != null)
        {
            agent.EndEpisode(); // 这会触发 Agent 的 OnEpisodeBegin 重置位置
        }

        // 停止导航
        Stop();
    }

    public void SetDestination(Vector3 targetPos)
    {
        if (navAgent == null || !IsValidVector(targetPos)) return;
        if (robotBody == null) return;

        // 安全 Warp
        if (IsValidVector(robotBody.position))
        {
            navAgent.Warp(robotBody.position);
        }

        navAgent.SetDestination(targetPos);
        navAgent.isStopped = false;
        isNavigating = true;

        if (agent != null) agent.moe = 2;

        if (showDebugInfo) Debug.Log($"[Go1Navigator] 收到新目标: {targetPos}");
    }

    public void Stop()
    {
        isNavigating = false;
        if (navAgent != null && navAgent.isOnNavMesh)
        {
            navAgent.isStopped = true;
            navAgent.ResetPath();
        }

        if (agent != null)
        {
            agent.vr = 0f;
            agent.wr = 0f;
            agent.vd = 0f;
            currentVr = 0f;
            currentWr = 0f;
            // 清除平滑速度缓存
            vrVelocity = 0f;
            wrVelocity = 0f;
        }
    }

    public bool HasArrived()
    {
        if (!isNavigating) return true;
        if (navAgent == null || !navAgent.isOnNavMesh) return true; // 避免未初始化报错
        if (navAgent.pathPending) return false;

        if (navAgent.remainingDistance <= stopDistance)
        {
            return true;
        }
        return false;
    }

    void HandleMouseInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                SetDestination(hit.point);
            }
        }
    }

    void FollowPath()
    {
        if (navAgent.pathPending || navAgent.pathStatus == NavMeshPathStatus.PathInvalid || !navAgent.isOnNavMesh)
        {
            ApplyMovement(0, 0);
            return;
        }

        Vector3 robotPos = robotBody.position;
        Vector3 nextSteeringTarget = navAgent.steeringTarget;

        float dist = navAgent.remainingDistance;
        if (dist < stopDistance)
        {
            Stop();
            return;
        }

        Vector3 toTarget = (nextSteeringTarget - robotPos);
        toTarget.y = 0;
        toTarget.Normalize();

        Vector3 robotForward = robotBody.forward;
        robotForward.y = 0;
        robotForward.Normalize();

        float angle = Vector3.SignedAngle(robotForward, toTarget, Vector3.up);

        float targetVr = 0f;
        float targetWr = 0f;

        // 转向控制
        targetWr = Mathf.Clamp(angle * 0.05f, -maxRotationSpeed, maxRotationSpeed);

        // 前进控制
        if (Mathf.Abs(angle) > 30f)
        {
            targetVr = 0f;
        }
        else
        {
            float speedFactor = Mathf.Clamp01(dist / slowDownDistance);
            targetVr = maxForwardSpeed * speedFactor;
            if (dist > stopDistance) targetVr = Mathf.Max(targetVr, 0.2f);
        }

        ApplyMovement(targetVr, targetWr);
    }

    void ApplyMovement(float targetVr, float targetWr)
    {
        if (agent == null) return;

        // 【防 NaN 保护】如果计算出的速度不合法，强制归零
        if (float.IsNaN(targetVr) || float.IsInfinity(targetVr)) targetVr = 0f;
        if (float.IsNaN(targetWr) || float.IsInfinity(targetWr)) targetWr = 0f;

        agent.moe = 2;
        agent.vd = 0;

        currentVr = Mathf.SmoothDamp(currentVr, targetVr, ref vrVelocity, speedSmoothTime);
        currentWr = Mathf.SmoothDamp(currentWr, targetWr, ref wrVelocity, speedSmoothTime);

        // 二次保护
        if (float.IsNaN(currentVr)) currentVr = 0f;
        if (float.IsNaN(currentWr)) currentWr = 0f;

        agent.vr = currentVr;
        agent.wr = currentWr;
    }

    void OnDrawGizmos()
    {
        if (showDebugInfo && navAgent != null && navAgent.hasPath)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, navAgent.steeringTarget);
            Gizmos.DrawSphere(navAgent.steeringTarget, 0.3f);
        }
    }
}
