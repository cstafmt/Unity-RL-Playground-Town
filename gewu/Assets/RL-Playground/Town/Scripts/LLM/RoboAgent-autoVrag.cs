using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;

public class RobotAgent_autoVRAG : MonoBehaviour
{
    [Header("Core Components")]
    public TownMap townMap;
    public RouteScheduler scheduler;
    public RobotCamera robotEyes;

    [Header("AI Services")]
    public LLMClient llmClient;
    public VisionClient visionClient;
    public MemoryClient memoryClient;

    [Header("Persona & State")]
    public string robotName = "G1-Explorer";
    [TextArea] public string personaDescription = "You are an intelligent robot. You build a semantic map of the world through vision.";

    [Header("Exploration Settings")]
    public float scanInterval = 4.0f; // 移动时每隔几秒看一眼
    public string treasureKeywords = "gold,box"; // 宝藏关键词(英文)

    [Header("UI Integration")]
    public RoboChatController_Vrag chatController;

    private bool isThinking = false;
    private bool isScanning = false; // 是否正在进行视觉分析
    private string currentLocationName = "StartPoint";
    private string currentGoal = "Explore the town and find hidden treasures.";

    void Start()
    {
        if (scheduler != null) scheduler.OnDestinationReached += HandleArrival;

        _ = memoryClient.AddMemoryAsync("System initialized. Treasure hunting mode active.", "StartPoint");

        // 启动主循环
        StartCoroutine(ThinkAndActRoutine());

        // 【新增】启动巡逻视觉扫描循环
        StartCoroutine(ActiveScanningRoutine());
    }

    // --- 主循环 ---
    private System.Collections.IEnumerator ThinkAndActRoutine()
    {
        // 如果正在思考或者正在处理发现的宝藏，则挂起
        if (isThinking) yield break;
        isThinking = true;

        yield return new WaitForSeconds(1.5f);

        // 只有当没有移动时才规划
        if (!scheduler.IsMoving)
        {
            _ = PlanNextMoveAsync();
        }
        else
        {
            // 如果正在移动，释放思考锁，让下一帧继续检查
            isThinking = false;
        }
    }

    // --- 【新增核心】移动中的主动感知 ---
    private System.Collections.IEnumerator ActiveScanningRoutine()
    {
        while (true)
        {
            // 只有在移动中且没有正在进行视觉请求时才扫描
            if (scheduler.IsMoving && !isScanning && !isThinking)
            {
                _ = PerformVisualScanAsync();
            }
            yield return new WaitForSeconds(scanInterval);
        }
    }

    private async Task PerformVisualScanAsync()
    {
        isScanning = true;

        // 1. 拍照
        string base64Image = robotEyes.CaptureBase64Image();
        if (base64Image.Contains(",")) base64Image = base64Image.Split(',')[1];

        // 2. 视觉分析 (带着寻宝的目的去问)
        // 提示词：描述画面，并特别指出是否有宝藏
        string prompt = $"Describe the scene concisely. Does it contain any of these: {treasureKeywords}? If yes, start with 'FOUND:'";

        string visualDesc = await visionClient.AnalyzeImageAsync(base64Image, prompt);

        // 3. 只有发现重要物体或者到了新环境才存记忆，避免大量垃圾记忆
        if (visualDesc.Contains("FOUND:") || visualDesc.Length > 20)
        {
            string log = $"While moving near {currentLocationName}, I saw: {visualDesc}";
            Debug.Log($"<color=cyan>[Scanning]</color> {log}");
            await memoryClient.AddMemoryAsync(log, currentLocationName);

            // 4. 如果发现了宝藏，触发特殊处理
            if (visualDesc.Contains("FOUND:"))
            {
                HandleTreasureDiscovery(visualDesc);
            }
        }

        isScanning = false;
    }

    // --- 发现宝藏后的紧急处理 ---
    private void HandleTreasureDiscovery(string visualDesc)
    {
        Debug.LogWarning($"<color=red>[TREASURE FOUND]</color> {visualDesc}");

        // 1. 立即停止移动
        scheduler.StopImmediate();

        // 2. 更新状态
        if (chatController != null)
            chatController.AppendHistory($"<color=yellow>System:</color> Stopped! Potential treasure detected.");

        // 3. 强制进入思考流程，决定下一步（比如靠近、捡起或者标记）
        // 我们修改 currentGoal 让 LLM 知道现在的任务变了
        currentGoal = $"URGENT: I spotted a potential treasure ({visualDesc}) while moving. What should I do?";

        // 4. 重置状态以便立即触发 PlanNextMoveAsync
        isThinking = false;
        StopCoroutine(ThinkAndActRoutine()); // 停止旧的思考循环
        StartCoroutine(ThinkAndActRoutine()); // 立即重启思考
    }

