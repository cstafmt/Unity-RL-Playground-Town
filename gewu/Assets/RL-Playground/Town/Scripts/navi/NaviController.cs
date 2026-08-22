//using UnityEngine;
//using UnityEngine.AI;

///// <summary>
///// 负责管理 NavMeshAgent 的自动导航
///// 修改：不再自动生成/获取 NavMeshAgent，而是通过 Inspector 指定，与 Go1Navigator 保持一致
///// </summary>
//public class RobotNavigationController : MonoBehaviour
//{
//    [Header("组件引用")]
//    // 允许在 Inspector 中手动拖拽，如同 Go1Navigator
//    public NavMeshAgent navAgent;
//    public G1opAgent robotPhysics; // 仅用于同步动画状态

//    [Header("导航参数")]
//    public float moveSpeed = 1.5f;
//    public float turnSpeed = 120f; // 角度/秒
//    public float stopDistance = 0.2f;

//    [Header("调试")]
//    public bool showDebugGizmos = true;

//    private bool isIntendingToMove = false;

//    void Start()
//    {
//        // 1. 像 Go1Navigator 一样进行空值检查
//        if (navAgent == null)
//        {
//            navAgent = GetComponent<NavMeshAgent>();
//            if (navAgent == null)
//            {
//                Debug.LogError("RobotNavigationController: 未指定 NavMeshAgent！请在 Inspector 中拖拽赋值。");
//                return;
//            }
//        }

//        if (robotPhysics == null)
//        {
//            robotPhysics = GetComponent<G1opAgent>();
//            // 如果没找到也不强求，只是动画会不播放
//        }

//        // 2. 启用自动导航控制
//        // 我们把控制权交给 NavMeshAgent
//        navAgent.updatePosition = true;
//        navAgent.updateRotation = true;

//        // 应用参数
//        navAgent.speed = moveSpeed;
//        navAgent.angularSpeed = turnSpeed;
//        navAgent.stoppingDistance = stopDistance;
//    }

//    void Update()
//    {
//        if (navAgent == null) return;

//        // 3. 同步动画状态 (可选)
//        // 虽然由 NavMeshAgent 负责移动，但我们需要让 G1opAgent 知道我们在动，以便播放走路动画
//        if (robotPhysics != null)
//        {
//            // 获取 NavMeshAgent 当前的实际移动速度
//            float currentVel = navAgent.velocity.magnitude;

//            // 简单映射：如果有速度，就设置 vr 让腿动起来
//            // 注意：这里的 vr 不再控制物理位移，只是告诉动画机 "该动腿了"
//            robotPhysics.vr = currentVel;
//            robotPhysics.cr = (currentVel > 0.1f) ? 0.5f : 0f;
//            robotPhysics.wr = 0f; // 旋转通常由 NavMeshAgent 自动处理，这里置0防止动画冲突
//        }
//    }

//    /// <summary>
//    /// 检查是否到达目的地
//    /// </summary>
//    public bool HasArrived()
//    {
//        if (navAgent == null) return true;

//        // 如果我们并未打算移动，视为“已就位”
//        if (!isIntendingToMove) return true;

//        // 如果正在计算路径，等待
//        if (navAgent.pathPending) return false;

//        // 检查剩余距离是否小于停止距离
//        if (navAgent.remainingDistance <= navAgent.stoppingDistance)
//        {
//            // 再次确认是否有有效路径且速度已降下来
//            if (!navAgent.hasPath || navAgent.velocity.sqrMagnitude == 0f)
//            {
//                return true;
//            }
//        }

//        return false;
//    }

//    public void MoveTo(Vector3 targetPosition)
//    {
//        if (navAgent == null) return;

//        // 确保启用
//        navAgent.isStopped = false;

//        navAgent.SetDestination(targetPosition);
//        isIntendingToMove = true;
//    }

//    public void Stop()
//    {
//        isIntendingToMove = false;
//        if (navAgent != null && navAgent.isOnNavMesh)
//        {
//            navAgent.isStopped = true;
//            navAgent.ResetPath();
//        }

//        // 停止动画
//        if (robotPhysics != null)
//        {
//            robotPhysics.vr = 0;
//            robotPhysics.wr = 0;
//        }
//    }

//    void OnDrawGizmos()
//    {
//        if (showDebugGizmos && navAgent != null && navAgent.hasPath)
//        {
//            Gizmos.color = Color.green;
//            Gizmos.DrawLine(transform.position, navAgent.destination);
//            Gizmos.DrawSphere(navAgent.destination, 0.3f);
//        }
//    }
//}

