//using UnityEngine;
//using System.Collections;
//using System.Collections.Generic;

//public class RouteScheduler : MonoBehaviour
//{
//    [Header("依赖项")]
//    public RobotNavigationController navController;

//    [Header("设置")]
//    public float waitTimeAtStop = 3.0f;

//    private Queue<Vector3> waypointsQueue = new Queue<Vector3>();
//    private bool isRunning = false;
//    private bool isWaiting = false;

//    void Update()
//    {
//        if (!isRunning || isWaiting) return;

//        // 检查 RobotNavigationController 是否报告已到达
//        if (navController.HasArrived())
//        {
//            StartCoroutine(WaitAndNext());
//        }
//    }

//    public void StartNewRoute(List<Vector3> routePoints)
//    {
//        waypointsQueue.Clear();
//        StopAllCoroutines();
//        isWaiting = false;

//        // 重置状态
//        navController.Stop();

//        foreach (var p in routePoints)
//        {
//            waypointsQueue.Enqueue(p);
//        }

//        Debug.Log($"[Scheduler] 收到新路线，包含 {waypointsQueue.Count} 个站点");

//        if (waypointsQueue.Count > 0)
//        {
//            isRunning = true;
//            MoveToNext();
//        }
//    }

//    private void MoveToNext()
//    {
//        if (waypointsQueue.Count > 0)
//        {
//            Vector3 nextTarget = waypointsQueue.Dequeue();
//            Debug.Log($"[Scheduler] 前往下一站: {nextTarget}");
//            navController.MoveTo(nextTarget);
//        }
//        else
//        {
//            Debug.Log("[Scheduler] 所有站点已完成。");
//            isRunning = false;
//            navController.Stop();
//        }
//    }

//    private IEnumerator WaitAndNext()
//    {
//        isWaiting = true;
//        Debug.Log($"[Scheduler] 到达站点，休息 {waitTimeAtStop} 秒...");

//        // 强制让机器人停下（包括停止动画）
//        navController.Stop();

//        yield return new WaitForSeconds(waitTimeAtStop);

//        isWaiting = false;

//        // 继续执行
//        if (isRunning)
//        {
//            MoveToNext();
//        }
//    }
//}
using UnityEngine;
using System;

public class RouteScheduler : MonoBehaviour
{
    [Header("依赖项")]
    public RobotNavigationController navController;

    // 定义到达事件
    public event Action OnDestinationReached;

    // --- 修改点：将 private 字段改为拥有 public getter 的属性 ---
    private bool _isMoving = false;
    public bool IsMoving => _isMoving; // 这样外部就能读取 scheduler.IsMoving 了

    void Update()
    {
        if (_isMoving)
        {
            // 检查 RobotNavigationController 是否报告已到达
            if (navController.HasArrived())
            {
                _isMoving = false;
                Debug.Log("[Scheduler] 确认到达目的地，触发回调。");
                OnDestinationReached?.Invoke();
            }
        }
    }

    public void MoveToLocation(Vector3 targetPos)
    {
        // 重置状态
        if (navController != null) navController.Stop();

        Debug.Log($"[Scheduler] 收到新指令，前往坐标: {targetPos}");

        if (navController != null)
        {
            navController.MoveTo(targetPos);
            _isMoving = true;
        }
        else
        {
            Debug.LogError("RouteScheduler: NavigationController 未绑定！");
        }
    }

    public void StopImmediate()
    {
        _isMoving = false;
        if (navController != null) navController.Stop();
    }
}