    // --- 核心能力 1: RAG + CoT 规划 ---
    private async Task PlanNextMoveAsync()
    {
        Debug.Log($"<color=yellow>[Brain]</color> Planning... Goal: {currentGoal}");

        string ragContext = await memoryClient.RetrieveRelevantMemories(
            $"memories related to {currentGoal} near {currentLocationName}",
            limit: 5,
            recencyWeight: 0.8f // 提高近期记忆权重，因为刚看到的宝藏很重要
        );

        string mapInfo = townMap.GetLocationListPrompt();

        var msgs = new List<LLMClient.Message>
        {
            new LLMClient.Message { role = "system", content =
                $"You are {robotName}. {personaDescription}\n" +
                $"CURRENT GOAL: \"{currentGoal}\"\n" +
                $"RULES:\n" +
                $"1. If exploring, choose a location you haven't visited.\n" +
                $"2. URGENT: If user memory says 'FOUND: treasure' or similar, your plan MUST be to stop and investigate or go to that exact spot. Change destination to 'CurrentPosition' or the closest node.\n" +
                $"3. OUTPUT JSON: {{ \"thought\": \"...\", \"destination\": \"...\", \"reason\": \"...\" }}"
            },
            new LLMClient.Message { role = "user", content =
                $"Current Loc: {currentLocationName}\n" +
                $"Map Nodes: {mapInfo}\n" +
                $"Recent Memories:\n{ragContext}\n\n" +
                $"Decide next move."
            }
        };

        string response = await llmClient.ChatAsync(msgs, 0.7f);

        if (!string.IsNullOrEmpty(response)) ProcessDecision(response);
        else
        {
            isThinking = false;
            StartCoroutine(ThinkAndActRoutine());
        }
    }

    [System.Serializable]
    private class DecisionJson
    {
        public string thought;
        public string destination;
        public string reason;
    }

    private void ProcessDecision(string jsonResponse)
    {
        try
        {
            // 清理 Markdown
            jsonResponse = jsonResponse.Replace("```json", "").Replace("```", "").Trim();

            // 简单的 JSON 提取逻辑
            int startIndex = jsonResponse.IndexOf('{');
            int endIndex = jsonResponse.LastIndexOf('}');
            if (startIndex != -1 && endIndex > startIndex)
            {
                jsonResponse = jsonResponse.Substring(startIndex, endIndex - startIndex + 1);
            }

            DecisionJson decision = JsonUtility.FromJson<DecisionJson>(jsonResponse);

            Debug.Log($"<color=green>[Decision]</color> Go to {decision.destination} ({decision.reason})");
            _ = memoryClient.AddMemoryAsync($"Planning: {decision.thought}", currentLocationName);

            // 如果 LLM 决定留在原地或者去当前位置（因为发现了宝藏）
            if (decision.destination == "CurrentPosition" || decision.destination == currentLocationName)
            {
                Debug.Log("Decided to stay here/investigate.");
                isThinking = false;
                return;
            }

            Transform targetTF = townMap.GetWaypointByName(decision.destination);

            // 模糊匹配逻辑
            if (targetTF == null) targetTF = FindBestMatchingLocation(decision.destination);

            if (targetTF != null)
            {
                currentLocationName = targetTF.name;
                scheduler.MoveToLocation(targetTF.position);
            }
            else
            {
                Debug.LogError($"Unknown location: {decision.destination}");
                isThinking = false; // 允许重试
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"JSON Error: {e.Message}");
            isThinking = false;
        }
    }

    // --- 核心能力 2: 到达后的感知 (保留) ---
    private async void HandleArrival()
    {
        Debug.Log($"[Arrival] Reached {currentLocationName}. Final check...");
        isThinking = false;

        // 到达后也看一眼，确保没漏掉
        await PerformVisualScanAsync();

        // 休息后继续
        StartCoroutine(WaitAndThinkAgain());
    }