//using UnityEngine;
//using UnityEngine.AI;

//public class RobotNavigationController : MonoBehaviour
//{
//    [Header("组件引用")]
//    public NavMeshAgent navAgent;

//    // 可选：引用上一段对话生成的物理导航脚本
//    public Go1Navigator physicsNavigator;
//    public G1opAgent robotPhysics; // 仅用于简单的动画同步（如果没有 physicsNavigator）

//    private bool isIntendingToMove = false;

//    void Start()
//    {
//        if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();

//        // 尝试自动获取 Go1Navigator
//        if (physicsNavigator == null) physicsNavigator = GetComponent<Go1Navigator>();

//        // 如果有物理导航器，确保它开启
//        if (physicsNavigator != null)
//        {
//            physicsNavigator.EnableNavigation(true);
//        }
//    }

//    public void MoveTo(Vector3 targetPosition)
//    {
//        isIntendingToMove = true;

//        // 如果有高级物理导航脚本，优先使用它
//        if (physicsNavigator != null)
//        {
//            physicsNavigator.SetDestination(targetPosition);
//            physicsNavigator.EnableNavigation(true);
//        }
//        else if (navAgent != null)
//        {
//            // 回退到普通 NavMesh 导航
//            navAgent.isStopped = false;
//            navAgent.SetDestination(targetPosition);
//        }
//    }

//    public void Stop()
//    {
//        isIntendingToMove = false;

//        if (physicsNavigator != null)
//        {
//            physicsNavigator.EnableNavigation(false); // 停止物理移动
//        }

//        if (navAgent != null && navAgent.isOnNavMesh)
//        {
//            navAgent.isStopped = true;
//            navAgent.ResetPath();
//        }

//        // 简单停止动画
//        if (robotPhysics != null)
//        {
//            robotPhysics.vr = 0;
//            robotPhysics.wr = 0;
//        }
//    }

//    public bool HasArrived()
//    {
//        if (!isIntendingToMove) return true;

//        // 如果使用了 Go1Navigator，可以检查它的状态（如果有公开方法的话）
//        // 这里主要依赖 NavMeshAgent 的状态判断，因为 Go1Navigator 也是基于 NavMeshAgent 的

//        if (navAgent == null) return true;
//        if (navAgent.pathPending) return false;

//        if (navAgent.remainingDistance <= navAgent.stoppingDistance)
//        {
//            if (!navAgent.hasPath || navAgent.velocity.sqrMagnitude < 0.01f)
//            {
//                return true;
//            }
//        }
//        return false;
//    }
//}
using UnityEngine;
using UnityEngine.AI;

public class RobotNavigationController : MonoBehaviour
{
    [Header("执行层引用")]
    public Go1Navigator go1Navigator; // 优先使用这个物理导航
    public NavMeshAgent fallbackAgent; // 仅作为备用或检查

    void Start()
    {
        // 自动查找
        if (go1Navigator == null) go1Navigator = GetComponent<Go1Navigator>();
        if (fallbackAgent == null) fallbackAgent = GetComponent<NavMeshAgent>();

        if (go1Navigator == null)
        {
            Debug.LogError("RobotNavigationController: 致命错误！找不到 Go1Navigator 脚本。请挂载该脚本。");
        }
    }

    public void MoveTo(Vector3 targetPosition)
    {
        // 优先使用 Go1 物理导航
        if (go1Navigator != null)
        {
            go1Navigator.SetDestination(targetPosition);
        }
        // 降级处理：直接操作 NavMeshAgent (通常不建议，因为没有物理避障)
        else if (fallbackAgent != null)
        {
            fallbackAgent.SetDestination(targetPosition);
            fallbackAgent.isStopped = false;
        }
    }

    public void Stop()
    {
        if (go1Navigator != null)
        {
            go1Navigator.Stop();
        }
        else if (fallbackAgent != null)
        {
            fallbackAgent.isStopped = true;
        }
    }

    public bool HasArrived()
    {
        if (go1Navigator != null)
        {
            return go1Navigator.HasArrived();
        }

        if (fallbackAgent != null)
        {
            if (fallbackAgent.pathPending) return false;
            if (fallbackAgent.remainingDistance <= fallbackAgent.stoppingDistance)
                return true;
        }

        return true; // 默认视为到达
    }
}
