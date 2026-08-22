using UnityEngine;
using UnityEngine.UI; // 引入 UI 命名空间
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using TMPro;
public class RobotAgent_RAG : MonoBehaviour
{
    [Header("Dependencies")]
    public TownMap townMap;
    public LLMClient llmClient;
    public RouteScheduler scheduler;

    [Header("UI Display")]
    [Tooltip("拖入场景中用于显示数字人思考/状态的 UI Text")]
    public TMP_Text thoughtDisplayUI;

    [Header("RAG Memory")]
    public MemoryClient memoryClient;

    [Header("Persona")]
    public string robotName = "G1-Explorer";
    [TextArea]
    public string personaDescription = "你是一个好奇的机器人，喜欢观察人类活动，由于电量有限，你有时会想休息，有时会想探索新的地方。";

    [Header("Memory Stream")]
    // 短期记忆限制，防止Prompt过长
    public int maxShortTermMemories = 10;
    [SerializeField]
    private List<Memory> memoryStream = new List<Memory>();

    private bool isThinking = false;
    private string currentLocationName = "StartPoint";

    void Start()
    {
        // 订阅调度器的到达事件
        scheduler.OnDestinationReached += HandleArrival;

        // 初始状态显示
        UpdateThoughtUI("系统启动中... 正在校准传感器。");
        AddMemory("我刚刚被启动，感觉系统运行正常。", "StartPoint");

        // 开始第一次决策
        StartCoroutine(ThinkAndActRoutine());
    }

    /// <summary>
    /// 更新 UI 显示的方法 (专门用于显示思考内容)
    /// </summary>
    private void UpdateThoughtUI(string text, string prefix = "💡")
    {
        if (thoughtDisplayUI != null)
        {
            // 这里我们不做追加，而是直接覆盖，让它看起来像实时的想法
            // 你也可以做简单的淡入淡出效果，但这里先做基础逻辑
            thoughtDisplayUI.text = $"{prefix} {text}";
        }
    }

    /// <summary>
    /// 记录记忆
    /// </summary>
    public void AddMemory(string content, string location)
    {
        Memory mem = new Memory(content, location);
        memoryStream.Add(mem);
        Debug.Log($"<color=cyan>[Memory]</color> {mem}");
    }

    /// <summary>
    /// 思考下一步行动（基于记忆流）
    /// </summary>
    private System.Collections.IEnumerator ThinkAndActRoutine()
    {
        if (isThinking) yield break;
        isThinking = true;

        UpdateThoughtUI("正在思考下一步该去哪里...", "🤔"); // UI 反馈：正在思考

        yield return new WaitForSeconds(1f); // 模拟思考延迟

        _ = PlanNextMoveAsync();
    }

    private async Task PlanNextMoveAsync()
    {
        Debug.Log("[RobotAgent] 正在根据记忆规划下一步...");

        // 1. 构建 Prompt
        // 这里的 query 可以是"我现在的状态是...我在找..."，RAG 会帮你找以前类似的经历
        string relevantMemories = await memoryClient.RetrieveRelevantMemories($"当前位置: {currentLocationName}, 任务: 探索");

        var msgs = new List<LLMClient.Message>
        {
            new LLMClient.Message { role = "system", content = "..." },
            new LLMClient.Message { role = "user", content =
            $"相关历史记忆：\n{relevantMemories}\n" + // 这里不再是最近几条，而是最有用的几条
            $"当前情况：..."
            }
        };

        // 2. 请求 LLM
        string response = await llmClient.ChatAsync(msgs, 1.1f);

        if (!string.IsNullOrEmpty(response))
        {
            ProcessDecision(response);
        }
        else
        {
            UpdateThoughtUI("连接有点不稳定，我随便走走吧。", "⚠️");
            Debug.LogError("LLM 返回为空，随机去一个地方");
            isThinking = false;
        }
    }

    [System.Serializable]
    private class DecisionJson
    {
        public string reason;
        public string destination;
    }

    private void ProcessDecision(string jsonResponse)
    {
        try
        {
            jsonResponse = jsonResponse.Replace("```json", "").Replace("```", "").Trim();
            DecisionJson decision = JsonUtility.FromJson<DecisionJson>(jsonResponse);

            Debug.Log($"[RobotAgent] 决策: {decision.reason} -> 去往 {decision.destination}");

            // --- 核心修改：将思考原因显示在 UI 上 ---
            UpdateThoughtUI($"{decision.reason}\n(准备前往: {decision.destination})", "💭");

            // 记录决策作为一种“意图”记忆
            AddMemory($"我决定去 {decision.destination}，因为：{decision.reason}", currentLocationName);

            // 执行移动
            Transform targetTF = townMap.GetWaypointByName(decision.destination);
            if (targetTF != null)
            {
                currentLocationName = decision.destination;
                scheduler.MoveToLocation(targetTF.position);
            }
            else
            {
                UpdateThoughtUI($"哎呀，我好像找不到去 {decision.destination} 的路了...", "❌");
                Debug.LogError($"无法找到地点: {decision.destination}");
                isThinking = false;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"解析决策失败: {e.Message}");
            isThinking = false;
        }
    }

    /// <summary>
    /// 到达回调
    /// </summary>
    private void HandleArrival()
    {
        Debug.Log("[RobotAgent] 到达目的地，开始感知环境...");
        isThinking = false;

        // 1. 生成环境观察
        string observation = GenerateRandomObservation(currentLocationName);
        AddMemory(observation, currentLocationName);

        // --- 核心修改：将观察到的内容显示在 UI 上 ---
        UpdateThoughtUI(observation, "👀");

        // 2. 休息一会然后开始下一轮循环
        StartCoroutine(WaitAndThinkAgain());
    }

    private System.Collections.IEnumerator WaitAndThinkAgain()
    {
        // 随机休息 3-6 秒
        float waitTime = Random.Range(3f, 6f);
        yield return new WaitForSeconds(waitTime);
        StartCoroutine(ThinkAndActRoutine());
    }

    private string GenerateRandomObservation(string location)
    {
        string[] templates = new string[]
        {
            "这里今天人很多。",
            "我闻到了空气中淡淡的香味。",
            "地面有点湿滑，我得小心走路。",
            "我看到一只蝴蝶飞过去了。",
            "这里很安静，适合休息数据处理。",
            "有个小孩好奇地看着我。"
        };
        return $"在 {location}，{templates[Random.Range(0, templates.Length)]}";
    }

    // 用户交互接口
    public async void OnUserInteract(string userText)
    {
        AddMemory($"用户对我说: \"{userText}\"", currentLocationName);

        UpdateThoughtUI("正在组织语言...", "💬"); // UI 反馈

        // 构建包含记忆的对话
        string recentMemories = string.Join("\n", memoryStream.TakeLast(5));
        var msgs = new List<LLMClient.Message>
        {
            new LLMClient.Message { role = "system", content = $"{personaDescription} 基于你的记忆回答用户。" },
            new LLMClient.Message { role = "user", content = $"记忆：\n{recentMemories}\n用户问题：{userText}" }
        };

        string reply = await llmClient.ChatAsync(msgs, 0.7f);
        if (!string.IsNullOrEmpty(reply))
        {
            Debug.Log($"Robot says: {reply}");
            AddMemory($"我回答用户: \"{reply}\"", currentLocationName);

            // --- 核心修改：显示回复内容 ---
            UpdateThoughtUI($"\"{reply}\"", "🗣️");
        }
    }
}