    private System.Collections.IEnumerator WaitAndThinkAgain()
    {
        float waitTime = Random.Range(2f, 4f);
        yield return new WaitForSeconds(waitTime);
        isThinking = false; // 确保解锁
        StartCoroutine(ThinkAndActRoutine());
    }

    // --- 核心能力 3: 指令跟随 (代码保持不变) ---
    // ... (你的 OnUserInteract 和 FindBestMatchingLocation 代码保持原样) ...
    [System.Serializable]
    private class UserIntent
    {
        public bool is_command;
        public string target_location;
        public string reply;
    }

    public async void OnUserInteract(string userText)
    {
        Debug.Log($"User: {userText}");
        await memoryClient.AddMemoryAsync($"User said: \"{userText}\"", currentLocationName);

        // ... (保持原有的 OnUserInteract 代码) ...
        // 只需要注意在最后处理 command 时，记得重置 isThinking = false;

        string ragContext = await memoryClient.RetrieveRelevantMemories(
             queryText: "My movement history and visual observations around the town",
             limit: 5
         );

        Debug.Log($"<color=green>[RAG Context]</color>: {ragContext}");
        string mapInfo = townMap.GetLocationListPrompt();
        var msgs = new List<LLMClient.Message>
        {
            new LLMClient.Message { role = "system", content =
                $"You are {robotName}. {personaDescription}\n" +
                $"KNOWN LOCATIONS: {mapInfo}\n" +
                $"Your MEMORIES:\n{ragContext}\n\n" +
                $"INSTRUCTIONS:\n" +
                $"1. Analyze the user's input.\n" +
                $"2. If the user asks you to move (e.g., 'Come here', 'Go to kitchen'), set 'is_command' to true and find the closest location name from KNOWN LOCATIONS.\n" +
                $"3. Answer the user's question based on your MEMORIES.\n" +
                $"4. CRITICAL: You MUST respond in valid JSON format. Do not include markdown blocks like ```json.\n" +
                $"JSON TEMPLATE:\n" +
                $"{{\n" +
                $"  \"is_command\": boolean,\n" +
                $"  \"target_location\": \"string (exact map node name or empty)\",\n" +
                $"  \"reply\": \"string (your response to user)\"\n" +
                $"}}"
            },
            new LLMClient.Message { role = "user", content = userText }
        };

        string json = "";
        try { json = await llmClient.ChatAsync(msgs, 0.1f); }
        catch { return; }

        if (string.IsNullOrWhiteSpace(json)) return;

        // ... JSON 解析逻辑 (同之前修复版) ...
        try
        {
            json = json.Replace("```json", "").Replace("```", "").Trim();
            int s = json.IndexOf('{'); int e = json.LastIndexOf('}');
            if (s != -1 && e > s) json = json.Substring(s, e - s + 1);

            var settings = new JsonSerializerSettings { FloatParseHandling = FloatParseHandling.Double, MissingMemberHandling = MissingMemberHandling.Ignore };
            UserIntent intent = JsonConvert.DeserializeObject<UserIntent>(json, settings);

            Debug.Log($"Bot: {intent.reply}");
            await memoryClient.AddMemoryAsync($"I replied: {intent.reply}", currentLocationName);
            if (chatController != null) chatController.AppendHistory($"<color=cyan>{robotName}:</color> {intent.reply}");

            if (intent.is_command)
            {
                string rawTarget = intent.target_location;
                Transform targetTF = townMap.GetWaypointByName(rawTarget);
                if (targetTF == null) targetTF = FindBestMatchingLocation(rawTarget);

                if (targetTF != null)
                {
                    this.currentGoal = $"USER ORDER: Go to {targetTF.name}";
                    StopAllCoroutines(); // 打断当前动作
                    isThinking = false;
                    currentLocationName = targetTF.name;
                    scheduler.MoveToLocation(targetTF.position);

                    // 重启感知和思考
                    StartCoroutine(ThinkAndActRoutine());
                    StartCoroutine(ActiveScanningRoutine());
                }
            }
        }
        catch (System.Exception ex) { Debug.LogError(ex.Message); }
    }

    private Transform FindBestMatchingLocation(string rawTarget)
    {
        if (string.IsNullOrEmpty(rawTarget)) return null;
        rawTarget = rawTarget.ToLower().Trim();
        foreach (Transform t in townMap.transform)
        {
            if (t.name.ToLower().Contains(rawTarget)) return t;
        }
        return null;
    }
}
