//using UnityEngine;
//using UnityEngine.AI;
//using System.Collections.Generic;
//using System.Threading.Tasks;
//using System.Linq;
//using Newtonsoft.Json;

//public class RobotAgent_VRAG : MonoBehaviour
//{
//    [Header("Core Components")]
//    public TownMap townMap;
//    public RouteScheduler scheduler;
//    public RobotCamera robotEyes;

//    [Header("AI Services")]
//    public LLMClient llmClient;
//    public VisionClient visionClient;
//    public MemoryClient memoryClient;

//    [Header("Persona & Settings")]
//    public string robotName = "G1-Explorer";
//    [TextArea]
//    public string personaDescription =
//        "You are an autonomous explorer robot with a credibility-aware memory system. " +
//        "You evaluate the reliability of your visual observations and make decisions based on trusted memories.";

//    public float wanderRadius = 15.0f;
//    public float scanInterval = 25.0f;

//    [Header("Treasure Detection")]
//    public string treasureTag = "Treasure";
//    public float treasureStopDistance = 2.0f;

//    [Header("Memory Credibility Settings")]
//    [Tooltip("璁板繂鍙俊搴﹂槇鍊硷紝浣庝簬姝ゅ€肩殑璁板繂灏嗚杩囨护")]
//    [Range(0f, 1f)] public float credibilityThreshold = 0.3f;
//    [Tooltip("璁板繂姣忓垎閽熻“鍑忕巼")]
//    [Range(0f, 0.1f)] public float decayRatePerMinute = 0.02f;
//    [Tooltip("閲嶅瑙傚療鏃跺彲淇″害鎻愬崌閲?)]
//    [Range(0f, 0.5f)] public float confirmationBoost = 0.15f;

//    [Header("UI Integration")]
//    public RoboChatController_Vrag chatController;

//    // =====================================================================
//    // --- 璁板繂鍙俊搴︾郴缁?---
//    // =====================================================================

//    /// <summary>
//    /// 甯﹀彲淇″害璇勫垎鐨勮瑙夎蹇嗘潯鐩?
//    /// </summary>
//    [System.Serializable]
//    public class VisualMemoryEntry
//    {
//        public string id;                   // 鍞竴ID
//        public string description;          // 瑙嗚鎻忚堪
//        public string location;             // 瑙傚療浣嶇疆
//        public Vector3 worldPosition;       // 涓栫晫鍧愭爣
//        public float credibility;           // 鍙俊搴?0-1
//        public float timestamp;             // 璁板綍鏃堕棿 (Time.time)
//        public int confirmationCount;       // 琚‘璁ゆ鏁?
//        public string category;             // 鍒嗙被: "landmark", "treasure", "path", "object", "ambient"
//        public bool containsTreasure;       // 鏄惁鍖呭惈瀹濈墿淇℃伅

//        // 鍙俊搴﹁瘎浼扮殑瀛愮淮搴?
//        public float descriptionQuality;    // 鎻忚堪璐ㄩ噺 0-1
//        public float spatialConsistency;    // 绌洪棿涓€鑷存€?0-1
//        public float temporalRelevance;     // 鏃堕棿鐩稿叧鎬?0-1
//        public float visualClarity;         // 瑙嗚娓呮櫚搴?0-1

//        /// <summary>
//        /// 璁＄畻褰撳墠鏃跺埢鐨勬湁鏁堝彲淇″害锛堣€冭檻鏃堕棿琛板噺锛?
//        /// </summary>
//        public float GetEffectiveCredibility(float currentTime, float decayRate)
//        {
//            float minutesElapsed = (currentTime - timestamp) / 60f;
//            float decay = Mathf.Exp(-decayRate * minutesElapsed);
//            float confirmBonus = Mathf.Min(confirmationCount * 0.1f, 0.3f);
//            return Mathf.Clamp01((credibility + confirmBonus) * decay);
//        }

//        public override string ToString()
//        {
//            return $"[C:{credibility:F2}|x{confirmationCount}] {description} @{location}";
//        }
//    }

//    // 鏈湴璁板繂瀛樺偍锛堝甫鍙俊搴︼級
//    private List<VisualMemoryEntry> visualMemories = new List<VisualMemoryEntry>();
//    private const int MAX_MEMORIES = 100;
//    private int memoryIdCounter = 0;

//    // --- 杩愯妯″紡 ---
//    private enum RobotMode
//    {
//        EXPLORE,
//        FIND_TREASURE,
//        GO_TO_LOCATION,
//        IDLE
//    }

//    private RobotMode currentMode = RobotMode.EXPLORE;

//    // --- 鐘舵€佸彉閲?---
//    private bool isThinking = false;
//    private bool isScanning = false;
//    private bool isVisionBusy = false;
//    private bool isWaitingForLLM = false;
//    private string currentLocationName = "StartPoint";
//    private string currentGoal = "Autonomously explore the unknown areas.";

//    private List<string> visitedHistory = new List<string>();
//    private Transform lockedTarget = null;
//    private bool isChasingTreasure = false;
//    private int arrivalRetryCount = 0;
//    private const int MAX_ARRIVAL_RETRIES = 3;

//    private string lastVisionDescription = "";
//    private int duplicateVisionCount = 0;

//    // Vision Prompt
//    private const string VISION_PROMPT =
//        "Describe what you see in this image in 2-3 sentences. " +
//        "Mention the environment type (indoor/outdoor), notable objects, colors, materials, and spatial layout. " +
//        "Rate the image clarity from 1-10 at the end in format [Clarity:N]. " +
//        "If you see a bright YELLOW CUBE or YELLOW BOX, " +
//        "start with 'FOUND: yellow cube' then describe its exact position (left/right/center, near/far). " +
//        "If there is NO yellow cube, do NOT say FOUND.";

//    // =====================================================================
//    // --- 鍒濆鍖?---
//    // =====================================================================
//    void Start()
//    {
//        if (scheduler != null) scheduler.OnDestinationReached += HandleArrival;

//        _ = AddLongTermMemoryAsync(
//            "System initialized. Credibility-Aware Memory System: ONLINE.",
//            "StartPoint"
//        );

//        AddBootstrapMemory();

//        StartCoroutine(ThinkAndActRoutine());
//        StartCoroutine(ActiveScanningRoutine());
//        StartCoroutine(MemoryMaintenanceRoutine());
//    }

//    private void AddBootstrapMemory()
//    {
//        var bootMemory = new VisualMemoryEntry
//        {
//            id = $"mem_{memoryIdCounter++}",
//            description = "System boot. Starting at spawn point. No observations yet.",
//            location = "StartPoint",
//            worldPosition = transform.position,
//            credibility = 1.0f,
//            timestamp = Time.time,
//            confirmationCount = 0,
//            category = "system",
//            containsTreasure = false,
//            descriptionQuality = 1f,
//            spatialConsistency = 1f,
//            temporalRelevance = 1f,
//            visualClarity = 1f
//        };
//        visualMemories.Add(bootMemory);
//    }

//    // =====================================================================
//    // --- 1. 涓绘€濊€冨惊鐜?---
//    // =====================================================================
//    private System.Collections.IEnumerator ThinkAndActRoutine()
//    {
//        while (true)
//        {
//            if (!scheduler.IsMoving && !isThinking && !isWaitingForLLM && !isChasingTreasure)
//            {
//                Debug.Log("<color=magenta>[Think]</color> Will plan in 3 seconds...");
//                isWaitingForLLM = true;
//                yield return new WaitForSeconds(3.0f);
//                PlanNextMoveWrapper();
//            }
//            yield return new WaitForSeconds(5.0f);
//        }
//    }

//    private async void PlanNextMoveWrapper()
//    {
//        if (isThinking)
//        {
//            isWaitingForLLM = false;
//            return;
//        }
//        try
//        {
//            await PlanNextMoveAsync();
//        }
//        catch (System.Exception e)
//        {
//            Debug.LogError($"[Think] Error: {e.Message}");
//        }
//        finally
//        {
//            isThinking = false;
//            isWaitingForLLM = false;
//            if (!scheduler.IsMoving && !isChasingTreasure)
//            {
//                Debug.LogWarning("[Think] Still not moving. Force wandering.");
//                FallbackWander();
//            }
//        }
//    }

//    // =====================================================================
//    // --- 2. 瑙嗚鎵弿寰幆 ---
//    // =====================================================================
//    private System.Collections.IEnumerator ActiveScanningRoutine()
//    {
//        yield return new WaitForSeconds(5.0f);

//        while (true)
//        {
//            if (!isScanning && !isVisionBusy)
//            {
//                Debug.Log("<color=yellow>[Eye]</color> Triggering visual scan...");
//                _ = PerformVisualScanAsync();
//            }
//            yield return new WaitForSeconds(scanInterval);
//        }
//    }

//    private async Task PerformVisualScanAsync()
//    {
//        if (isScanning || isVisionBusy) return;
//        isScanning = true;
//        isVisionBusy = true;

//        try
//        {
//            string base64Image = robotEyes.CaptureBase64Image();
//            if (string.IsNullOrEmpty(base64Image)) return;
//            if (base64Image.Contains(",")) base64Image = base64Image.Split(',')[1];

//            int imageSizeKB = base64Image.Length * 3 / 4 / 1024;
//            Debug.Log($"<color=yellow>[Eye]</color> Image: {imageSizeKB}KB. Sending...");

//            string desc = await visionClient.AnalyzeImageAsync(base64Image, VISION_PROMPT);
//            Debug.Log($"<color=yellow>[Eye]</color> Vision ({desc.Length} chars): {desc}");

//            if (string.IsNullOrEmpty(desc) || desc.Length < 10) return;
//            if (desc.Contains("Unable") || desc.Contains("Error") || desc.Contains("offline")) return;

//            // 鈽?鏍稿績鍒涙柊锛氬彲淇″害鍒嗘瀽
//            VisualMemoryEntry memEntry = AnalyzeAndCreateMemory(desc, imageSizeKB);

//            if (memEntry == null)
//            {
//                Debug.Log("<color=grey>[Memory]</color> Duplicate or low-quality. Skipped.");
//                return;
//            }

//            // 瀛樺叆鏈湴鍙俊搴﹁蹇?
//            AddVisualMemory(memEntry);

//            // 鍚屾椂瀛樺叆 ChromaDB锛堝甫鍙俊搴︽爣绛撅級
//            string taggedMemory = $"[Credibility:{memEntry.credibility:F2}][{memEntry.category}] {memEntry.description}";
//            await AddLongTermMemoryAsync(taggedMemory, memEntry.location);
//            Debug.Log($"<color=cyan>[Memory] Stored (C={memEntry.credibility:F2}):</color> {memEntry.description}");

//            // 瀹濈墿妫€娴?
//            if (memEntry.containsTreasure)
//            {
//                Debug.LogWarning($"<color=red>鈽?TREASURE (C={memEntry.credibility:F2}) 鈽?/color> {desc}");
//                HandleTreasureDetection(memEntry);
//            }
//        }
//        catch (System.Exception e)
//        {
//            Debug.LogWarning($"<color=orange>[Vision Error]</color> {e.Message}");
//        }
//        finally
//        {
//            isScanning = false;
//            isVisionBusy = false;
//        }
//    }

//    // =====================================================================
//    // --- 鈽?鍒涙柊鏍稿績锛氬缁村害鍙俊搴﹀垎鏋?---
//    // =====================================================================

//    /// <summary>
//    /// 鍒嗘瀽瑙嗚鎻忚堪锛屽垱寤哄甫鍙俊搴﹁瘎鍒嗙殑璁板繂鏉＄洰
//    /// </summary>
//    private VisualMemoryEntry AnalyzeAndCreateMemory(string description, int imageSizeKB)
//    {
//        // --- 鍘婚噸妫€鏌?---
//        if (IsSimilarToRecent(description))
//        {
//            // 鎵惧埌鐩镐技璁板繂锛屾彁鍗囧叾鍙俊搴︼紙纭鏈哄埗锛?
//            BoostSimilarMemoryCredibility(description);
//            return null;
//        }

//        string loc = currentLocationName == "Unknown Area"
//            ? $"Coord({transform.position.x:F0},{transform.position.z:F0})"
//            : currentLocationName;

//        // --- 缁村害1锛氭弿杩拌川閲忚瘎浼?---
//        float descQuality = EvaluateDescriptionQuality(description);

//        // --- 缁村害2锛氳瑙夋竻鏅板害锛堜粠鎻忚堪涓彁鍙?[Clarity:N]锛?---
//        float visualClarity = ExtractClarityScore(description);

//        // --- 缁村害3锛氬浘鐗囧ぇ灏忓悎鐞嗘€?---
//        float imageSizeScore = EvaluateImageSize(imageSizeKB);

//        // --- 缁村害4锛氱┖闂翠竴鑷存€э紙涓庢渶杩戣蹇嗙殑浣嶇疆鏄惁杩炶疮锛?---
//        float spatialScore = EvaluateSpatialConsistency(transform.position);

//        // --- 缁村害5锛氬唴瀹逛竴鑷存€э紙鏄惁涓庡凡鐭ョ幆澧冧俊鎭煕鐩撅級 ---
//        float contentScore = EvaluateContentConsistency(description);

//        // --- 缁煎悎鍙俊搴﹁绠楋紙鍔犳潈骞冲潎锛?---
//        float credibility =
//            descQuality * 0.25f +
//            visualClarity * 0.20f +
//            imageSizeScore * 0.10f +
//            spatialScore * 0.20f +
//            contentScore * 0.25f;

//        credibility = Mathf.Clamp01(credibility);

//        // --- 鍒嗙被 ---
//        string category = CategorizeMemory(description);
//        bool hasTreasure = description.ToLower().Contains("found");

//        // 濡傛灉鍖呭惈瀹濈墿淇℃伅锛岄澶栭獙璇?
//        if (hasTreasure)
//        {
//            credibility = ValidateTreasureClaim(description, credibility);
//        }

//        var entry = new VisualMemoryEntry
//        {
//            id = $"mem_{memoryIdCounter++}",
//            description = CleanDescription(description),
//            location = loc,
//            worldPosition = transform.position,
//            credibility = credibility,
//            timestamp = Time.time,
//            confirmationCount = 0,
//            category = category,
//            containsTreasure = hasTreasure,
//            descriptionQuality = descQuality,
//            spatialConsistency = spatialScore,
//            temporalRelevance = 1.0f,
//            visualClarity = visualClarity
//        };

//        Debug.Log($"<color=magenta>[Credibility Analysis]</color>\n" +
//                  $"  Description Quality: {descQuality:F2}\n" +
//                  $"  Visual Clarity: {visualClarity:F2}\n" +
//                  $"  Image Size Score: {imageSizeScore:F2}\n" +
//                  $"  Spatial Consistency: {spatialScore:F2}\n" +
//                  $"  Content Consistency: {contentScore:F2}\n" +
//                  $"  鈽?Final Credibility: {credibility:F2}\n" +
//                  $"  Category: {category} | Treasure: {hasTreasure}");

//        return entry;
//    }

//    /// <summary>
//    /// 缁村害1锛氭弿杩拌川閲?鈥?闀垮害銆佺粏鑺備赴瀵屽害銆佽娉曞畬鏁存€?
//    /// </summary>
//    private float EvaluateDescriptionQuality(string desc)
//    {
//        float score = 0f;

//        // 闀垮害璇勫垎锛堝お鐭垨澶暱閮戒笉濂斤級
//        int len = desc.Length;
//        if (len < 20) score += 0.1f;
//        else if (len < 50) score += 0.3f;
//        else if (len < 150) score += 0.7f;
//        else if (len < 300) score += 1.0f;
//        else if (len < 500) score += 0.9f;
//        else score += 0.7f; // 澶暱鍙兘鏄够瑙?

//        // 缁嗚妭璇嶆眹妫€娴?
//        string lower = desc.ToLower();
//        string[] detailWords = { "left", "right", "center", "near", "far", "behind",
//                                  "wooden", "stone", "metal", "large", "small",
//                                  "building", "path", "tree", "wall", "gate",
//                                  "red", "blue", "green", "yellow", "brown" };

//        int detailCount = detailWords.Count(w => lower.Contains(w));
//        float detailScore = Mathf.Clamp01(detailCount / 5f);

//        // 鍙ュ瓙鏁伴噺
//        int sentenceCount = desc.Split('.', '!', '?').Count(s => s.Trim().Length > 5);
//        float sentenceScore = Mathf.Clamp01(sentenceCount / 3f);

//        return (score * 0.4f + detailScore * 0.35f + sentenceScore * 0.25f);
//    }

//    /// <summary>
//    /// 缁村害2锛氫粠鎻忚堪涓彁鍙?[Clarity:N] 璇勫垎
//    /// </summary>
//    private float ExtractClarityScore(string desc)
//    {
//        // 瀵绘壘 [Clarity:N] 鏍煎紡
//        int idx = desc.IndexOf("[Clarity:");
//        if (idx >= 0)
//        {
//            int start = idx + 9;
//            int end = desc.IndexOf(']', start);
//            if (end > start)
//            {
//                string numStr = desc.Substring(start, end - start).Trim();
//                if (float.TryParse(numStr, out float clarity))
//                {
//                    return Mathf.Clamp01(clarity / 10f);
//                }
//            }
//        }

//        // 娌℃湁鏍囨敞锛屾牴鎹弿杩伴棿鎺ユ帹鏂?
//        string lower = desc.ToLower();
//        if (lower.Contains("blurr") || lower.Contains("unclear") || lower.Contains("dark"))
//            return 0.3f;
//        if (lower.Contains("clear") || lower.Contains("bright") || lower.Contains("detailed"))
//            return 0.8f;

//        return 0.6f; // 榛樿涓瓑
//    }

//    /// <summary>
//    /// 缁村害3锛氬浘鐗囧ぇ灏忓悎鐞嗘€?
//    /// </summary>
//    private float EvaluateImageSize(int sizeKB)
//    {
//        if (sizeKB < 5) return 0.1f;        // 澶皬锛屽彲鑳芥崯鍧?
//        if (sizeKB < 20) return 0.4f;       // 鍋忓皬
//        if (sizeKB < 50) return 0.7f;       // 姝ｅ父鍋忓皬
//        if (sizeKB < 200) return 1.0f;      // 鐞嗘兂鑼冨洿
//        if (sizeKB < 500) return 0.9f;      // 鍋忓ぇ浣嗗彲鎺ュ彈
//        return 0.7f;                         // 澶ぇ锛屽彲鑳戒紶杈撴湁闂
//    }

//    /// <summary>
//    /// 缁村害4锛氱┖闂翠竴鑷存€?鈥?褰撳墠浣嶇疆涓庢渶杩戣蹇嗕綅缃槸鍚﹁繛璐?
//    /// </summary>
//    private float EvaluateSpatialConsistency(Vector3 currentPos)
//    {
//        if (visualMemories.Count == 0) return 0.8f;

//        // 鎵炬渶杩戜竴鏉¤蹇?
//        var lastMemory = visualMemories[visualMemories.Count - 1];
//        float dist = Vector3.Distance(currentPos, lastMemory.worldPosition);
//        float timeDiff = Time.time - lastMemory.timestamp;

//        if (timeDiff <= 0) return 0.8f;

//        // 璁＄畻"閫熷害"锛堣窛绂?鏃堕棿锛夛紝鍒ゆ柇鏄惁鍚堢悊
//        float speed = dist / timeDiff;

//        // NavMeshAgent 姝ｅ父閫熷害澶х害 3-5 m/s
//        if (speed < 8f) return 1.0f;        // 鍚堢悊閫熷害
//        if (speed < 15f) return 0.7f;       // 鍋忓揩浣嗗彲鑳?
//        if (speed < 30f) return 0.4f;       // 鍙枒锛屽彲鑳戒紶閫佷簡
//        return 0.2f;                         // 闈炲父鍙枒
//    }

//    /// <summary>
//    /// 缁村害5锛氬唴瀹逛竴鑷存€?鈥?鎻忚堪鏄惁涓庡凡鐭ョ幆澧冧俊鎭竴鑷?
//    /// </summary>
//    private float EvaluateContentConsistency(string desc)
//    {
//        string lower = desc.ToLower();
//        float score = 0.7f; // 鍩虹鍒?

//        // 妫€鏌ユ槸鍚︽湁鏄庢樉鐨勫够瑙夋爣蹇?
//        string[] hallucinationMarkers = {
//            "person standing", "people walking", "car driving", "modern city",
//            "ocean", "beach", "snow mountain", "real photo", "photograph"
//        };

//        foreach (string marker in hallucinationMarkers)
//        {
//            if (lower.Contains(marker))
//            {
//                score -= 0.2f;
//                Debug.Log($"<color=orange>[Credibility]</color> Hallucination marker: '{marker}'");
//            }
//        }

//        // 涓庢父鎴忓満鏅竴鑷寸殑鎻忚堪鍔犲垎
//        string[] consistentMarkers = {
//            "3d", "render", "virtual", "game", "building", "stone", "path",
//            "temple", "gate", "lantern", "wooden", "village", "town"
//        };

//        foreach (string marker in consistentMarkers)
//        {
//            if (lower.Contains(marker))
//            {
//                score += 0.05f;
//            }
//        }

//        return Mathf.Clamp01(score);
//    }

//    /// <summary>
//    /// 瀹濈墿澹版槑鐨勯澶栭獙璇?
//    /// </summary>
//    private float ValidateTreasureClaim(string desc, float baseCredibility)
//    {
//        string lower = desc.ToLower();
//        float bonus = 0f;

//        // 鎻忚堪涓寘鍚叿浣撲綅缃俊鎭?鈫?鏇村彲淇?
//        if (lower.Contains("left") || lower.Contains("right") || lower.Contains("center"))
//            bonus += 0.1f;

//        // 鎻忚堪涓寘鍚窛绂讳俊鎭?鈫?鏇村彲淇?
//        if (lower.Contains("near") || lower.Contains("far") || lower.Contains("close"))
//            bonus += 0.05f;

//        // 鎻忚堪涓寘鍚鑹茬‘璁?鈫?鏇村彲淇?
//        if (lower.Contains("yellow") && lower.Contains("cube"))
//            bonus += 0.1f;

//        // 涔嬪墠鍦ㄩ檮杩戜篃鐪嬪埌杩囧疂鐗?鈫?澶у箙鍔犲垎
//        int nearbyTreasureMemories = visualMemories.Count(m =>
//            m.containsTreasure &&
//            Vector3.Distance(m.worldPosition, transform.position) < 30f
//        );
//        if (nearbyTreasureMemories > 0)
//            bonus += 0.15f;

//        return Mathf.Clamp01(baseCredibility + bonus);
//    }

//    /// <summary>
//    /// 璁板繂鍒嗙被
//    /// </summary>
//    private string CategorizeMemory(string desc)
//    {
//        string lower = desc.ToLower();

//        if (lower.Contains("found") || lower.Contains("treasure") || lower.Contains("yellow cube"))
//            return "treasure";
//        if (lower.Contains("building") || lower.Contains("temple") || lower.Contains("tower") || lower.Contains("gate"))
//            return "landmark";
//        if (lower.Contains("path") || lower.Contains("road") || lower.Contains("bridge") || lower.Contains("street"))
//            return "path";
//        if (lower.Contains("tree") || lower.Contains("grass") || lower.Contains("water") || lower.Contains("sky"))
//            return "ambient";

//        return "object";
//    }

//    /// <summary>
//    /// 娓呯悊鎻忚堪鏂囨湰锛堝幓鎺?Clarity 鏍囪绛夛級
//    /// </summary>
//    private string CleanDescription(string desc)
//    {
//        // 鍘绘帀 [Clarity:N]
//        int idx = desc.IndexOf("[Clarity:");
//        if (idx >= 0)
//        {
//            int end = desc.IndexOf(']', idx);
//            if (end > idx)
//            {
//                desc = desc.Substring(0, idx) + desc.Substring(end + 1);
//            }
//        }
//        return desc.Trim();
//    }

//    // =====================================================================
//    // --- 璁板繂鍘婚噸涓庣‘璁ゆ満鍒?---
//    // =====================================================================

//    private bool IsSimilarToRecent(string newDesc)
//    {
//        string newLower = newDesc.ToLower().Trim();

//        // 妫€鏌ユ渶杩?5 鏉¤蹇?
//        int checkCount = Mathf.Min(5, visualMemories.Count);
//        for (int i = visualMemories.Count - 1; i >= visualMemories.Count - checkCount && i >= 0; i--)
//        {
//            string oldLower = visualMemories[i].description.ToLower().Trim();

//            if (newLower == oldLower) return true;

//            // 閮芥槸鐭?FOUND 鏂囨湰
//            if (newLower.Length < 30 && oldLower.Length < 30 &&
//                newLower.Contains("found") && oldLower.Contains("found"))
//                return true;

//            // 鍏抽敭璇嶉噸鍙犲害
//            var newWords = new HashSet<string>(
//                newLower.Split(' ', ',', '.', '!', '?').Where(w => w.Length > 3));
//            var oldWords = new HashSet<string>(
//                oldLower.Split(' ', ',', '.', '!', '?').Where(w => w.Length > 3));

//            if (newWords.Count > 0 && oldWords.Count > 0)
//            {
//                int overlap = newWords.Intersect(oldWords).Count();
//                float similarity = (float)overlap / Mathf.Max(newWords.Count, oldWords.Count);
//                if (similarity > 0.7f) return true;
//            }
//        }
//        return false;
//    }

//    /// <summary>
//    /// 鈽?纭鏈哄埗锛氶噸澶嶈瀵熷埌鐩镐技鍐呭鏃讹紝鎻愬崌宸叉湁璁板繂鐨勫彲淇″害
//    /// </summary>
//    private void BoostSimilarMemoryCredibility(string newDesc)
//    {
//        string newLower = newDesc.ToLower();

//        for (int i = visualMemories.Count - 1; i >= Mathf.Max(0, visualMemories.Count - 5); i--)
//        {
//            string oldLower = visualMemories[i].description.ToLower();

//            bool isSimilar = false;
//            if (newLower.Contains("found") && oldLower.Contains("found")) isSimilar = true;

//            var newWords = new HashSet<string>(newLower.Split(' ', ',', '.').Where(w => w.Length > 3));
//            var oldWords = new HashSet<string>(oldLower.Split(' ', ',', '.').Where(w => w.Length > 3));
//            if (newWords.Count > 0 && oldWords.Count > 0)
//            {
//                float sim = (float)newWords.Intersect(oldWords).Count() / Mathf.Max(newWords.Count, oldWords.Count);
//                if (sim > 0.6f) isSimilar = true;
//            }

//            if (isSimilar)
//            {
//                visualMemories[i].confirmationCount++;
//                visualMemories[i].credibility = Mathf.Clamp01(
//                    visualMemories[i].credibility + confirmationBoost
//                );

//                Debug.Log($"<color=green>[Memory Confirmed]</color> {visualMemories[i].id} " +
//                          $"x{visualMemories[i].confirmationCount} 鈫?C={visualMemories[i].credibility:F2}");
//                break;
//            }
//        }
//    }

//    /// <summary>
//    /// 娣诲姞璁板繂鍒版湰鍦板瓨鍌紙鑷姩娣樻卑浣庡彲淇″害鏃ц蹇嗭級
//    /// </summary>
//    private void AddVisualMemory(VisualMemoryEntry entry)
//    {
//        visualMemories.Add(entry);

//        // 瓒呰繃涓婇檺鏃讹紝娣樻卑鍙俊搴︽渶浣庣殑
//        if (visualMemories.Count > MAX_MEMORIES)
//        {
//            float minCred = float.MaxValue;
//            int minIdx = 0;

//            for (int i = 0; i < visualMemories.Count; i++)
//            {
//                float eff = visualMemories[i].GetEffectiveCredibility(Time.time, decayRatePerMinute);
//                if (eff < minCred)
//                {
//                    minCred = eff;
//                    minIdx = i;
//                }
//            }

//            Debug.Log($"<color=grey>[Memory Evict]</color> {visualMemories[minIdx].id} (C={minCred:F2})");
//            visualMemories.RemoveAt(minIdx);
//        }
//    }

//    // =====================================================================
//    // --- 3. 璁板繂缁存姢鍗忕▼锛堝畾鏈熻“鍑?+ 娓呯悊锛?---
//    // =====================================================================
//    private System.Collections.IEnumerator MemoryMaintenanceRoutine()
//    {
//        while (true)
//        {
//            yield return new WaitForSeconds(60f); // 姣忓垎閽熺淮鎶や竴娆?

//            int removed = 0;
//            for (int i = visualMemories.Count - 1; i >= 0; i--)
//            {
//                float eff = visualMemories[i].GetEffectiveCredibility(Time.time, decayRatePerMinute);

//                // 娣樻卑浣庝簬闃堝€肩殑璁板繂
//                if (eff < credibilityThreshold && visualMemories[i].category != "treasure")
//                {
//                    visualMemories.RemoveAt(i);
//                    removed++;
//                }
//            }

//            if (removed > 0)
//            {
//                Debug.Log($"<color=grey>[Memory Maintenance]</color> Removed {removed} low-credibility memories. " +
//                          $"Remaining: {visualMemories.Count}");
//            }

//            // 杈撳嚭璁板繂鐘舵€佹憳瑕?
//            LogMemoryStatus();
//        }
//    }

//    private void LogMemoryStatus()
//    {
//        if (visualMemories.Count == 0) return;

//        float avgCred = visualMemories.Average(m => m.GetEffectiveCredibility(Time.time, decayRatePerMinute));
//        int treasureCount = visualMemories.Count(m => m.containsTreasure);
//        int highCred = visualMemories.Count(m => m.GetEffectiveCredibility(Time.time, decayRatePerMinute) > 0.7f);

//        Debug.Log($"<color=magenta>[Memory Status]</color> Total: {visualMemories.Count} | " +
//                  $"Avg Credibility: {avgCred:F2} | High-C: {highCred} | Treasure refs: {treasureCount}");
//    }

//    // =====================================================================
//    // --- 鈽?鍙俊搴﹀姞鏉?RAG ---
//    // =====================================================================

//    /// <summary>
//    /// 鍩轰簬鍙俊搴﹁繃婊ゅ拰鎺掑簭鐨?RAG 妫€绱?
//    /// </summary>
//    private string GetCredibilityWeightedContext(string query, int maxTokens = 300)
//    {
//        float currentTime = Time.time;

//        // 璁＄畻姣忔潯璁板繂鐨勬湁鏁堝彲淇″害
//        var scoredMemories = visualMemories
//            .Select(m => new
//            {
//                memory = m,
//                effectiveCredibility = m.GetEffectiveCredibility(currentTime, decayRatePerMinute),
//                relevance = CalculateRelevance(m, query)
//            })
//            .Where(x => x.effectiveCredibility >= credibilityThreshold) // 杩囨护浣庡彲淇″害
//            .OrderByDescending(x => x.effectiveCredibility * 0.4f + x.relevance * 0.6f) // 鍔犳潈鎺掑簭
//            .ToList();

//        // 鏋勫缓涓婁笅鏂囧瓧绗︿覆
//        var contextLines = new List<string>();
//        int charCount = 0;

//        foreach (var item in scoredMemories)
//        {
//            string line = $"[C:{item.effectiveCredibility:F1}|{item.memory.category}] " +
//                          $"{item.memory.description} @{item.memory.location}";

//            if (charCount + line.Length > maxTokens * 4) break; // 绮楃暐浼拌 token

//            contextLines.Add(line);
//            charCount += line.Length;
//        }

//        string context = string.Join("\n", contextLines);

//        Debug.Log($"<color=magenta>[RAG]</color> Query: '{query.Substring(0, Mathf.Min(50, query.Length))}...' " +
//                  $"鈫?{contextLines.Count}/{visualMemories.Count} memories (threshold: {credibilityThreshold:F1})");

//        return context;
//    }

//    /// <summary>
//    /// 璁＄畻璁板繂涓庢煡璇㈢殑鐩稿叧搴?
//    /// </summary>
//    private float CalculateRelevance(VisualMemoryEntry memory, string query)
//    {
//        string queryLower = query.ToLower();
//        string memLower = memory.description.ToLower();

//        // 鍏抽敭璇嶅尮閰?
//        var queryWords = new HashSet<string>(
//            queryLower.Split(' ', ',', '.', '?', '!').Where(w => w.Length > 3));
//        var memWords = new HashSet<string>(
//            memLower.Split(' ', ',', '.', '?', '!').Where(w => w.Length > 3));

//        if (queryWords.Count == 0) return 0.5f;

//        int overlap = queryWords.Intersect(memWords).Count();
//        float wordRelevance = (float)overlap / queryWords.Count;

//        // 绫诲埆鍖归厤
//        float categoryBonus = 0f;
//        if (queryLower.Contains("treasure") && memory.category == "treasure") categoryBonus = 0.3f;
//        if (queryLower.Contains("explore") && memory.category == "landmark") categoryBonus = 0.2f;
//        if (queryLower.Contains("path") && memory.category == "path") categoryBonus = 0.2f;

//        // 璺濈鐩稿叧鎬э紙杩戠殑鏇寸浉鍏筹級
//        float dist = Vector3.Distance(transform.position, memory.worldPosition);
//        float distRelevance = Mathf.Clamp01(1f - dist / 100f);

//        return Mathf.Clamp01(wordRelevance * 0.5f + categoryBonus + distRelevance * 0.2f);
//    }

//    // =====================================================================
//    // --- 瀹濈墿妫€娴嬶紙鍩轰簬鍙俊搴︼級 ---
//    // =====================================================================
//    private void HandleTreasureDetection(VisualMemoryEntry memEntry)
//    {
//        // 鈽?鍙湁鍙俊搴﹁冻澶熼珮鎵嶇湡姝ｅ幓杩?
//        if (memEntry.credibility < 0.5f)
//        {
//            Debug.Log($"<color=orange>[TREASURE]</color> Low credibility ({memEntry.credibility:F2}). " +
//                      $"Needs more confirmation before chasing.");

//            if (chatController != null)
//                chatController.AppendHistory(
//                    $"<color=orange>System:</color> Possible treasure detected (confidence: {memEntry.credibility:P0}). Needs visual confirmation.");
//            return;
//        }

//        if (currentMode != RobotMode.FIND_TREASURE)
//        {
//            Debug.Log($"<color=yellow>[TREASURE]</color> Mode is {currentMode}. Only logging.");
//            if (chatController != null)
//                chatController.AppendHistory(
//                    $"<color=yellow>System:</color> Treasure spotted (confidence: {memEntry.credibility:P0})! Not chasing 鈥?mode: {currentMode}");
//            return;
//        }

//        // 鍙俊搴﹀楂?+ 鍦ㄥ瀹濇ā寮?鈫?杩斤紒
//        HandleTreasureDiscovery(memEntry.description);
//    }

//    private void HandleTreasureDiscovery(string visualDesc)
//    {
//        Debug.LogWarning($"<color=red>[TREASURE SPOTTED]</color> {visualDesc}");

//        scheduler.StopImmediate();
//        isChasingTreasure = true;
//        lockedTarget = null;
//        arrivalRetryCount = 0;

//        GameObject[] treasures = GameObject.FindGameObjectsWithTag(treasureTag);
//        float minDistance = float.MaxValue;
//        GameObject closestTreasure = null;

//        foreach (GameObject t in treasures)
//        {
//            float dist = Vector3.Distance(transform.position, t.transform.position);
//            NavMeshHit hit;
//            bool reachable = NavMesh.SamplePosition(t.transform.position, out hit, 30f, NavMesh.AllAreas);

//            if (reachable && dist < minDistance)
//            {
//                minDistance = dist;
//                closestTreasure = t;
//                lockedTarget = t.transform;
//            }
//        }

//        if (lockedTarget != null)
//        {
//            Debug.LogWarning($"<color=red>[TREASURE] LOCKED:</color> {closestTreasure.name}, dist={minDistance:F1}m");

//            if (chatController != null)
//                chatController.AppendHistory(
//                    $"<color=yellow>鈽?TREASURE LOCKED!</color> {closestTreasure.name} 鈥?{minDistance:F1}m away!");

//            NavigateToTreasure();

//            _ = AddLongTermMemoryAsync(
//                $"[Credibility:0.95][treasure] LOCKED: {closestTreasure.name}, dist={minDistance:F1}m. Pursuing!",
//                currentLocationName
//            );
//        }
//        else
//        {
//            Vector3 searchDir = EstimateDirectionFromDescription(visualDesc);
//            Vector3 searchPoint = transform.position + searchDir * 10f;

//            NavMeshHit hit;
//            if (NavMesh.SamplePosition(searchPoint, out hit, 15f, 1))
//                scheduler.MoveToLocation(hit.position);

//            currentLocationName = "Treasure_Search_Area";
//            currentGoal = "Moving closer to investigate.";
//            StartCoroutine(RescanAfterDelay(5f));
//        }
//    }

//    private bool TryDirectTreasureSearch()
//    {
//        GameObject[] treasures = GameObject.FindGameObjectsWithTag(treasureTag);
//        if (treasures.Length == 0) return false;

//        float minDistance = float.MaxValue;
//        GameObject closestTreasure = null;

//        foreach (GameObject t in treasures)
//        {
//            float dist = Vector3.Distance(transform.position, t.transform.position);
//            NavMeshHit hit;
//            bool reachable = NavMesh.SamplePosition(t.transform.position, out hit, 30f, NavMesh.AllAreas);

//            if (reachable && dist < minDistance)
//            {
//                minDistance = dist;
//                closestTreasure = t;
//                lockedTarget = t.transform;
//            }
//        }

//        if (lockedTarget != null)
//        {
//            if (chatController != null)
//                chatController.AppendHistory(
//                    $"<color=yellow>鈽?TREASURE FOUND!</color> {closestTreasure.name} 鈥?{minDistance:F1}m away!");

//            isChasingTreasure = true;
//            arrivalRetryCount = 0;
//            scheduler.StopImmediate();
//            NavigateToTreasure();

//            // 鍒涘缓楂樺彲淇″害璁板繂锛堢洿鎺ユ悳绱㈠埌鐨勭墿浣擄級
//            var directMem = new VisualMemoryEntry
//            {
//                id = $"mem_{memoryIdCounter++}",
//                description = $"Direct detection: {closestTreasure.name} at distance {minDistance:F1}m",
//                location = currentLocationName,
//                worldPosition = transform.position,
//                credibility = 0.95f,
//                timestamp = Time.time,
//                confirmationCount = 1,
//                category = "treasure",
//                containsTreasure = true,
//                descriptionQuality = 1f,
//                spatialConsistency = 1f,
//                temporalRelevance = 1f,
//                visualClarity = 1f
//            };
//            AddVisualMemory(directMem);

//            return true;
//        }
//        return false;
//    }

//    private Vector3 EstimateDirectionFromDescription(string desc)
//    {
//        desc = desc.ToLower();
//        if (desc.Contains("left"))
//            return (transform.forward + -transform.right).normalized;
//        if (desc.Contains("right"))
//            return (transform.forward + transform.right).normalized;
//        return transform.forward;
//    }

//    private void NavigateToTreasure()
//    {
//        if (lockedTarget == null)
//        {
//            isChasingTreasure = false;
//            return;
//        }

//        NavMeshHit hit;
//        if (NavMesh.SamplePosition(lockedTarget.position, out hit, 30f, NavMesh.AllAreas))
//        {
//            scheduler.MoveToLocation(hit.position);
//            currentLocationName = "Treasure_Location";
//            currentGoal = "Approaching the yellow cube treasure!";
//        }
//        else
//        {
//            if (chatController != null)
//                chatController.AppendHistory("<color=red>System:</color> Treasure unreachable!");
//            isChasingTreasure = false;
//            lockedTarget = null;
//            currentGoal = "Treasure unreachable. Continue exploring.";
//        }
//    }

//    private System.Collections.IEnumerator RescanAfterDelay(float delay)
//    {
//        yield return new WaitForSeconds(delay);
//        if (isChasingTreasure && lockedTarget == null)
//            _ = PerformVisualScanAsync();
//    }

//    // =====================================================================
//    // --- 3. 澶ц剳鍐崇瓥锛堜娇鐢ㄥ彲淇″害鍔犳潈 RAG锛?---
//    // =====================================================================
//    private async Task PlanNextMoveAsync()
//    {
//        if (isThinking || isChasingTreasure) return;
//        isThinking = true;

//        Debug.Log("<color=magenta>[Think]</color> Planning next move...");

//        try
//        {
//            string historyStr = visitedHistory.Count > 0 ? string.Join(", ", visitedHistory) : "None";

//            // 鈽?浣跨敤鍙俊搴﹀姞鏉冪殑鏈湴璁板繂
//            string localContext = GetCredibilityWeightedContext(
//                currentMode == RobotMode.FIND_TREASURE
//                    ? "treasure yellow cube location"
//                    : $"explored areas landmarks {historyStr}"
//            );

//            // 鍚屾椂浠?ChromaDB 鑾峰彇锛堜綔涓鸿ˉ鍏咃級
//            string ragQuery = currentMode == RobotMode.FIND_TREASURE
//                ? "Where was treasure or yellow cube seen?"
//                : $"Which locations have I visited? Unexplored areas? Recent: {historyStr}";

//            string chromaContext = await memoryClient.RetrieveRelevantMemories(ragQuery, limit: 200);
//            chromaContext = FilterRagContext(chromaContext);

//            // 鍚堝苟涓婁笅鏂囷紙鏈湴鍙俊搴﹁蹇嗕紭鍏堬級
//            string combinedContext = $"=== Trusted Observations (credibility-weighted) ===\n{localContext}\n" +
//                                     $"=== Additional Context ===\n{chromaContext}";

//            string mapInfo = townMap.GetLocationListPrompt();

//            var msgs = new List<LLMClient.Message>
//            {
//                new LLMClient.Message { role = "system", content =
//                    $"You are {robotName}. {personaDescription}\n" +
//                    $"GOAL: {currentGoal}\n" +
//                    $"CONTEXT: Recently Visited: {historyStr} | Known Map Nodes: {mapInfo}\n" +
//                    $"MEMORY (sorted by credibility):\n{combinedContext}\n" +
//                    $"NOTE: [C:0.8] means 80% credibility. Prefer high-credibility memories for decisions.\n" +
//                    $"RULES:\n" +
//                    $"1. Choose an UNVISITED destination from Known Map Nodes.\n" +
//                    $"2. If all known nodes visited, output 'RANDOM_WANDER'.\n" +
//                    $"3. Destination MUST be an EXACT name from Known Map Nodes, or 'RANDOM_WANDER'.\n" +
//                    $"4. OUTPUT JSON ONLY: {{ \"thought\": \"...\", \"destination\": \"...\" }}"
//                },
//                new LLMClient.Message { role = "user", content = $"I am at {currentLocationName}. What is my next move?" }
//            };

//            string response = await llmClient.ChatAsync(msgs, 0.7f);
//            if (!string.IsNullOrEmpty(response))
//                ProcessDecision(response);
//            else
//                FallbackWander();
//        }
//        catch (System.Exception e)
//        {
//            Debug.LogError($"[Think] Error: {e.Message}");
//            FallbackWander();
//        }
//        finally
//        {
//            isThinking = false;
//        }
//    }

//    [System.Serializable]
//    private class DecisionJson { public string thought; public string destination; }

//    private void ProcessDecision(string jsonResponse)
//    {
//        try
//        {
//            jsonResponse = jsonResponse.Replace("```json", "").Replace("```", "").Trim();
//            int s = jsonResponse.IndexOf('{'); int e = jsonResponse.LastIndexOf('}');
//            if (s != -1 && e > s) jsonResponse = jsonResponse.Substring(s, e - s + 1);

//            DecisionJson decision = JsonUtility.FromJson<DecisionJson>(jsonResponse);
//            Debug.Log($"<color=magenta>[Think]</color> Thought: {decision.thought} | Dest: {decision.destination}");

//            if (decision.destination == "RANDOM_WANDER")
//            {
//                FallbackWander();
//                return;
//            }

//            Transform targetTF = townMap.GetWaypointByName(decision.destination)
//                                ?? FindBestMatchingLocation(decision.destination);

//            if (targetTF != null)
//            {
//                currentLocationName = targetTF.name;
//                scheduler.MoveToLocation(targetTF.position);
//                Debug.Log($"<color=green>[Move]</color> Heading to: {targetTF.name}");
//            }
//            else
//            {
//                FallbackWander();
//            }

//            if (!visitedHistory.Contains(currentLocationName))
//            {
//                visitedHistory.Add(currentLocationName);
//                if (visitedHistory.Count > 10) visitedHistory.RemoveAt(0);
//            }
//        }
//        catch (System.Exception ex)
//        {
//            Debug.LogError($"Decision Error: {ex.Message}");
//            FallbackWander();
//        }
//    }

//    private string FilterRagContext(string rawContext)
//    {
//        if (string.IsNullOrEmpty(rawContext)) return "";

//        string[] lines = rawContext.Split('\n');
//        var filteredLines = new List<string>();
//        var seenSummaries = new HashSet<string>();
//        int foundCount = 0;

//        foreach (string line in lines)
//        {
//            string trimmed = line.Trim();
//            if (string.IsNullOrEmpty(trimmed)) continue;

//            if (trimmed.ToLower().Contains("found"))
//            {
//                foundCount++;
//                if (foundCount > 2) continue;
//            }

//            if (trimmed.Contains("System initialized") || trimmed.Contains("ONLINE"))
//                continue;

//            string summary = trimmed.Length > 40 ? trimmed.Substring(0, 40) : trimmed;
//            if (seenSummaries.Contains(summary)) continue;
//            seenSummaries.Add(summary);

//            filteredLines.Add(trimmed);
//        }

//        return string.Join("\n", filteredLines);
//    }

//    private void FallbackWander()
//    {
//        Vector3 point = GetRandomNavMeshPoint();
//        scheduler.MoveToLocation(point);
//        currentLocationName = "Unknown Area";
//        Debug.Log($"<color=blue>[Wander]</color> Random wander to ({point.x:F0}, {point.z:F0})");
//    }

//    private Vector3 GetRandomNavMeshPoint()
//    {
//        Vector3 dir = Random.insideUnitSphere * wanderRadius + transform.position;
//        NavMeshHit hit;
//        if (NavMesh.SamplePosition(dir, out hit, wanderRadius, 1)) return hit.position;
//        return transform.position;
//    }

//    // =====================================================================
//    // --- 鍒拌揪鐩殑鍦?---
//    // =====================================================================
//    private void HandleArrival()
//    {
//        Debug.Log($"<color=green>[Event]</color> Destination Reached: {currentLocationName}");

//        if (isChasingTreasure)
//        {
//            if (lockedTarget != null)
//            {
//                float dist = Vector3.Distance(transform.position, lockedTarget.position);

//                if (dist < treasureStopDistance + 3f)
//                {
//                    Debug.LogWarning($"<color=red>鈽呪槄鈽?TREASURE COLLECTED! 鈽呪槄鈽?/color> dist={dist:F1}m");

//                    if (chatController != null)
//                        chatController.AppendHistory("<color=green>鈽呪槄鈽?TREASURE COLLECTED! 鈽呪槄鈽?/color>");

//                    // 瀛樺叆楂樺彲淇″害璁板繂
//                    var collectMem = new VisualMemoryEntry
//                    {
//                        id = $"mem_{memoryIdCounter++}",
//                        description = $"SUCCESS! Collected treasure at ({lockedTarget.position.x:F0}, {lockedTarget.position.z:F0})",
//                        location = "Treasure_Location",
//                        worldPosition = lockedTarget.position,
//                        credibility = 1.0f,
//                        timestamp = Time.time,
//                        confirmationCount = 5,
//                        category = "treasure",
//                        containsTreasure = true,
//                        descriptionQuality = 1f,
//                        spatialConsistency = 1f,
//                        temporalRelevance = 1f,
//                        visualClarity = 1f
//                    };
//                    AddVisualMemory(collectMem);
//                    _ = AddLongTermMemoryAsync(collectMem.description, "Treasure_Location");

//                    isChasingTreasure = false;
//                    lockedTarget = null;
//                    arrivalRetryCount = 0;
//                    currentMode = RobotMode.EXPLORE;
//                    currentGoal = "Treasure collected! Continue exploring.";
//                }
//                else
//                {
//                    arrivalRetryCount++;
//                    if (arrivalRetryCount >= MAX_ARRIVAL_RETRIES)
//                    {
//                        if (chatController != null)
//                            chatController.AppendHistory("<color=orange>System:</color> Cannot reach treasure. Resuming.");
//                        isChasingTreasure = false;
//                        lockedTarget = null;
//                        arrivalRetryCount = 0;
//                        currentMode = RobotMode.EXPLORE;
//                        currentGoal = "Treasure unreachable. Continue exploring.";
//                    }
//                    else
//                    {
//                        NavigateToTreasure();
//                    }
//                }
//            }
//            else
//            {
//                isChasingTreasure = false;
//                arrivalRetryCount = 0;
//                _ = PerformVisualScanAsync();
//            }
//            return;
//        }

//        arrivalRetryCount = 0;
//        if (!isScanning && !isVisionBusy)
//            _ = PerformVisualScanAsync();
//    }

//    private Transform FindBestMatchingLocation(string rawTarget)
//    {
//        if (string.IsNullOrEmpty(rawTarget)) return null;
//        rawTarget = rawTarget.ToLower().Trim();

//        foreach (Transform t in townMap.transform)
//            if (t.name.ToLower() == rawTarget) return t;
//        foreach (Transform t in townMap.transform)
//            if (t.name.ToLower().Contains(rawTarget) || rawTarget.Contains(t.name.ToLower())) return t;
//        return null;
//    }

//    // =====================================================================
//    // --- 4. 浜や簰瀵硅瘽绯荤粺锛堜娇鐢ㄥ彲淇″害璁板繂锛?---
//    // =====================================================================

//    [System.Serializable]
//    private class InteractionIntent
//    {
//        public string intent;
//        public string target_location;
//        public string reply;
//    }

//    public async void OnUserInteract(string userText)
//    {
//        Debug.Log($"[User Input] {userText}");

//        await AddLongTermMemoryAsync($"User asked: \"{userText}\"", currentLocationName);

//        // 鈽?浣跨敤鍙俊搴﹀姞鏉?RAG
//        string localContext = GetCredibilityWeightedContext(userText, 200);

//        string chromaContext = await memoryClient.RetrieveRelevantMemories(userText, limit: 200);
//        chromaContext = FilterRagContext(chromaContext);

//        string combinedContext = !string.IsNullOrEmpty(localContext)
//            ? $"=== Trusted Memories ===\n{localContext}\n=== Other ===\n{chromaContext}"
//            : chromaContext;

//        string mapInfo = townMap.GetLocationListPrompt();

//        // 鈽?璁板繂鍙俊搴︾粺璁★紝渚?LLM 鍙傝€?
//        float avgCred = visualMemories.Count > 0
//            ? visualMemories.Average(m => m.GetEffectiveCredibility(Time.time, decayRatePerMinute))
//            : 0f;
//        int totalMem = visualMemories.Count;
//        int trustedMem = visualMemories.Count(m => m.GetEffectiveCredibility(Time.time, decayRatePerMinute) > 0.6f);

//        var msgs = new List<LLMClient.Message>
//        {
//            new LLMClient.Message { role = "system", content =
//                $"You are {robotName}, a smart AI robot dog. {personaDescription}\n" +
//                $"CURRENT MODE: {currentMode}\n" +
//                $"KNOWN LOCATIONS: {mapInfo}\n" +
//                $"MEMORY STATUS: {totalMem} total memories, {trustedMem} trusted (>60% credibility), avg credibility: {avgCred:P0}\n" +
//                $"YOUR MEMORIES:\n{combinedContext}\n\n" +
//                $"INSTRUCTIONS:\n" +
//                $"1. Determine intent: 'GO_TO_LOCATION', 'FIND_TREASURE', 'EXPLORE', or 'CHAT'.\n" +
//                $"2. If 'GO_TO_LOCATION', set 'target_location' from KNOWN LOCATIONS.\n" +
//                $"3. When replying, mention memory credibility if relevant (e.g., 'I'm fairly confident I saw...').\n" +
//                $"4. OUTPUT JSON:\n" +
//                $"{{\n  \"intent\": \"...\",\n  \"target_location\": \"...\",\n  \"reply\": \"...\"\n}}"
//            },
//            new LLMClient.Message { role = "user", content = userText }
//        };

//        try
//        {
//            string json = await llmClient.ChatAsync(msgs, 0.3f);

//            json = json.Replace("```json", "").Replace("```", "").Trim();
//            int s = json.IndexOf('{'); int e = json.LastIndexOf('}');
//            if (s != -1 && e > s) json = json.Substring(s, e - s + 1);

//            var settings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore };
//            InteractionIntent parsedIntent = JsonConvert.DeserializeObject<InteractionIntent>(json, settings);
//            if (parsedIntent == null) return;

//            await AddLongTermMemoryAsync($"I replied: {parsedIntent.reply}", currentLocationName);
//            if (chatController != null)
//                chatController.AppendHistory($"<color=cyan>{robotName}:</color> {parsedIntent.reply}");

//            Debug.Log($"<color=magenta>[Intent]</color> {parsedIntent.intent}");

//            switch (parsedIntent.intent)
//            {
//                case "FIND_TREASURE":
//                    currentMode = RobotMode.FIND_TREASURE;
//                    currentGoal = "Search for the yellow cube treasure!";
//                    isChasingTreasure = false;
//                    lockedTarget = null;

//                    if (chatController != null)
//                        chatController.AppendHistory("<color=yellow>Mode 鈫?FIND_TREASURE</color>");

//                    bool foundDirectly = TryDirectTreasureSearch();
//                    if (!foundDirectly)
//                    {
//                        _ = PerformVisualScanAsync();
//                        RestartThinking();
//                    }
//                    break;

//                case "EXPLORE":
//                    currentMode = RobotMode.EXPLORE;
//                    currentGoal = "Autonomously explore the unknown areas.";
//                    isChasingTreasure = false;
//                    lockedTarget = null;

//                    if (chatController != null)
//                        chatController.AppendHistory("<color=green>Mode 鈫?EXPLORE</color> (treasure tracking off)");

//                    RestartThinking();
//                    break;

//                case "GO_TO_LOCATION":
//                    Transform targetTF = townMap.GetWaypointByName(parsedIntent.target_location)
//                                        ?? FindBestMatchingLocation(parsedIntent.target_location);
//                    if (targetTF != null)
//                    {
//                        currentMode = RobotMode.GO_TO_LOCATION;
//                        currentGoal = $"USER ORDER: Go to {targetTF.name}";
//                        isChasingTreasure = false;
//                        lockedTarget = null;
//                        scheduler.StopImmediate();
//                        currentLocationName = targetTF.name;
//                        scheduler.MoveToLocation(targetTF.position);
//                        RestartThinking();
//                    }
//                    else
//                    {
//                        if (chatController != null)
//                            chatController.AppendHistory($"<color=red>System:</color> Cannot find '{parsedIntent.target_location}'.");
//                    }
//                    break;

//                case "CHAT":
//                default:
//                    break;
//            }
//        }
//        catch (System.Exception ex)
//        {
//            Debug.LogError($"[Interact Error] {ex.Message}");
//        }
//    }

//    private void RestartThinking()
//    {
//        scheduler.StopImmediate();
//        isThinking = false;
//        isWaitingForLLM = false;
//        StopCoroutine(ThinkAndActRoutine());
//        StartCoroutine(ThinkAndActRoutine());
//    }
//}
using System;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;

public class RobotAgent_VRAG : MonoBehaviour
{
    [Header("Core Components")]
    public TownMap townMap;
    public RouteScheduler scheduler;
    public RobotCamera robotEyes;

    [Header("AI Services")]
    public LLMClient llmClient;
    public VisionClient visionClient;
    public MemoryClient memoryClient;

    [Header("Persona & Settings")]
    public string robotName = "G1-Explorer";
    [TextArea]
    public string personaDescription =
        "You are an autonomous explorer robot with a credibility-aware memory system. " +
        "You evaluate the reliability of your visual observations and make decisions based on trusted memories.";

    public float wanderRadius = 15.0f;
    public float scanInterval = 25.0f;

    [Header("Treasure Detection")]
    public string treasureTag = "Treasure";
    public float treasureStopDistance = 2.0f;
    [Tooltip("Formal trials score collection only at physical contact, never at the 5 m query radius.")]
    [Min(0.05f)] public float formalCollectionContactDistance = 0.35f;
    public LayerMask formalCollectionLineOfSightMask = ~0;

    [Header("Memory Credibility Settings")]
    [Tooltip("Minimum credibility used by the heuristic baseline.")]
    [Range(0f, 1f)] public float credibilityThreshold = 0.3f;
    [Tooltip("Exponential credibility decay rate per minute.")]
    [Range(0f, 0.1f)] public float decayRatePerMinute = 0.02f;
    [Tooltip("Credibility boost after an independent confirmation.")]
    [Range(0f, 0.5f)] public float confirmationBoost = 0.15f;

    [Header("UI Integration")]
    public RoboChatController_Vrag chatController;

    [Header("Multi-Target Identification")]
    public TreasureIdentifier treasureIdentifier;

    [Header("Experiment Instrumentation")]
    public EmbodiedRagExperiment experimentRecorder;
    public ReliabilityCalibrator reliabilityCalibrator;
    public ExperimentReplayRecorder replayRecorder;
    public EmbodiedRagAttackController attackController;
    public bool experimentControlledStart = false;
    [Range(1, 50)] public int retrievalTopK = 8;
    [Range(0f, 1f)] public float retrievalTrustWeight = 0.35f;
    [Range(0f, 1f)] public float retrievalRecencyWeight = 0.10f;
    [Range(0f, 1f)] public float retrievalSpatialWeight = 0.10f;
    public float verificationTimeoutSeconds = 25f;

    private EmbodiedRagExperiment ExperimentRecorder
    {
        get { return experimentRecorder != null ? experimentRecorder : EmbodiedRagExperiment.Active; }
    }

    public bool IsAutonomyRunning { get { return autonomyRunning; } }
    private bool autonomyRunning;
    private bool lifecycleInitialized;
    private int runEpoch;
    private string injectedTaskId = "";
    private string activeNavigationDecisionId = "";
    private Coroutine thinkingCoroutine, scanningCoroutine;
    private Coroutine maintenanceCoroutine, proximityCoroutine;
    private bool ExperimentNoOracle
    {
        get
        {
            EmbodiedRagExperiment recorder = ExperimentRecorder;
            return recorder != null && recorder.IsRunning && recorder.enforceNoOracleLookup;
        }
    }

    public EmbodiedRagMethodCondition ActiveMethod
    {
        get
        {
            EmbodiedRagExperiment recorder = ExperimentRecorder;
            return recorder != null
                ? recorder.methodCondition
                : EmbodiedRagMethodCondition.C_HeuristicTrust;
        }
    }

    private bool UsesLongTermMemory { get { return ActiveMethod.UsesLongTermMemory(); } }
    private bool UsesReliabilityAwareMethod { get { return ActiveMethod.UsesCalibratedReliability(); } }

    [Header("Hide and Seek")]
    public float seekerDetectionRadius = 8f;  // 鍙戠幇 Hider 鐨勮窛绂?
    private Transform hiderTransform;
    private bool hiderFound = false;

    // 鈽?娓告垙鐘舵€?
    [Header("Game State")]
    public int totalTreasures = 3;           // 鍦烘櫙涓湡瀹濈墿鏁伴噺
    private int collectedTreasures = 0;
    private HashSet<string> collectedTreasureNames = new HashSet<string>();
    private float gameStartTime;

    

    // =====================================================================
    // --- 璁板繂鍙俊搴︾郴缁?---
    // =====================================================================

    /// <summary>
    /// 甯﹀彲淇″害璇勫垎鐨勮瑙夎蹇嗘潯鐩?
    /// </summary>
    [System.Serializable]
    public class VisualMemoryEntry
    {
        public string id;                   // 鍞竴ID
        public string description;          // 瑙嗚鎻忚堪
        public string location;             // 瑙傚療浣嶇疆
        public Vector3 worldPosition;       // 涓栫晫鍧愭爣
        public float credibility;           // 鍙俊搴?0-1
        public float rawCredibility;
        public int contradictionCount;
        public string status = "accepted";
        public string backendId;
        public string observationId, frameId, claimId;
        public string sourceModel;
        public Vector3 lastObservationPosition;
        public Vector3 lastObservationForward;
        public float lastObservationTime;        public float timestamp;             // 璁板綍鏃堕棿 (Time.time)
        public int confirmationCount;       // 琚‘璁ゆ鏁?
        public string category;             // 鍒嗙被: "landmark", "treasure", "path", "object", "ambient"
        public bool containsTreasure;       // 鏄惁鍖呭惈瀹濈墿淇℃伅

        // 鍙俊搴﹁瘎浼扮殑瀛愮淮搴?
        public float descriptionQuality;    // 鎻忚堪璐ㄩ噺 0-1
        public float spatialConsistency;    // 绌洪棿涓€鑷存€?0-1
        public float temporalRelevance;     // 鏃堕棿鐩稿叧鎬?0-1
        public float visualClarity;         // 瑙嗚娓呮櫚搴?0-1

        /// <summary>
        /// 璁＄畻褰撳墠鏃跺埢鐨勬湁鏁堝彲淇″害锛堣€冭檻鏃堕棿琛板噺锛?
        /// </summary>
        public float GetEffectiveCredibility(float currentTime, float decayRate)
        {
            float minutesElapsed = (currentTime - timestamp) / 60f;
            float decay = Mathf.Exp(-decayRate * minutesElapsed);
            float confirmBonus = Mathf.Min(confirmationCount * 0.1f, 0.3f);
            return Mathf.Clamp01((credibility + confirmBonus) * decay);
        }

        public override string ToString()
        {
            return $"[C:{credibility:F2}|x{confirmationCount}] {description} @{location}";
        }
    }

    // 鏈湴璁板繂瀛樺偍锛堝甫鍙俊搴︼級
    private List<VisualMemoryEntry> visualMemories = new List<VisualMemoryEntry>();
    private const int MAX_MEMORIES = 100;
    private int memoryIdCounter = 0;

    // --- 杩愯妯″紡 ---
    private enum RobotMode
    {
        EXPLORE,
        FIND_TREASURE,
        GO_TO_LOCATION,
        SEEK_HIDER,       // 鈽?鏂板锛氬鎵捐翰钘忚€?
        INVESTIGATE,       // 鈽?鏂板锛氳皟鏌ュ彲鐤戠墿浣?
        IDLE
    }

    private RobotMode currentMode = RobotMode.EXPLORE;

    // --- 鐘舵€佸彉閲?---
    private bool isThinking = false;
    private bool isScanning = false;
    private bool isVisionBusy = false;
    private bool isWaitingForLLM = false;
    private string currentLocationName = "StartPoint";
    private string currentGoal = "Autonomously explore the unknown areas.";

    private List<string> visitedHistory = new List<string>();
    private Transform lockedTarget = null;
    private bool isChasingTreasure = false;
    private int arrivalRetryCount = 0;
    private const int MAX_ARRIVAL_RETRIES = 3;

    private string lastVisionDescription = "";
    private int duplicateVisionCount = 0;
    private VisualMemoryEntry lastConfirmedMemory;
    private bool lastConfirmationIndependent;
    private VisualMemoryEntry pendingVerificationMemory;
    private RobotMode modeBeforeVerification = RobotMode.FIND_TREASURE;
    private readonly HashSet<string> lastPresentedEvidenceIds = new HashSet<string>();

    // Vision Prompt
    private const string VISION_PROMPT =
        "Return JSON only: {\"schema_version\":1,\"scene_description\":\"...\",\"claims\":[{" +
        "\"claim_id\":\"c0\",\"predicted_label\":\"treasure|decoy|unknown\",\"color\":\"...\"," +
        "\"shape\":\"...\",\"confidence\":0.0,\"bbox_xmin\":0.0,\"bbox_ymin\":0.0," +
        "\"bbox_xmax\":1.0,\"bbox_ymax\":1.0}]}. Create one claim for every treasure-like object. " +
        "A bright yellow cube is treasure; wrong-color or wrong-shape lookalikes are decoys. " +
        "Use normalized xyxy coordinates with top-left origin and unique IDs c0,c1,... " +
        "If no candidate exists use claims:[]; never invent a box and never output markdown.";

    // =====================================================================
    // --- 鍒濆鍖?---
    // =====================================================================
    void Start()
    {
        InitializeLifecycle();
        if (!experimentControlledStart)
        {
            InjectExperimentTask("legacy_interactive_task", currentGoal);
            BeginAutonomy();
        }
    }

    private void InitializeLifecycle()
    {
        if (lifecycleInitialized) return;
        lifecycleInitialized = true;
        if (scheduler != null) scheduler.OnDestinationReached += HandleArrival;
        GameObject hiderObj = FindGameObjectWithTagSafe("Hider");
        if (hiderObj != null) hiderTransform = hiderObj.transform;
        if (reliabilityCalibrator == null)
            reliabilityCalibrator = FindObjectOfType<ReliabilityCalibrator>();
        if (replayRecorder == null) replayRecorder = FindObjectOfType<ExperimentReplayRecorder>();
        if (attackController == null) attackController = FindObjectOfType<EmbodiedRagAttackController>();
        if (visionClient != null && ExperimentRecorder != null)
            visionClient.seed = ExperimentRecorder.randomSeed;
    }

    public bool InjectExperimentTask(string taskId, string prompt)
    {
        InitializeLifecycle();
        if (autonomyRunning || string.IsNullOrWhiteSpace(taskId) || string.IsNullOrWhiteSpace(prompt))
            return false;
        injectedTaskId = taskId.Trim();
        currentGoal = prompt.Trim();
        currentMode = RobotMode.FIND_TREASURE;
        isChasingTreasure = false;
        lockedTarget = null;
        visualMemories.Clear();
        visitedHistory.Clear();
        pendingVerificationMemory = null;
        lastConfirmedMemory = null;
        lastPresentedEvidenceIds.Clear();
        memoryIdCounter = 0;
        return true;
    }

    public bool BeginAutonomy()
    {
        InitializeLifecycle();
        if (autonomyRunning) return false;
        if (experimentControlledStart && (ExperimentRecorder == null || !ExperimentRecorder.IsRunning))
            return false;
        autonomyRunning = true;
        runEpoch++;
        gameStartTime = Time.time;
        thinkingCoroutine = StartCoroutine(ThinkAndActRoutine());
        scanningCoroutine = StartCoroutine(ActiveScanningRoutine());
        maintenanceCoroutine = StartCoroutine(MemoryMaintenanceRoutine());
        proximityCoroutine = StartCoroutine(ProximityCheckRoutine());
        return true;
    }

    public void StopAutonomy(string reason)
    {
        autonomyRunning = false;
        runEpoch++;
        StopAllCoroutines();
        if (scheduler != null) scheduler.StopImmediate();
        isThinking = false;
        isScanning = false;
        isVisionBusy = false;
        isWaitingForLLM = false;
        activeNavigationDecisionId = "";
        Debug.Log("[Experiment Agent] Autonomy stopped: " + reason, this);
    }

    private bool CanAct(int epoch)
    {
        if (!autonomyRunning || epoch != runEpoch) return false;
        return !experimentControlledStart ||
            (ExperimentRecorder != null && ExperimentRecorder.IsRunning);
    }

    private bool CanActNow()
    {
        return CanAct(runEpoch);
    }

    private void OnDestroy()
    {
        if (scheduler != null) scheduler.OnDestinationReached -= HandleArrival;
    }

    private void AddBootstrapMemory()
    {
        var bootMemory = new VisualMemoryEntry
        {
            id = $"mem_{memoryIdCounter++}",
            description = "System boot. Starting at spawn point. No observations yet.",
            location = "StartPoint",
            worldPosition = transform.position,
            credibility = 1.0f,
            timestamp = Time.time,
            confirmationCount = 0,
            category = "system",
            containsTreasure = false,
            descriptionQuality = 1f,
            spatialConsistency = 1f,
            temporalRelevance = 1f,
            visualClarity = 1f
        };
        visualMemories.Add(bootMemory);
    }

    private async Task AddLongTermMemoryAsync(string content, string location)
    {
        if (!UsesLongTermMemory || memoryClient == null || string.IsNullOrEmpty(content)) return;
        await memoryClient.AddMemoryAsync(content, location);
    }

    private static GameObject FindGameObjectWithTagSafe(string tagName)
    {
        try { return GameObject.FindGameObjectWithTag(tagName); }
        catch (UnityException) { return null; }
    }

    private static bool ColliderHasTagSafe(Collider collider, string tagName)
    {
        try { return collider != null && collider.CompareTag(tagName); }
        catch (UnityException) { return false; }
    }

    private System.Collections.IEnumerator ProximityCheckRoutine()
    {
        while (autonomyRunning)
        {
            yield return new WaitForSeconds(1f);

            // 妫€娴?Hider
            if (!hiderFound && hiderTransform != null)
            {
                float distToHider = Vector3.Distance(transform.position, hiderTransform.position);
                if (distToHider < seekerDetectionRadius)
                {
                    // 瑙嗙嚎妫€娴?
                    Vector3 dirToHider = (hiderTransform.position - transform.position).normalized;
                    if (!Physics.Linecast(transform.position + Vector3.up, hiderTransform.position + Vector3.up))
                    {
                        OnHiderDiscovered();
                    }
                }
            }

            // 妫€娴嬮檮杩戠殑瀹濈墿/鍋囧疂鐗?
            CheckNearbyObjects();
        }
    }


    // =====================================================================
    // --- 1. 涓绘€濊€冨惊鐜?---
    // =====================================================================
    private System.Collections.IEnumerator ThinkAndActRoutine()
    {
        while (autonomyRunning)
        {
            if (!scheduler.IsMoving && !isThinking && !isWaitingForLLM && !isChasingTreasure)
            {
                Debug.Log("<color=magenta>[Think]</color> Will plan in 3 seconds...");
                isWaitingForLLM = true;
                yield return new WaitForSeconds(3.0f);
                PlanNextMoveWrapper();
            }
            yield return new WaitForSeconds(5.0f);
        }
    }

    private async void PlanNextMoveWrapper()
    {
        if (isThinking)
        {
            isWaitingForLLM = false;
            return;
        }
        try
        {
            await PlanNextMoveAsync();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Think] Error: {e.Message}");
        }
        finally
        {
            isThinking = false;
            isWaitingForLLM = false;
            if (!scheduler.IsMoving && !isChasingTreasure)
            {
                Debug.LogWarning("[Think] Still not moving. Force wandering.");
                FallbackWander();
            }
        }
    }
    /// <summary>
    /// 鍙戠幇 Hider
    /// </summary>
    private void OnHiderDiscovered()
    {
        if (hiderFound) return;
        hiderFound = true;
        if (ExperimentRecorder != null) ExperimentRecorder.OnHiderFound();

        Debug.LogWarning("<color=red>鈽呪槄鈽?HIDER FOUND! 鈽呪槄鈽?/color>");

        if (chatController != null)
            chatController.AppendHistory("<color=green>鈽呪槄鈽?Found the Hider! 鈽呪槄鈽?/color>");

        _ = AddLongTermMemoryAsync(
        "[Credibility:1.0] FOUND THE HIDER! Discovered the hiding robot.", currentLocationName
        );

        // 閫氱煡 Hider
        HiderAgent hider = hiderTransform.GetComponent<HiderAgent>();
        if (hider != null) hider.OnDiscovered();

        CheckGameCompletion();
    }

    /// <summary>
    /// 鈽?妫€娴嬮檮杩戠墿浣撳苟杩涜鐪熶吉鍒ゆ柇
    /// </summary>
    private void CheckNearbyObjects()
    {
        float checkRadius = 5f;
        Collider[] nearby = Physics.OverlapSphere(transform.position, checkRadius);

        foreach (Collider col in nearby)
        {
            if (ColliderHasTagSafe(col, "Treasure") && !collectedTreasureNames.Contains(col.name))
            {
                // 鐪熷疂鐗?鈥?鏀堕泦
                if (!ExperimentNoOracle || IsFormalCollectionContact(col))
                    CollectTreasure(col.gameObject);
            }
            else if (ColliderHasTagSafe(col, "Decoy"))
            {
                // 璧拌繎浜嗗亣瀹濈墿 鈥?璁板綍杩欐槸鍋囩殑
                IdentifyDecoy(col.gameObject);
            }
        }
    }

    private bool IsFormalCollectionContact(Collider targetCollider)
    {
        if (targetCollider == null) return false;
        Vector3 closest = targetCollider.ClosestPoint(transform.position);
        if (Vector3.Distance(transform.position, closest) > formalCollectionContactDistance)
            return false;

        Transform cameraTransform = robotEyes != null && robotEyes.visionCamera != null
            ? robotEyes.visionCamera.transform : transform;
        Vector3 direction = targetCollider.bounds.center - cameraTransform.position;
        float distance = direction.magnitude;
        if (distance <= 0.001f) return true;
        RaycastHit[] hits = Physics.RaycastAll(cameraTransform.position, direction.normalized,
            distance + 0.1f, formalCollectionLineOfSightMask, QueryTriggerInteraction.Collide);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
            Transform target = targetCollider.transform;
            return hit.collider == targetCollider || hit.transform == target ||
                hit.transform.IsChildOf(target) || target.IsChildOf(hit.transform);
        }
        return false;
    }

    private void CollectTreasure(GameObject treasure)
    {
        if (collectedTreasureNames.Contains(treasure.name)) return;
        bool evaluationOnly = ExperimentNoOracle;

        collectedTreasures++;
        collectedTreasureNames.Add(treasure.name);
        if (ExperimentRecorder != null) ExperimentRecorder.OnTreasureCollected();

        Debug.LogWarning($"<color=green>鈽呪槄鈽?TREASURE {collectedTreasures}/{totalTreasures} COLLECTED! 鈽呪槄鈽?/color> {treasure.name}");

        if (!evaluationOnly)
        {
            if (chatController != null)
                chatController.AppendHistory($"<color=green>鈽?Treasure {collectedTreasures}/{totalTreasures}: {treasure.name}</color>");
            _ = AddLongTermMemoryAsync(
                $"[Credibility:1.0] Collected real treasure #{collectedTreasures}: {treasure.name} at {treasure.transform.position}",
                currentLocationName);
        }
        else if (ExperimentRecorder != null)
        {
            ExperimentRecorder.RecordProtocolEvent("evaluation_only_contact", treasure.name);
        }

        // 鍙互閫夋嫨閿€姣佹垨绂佺敤瀹濈墿
        treasure.SetActive(false);

        isChasingTreasure = false;
        lockedTarget = null;

        CheckGameCompletion();
    }

    private void IdentifyDecoy(GameObject decoy)
    {
        // In an experiment, a Unity tag may score behavior but must not teach the policy.
        if (ExperimentNoOracle) return;

        string decoyName = decoy.name;

        _ = AddLongTermMemoryAsync(
    $"[Credibility:0.9] DECOY IDENTIFIED: {decoyName} is a fake treasure! It is {GetDecoyDescription(decoy)}.",
    currentLocationName
);

        Debug.Log($"<color=orange>[Decoy]</color> Identified fake: {decoyName}");

        if (chatController != null)
            chatController.AppendHistory($"<color=orange>鈿?Decoy identified: {decoyName}</color>");
    }

    private string GetDecoyDescription(GameObject obj)
    {
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            Color color = renderer.material.color;
            string colorName = GetColorName(color);

            MeshFilter mesh = obj.GetComponent<MeshFilter>();
            string shapeName = mesh != null ? mesh.sharedMesh.name : "unknown shape";

            return $"a {colorName} {shapeName}";
        }
        return "an unidentifiable object";
    }

    private string GetColorName(Color color)
    {
        if (color.r > 0.8f && color.g > 0.8f && color.b < 0.3f) return "yellow";
        if (color.r > 0.8f && color.g > 0.4f && color.b < 0.3f) return "orange";
        if (color.r > 0.6f && color.g > 0.3f && color.b < 0.2f) return "brown";
        if (color.r > 0.8f && color.g < 0.3f && color.b < 0.3f) return "red";
        return "unknown color";
    }

    /// <summary>
    /// 娓告垙瀹屾垚妫€娴?
    /// </summary>
    private void CheckGameCompletion()
    {
        bool allTreasures = collectedTreasures >= totalTreasures;
        bool foundHider = this.hiderFound;

        EmbodiedRagExperiment recorder = ExperimentRecorder;
        if (allTreasures && recorder != null && recorder.IsRunning &&
            (!recorder.requireHiderForCompletion || foundHider))
        {
            recorder.CompleteExperiment(foundHider ? "all_objectives" : "treasures_complete");
        }

        if (allTreasures && foundHider)
        {
            float elapsed = Time.time - gameStartTime;

            Debug.LogWarning($"<color=green>鈽呪槄鈽呪槄鈽?GAME COMPLETE! 鈽呪槄鈽呪槄鈽?/color> " +
                             $"Time: {elapsed:F0}s | Treasures: {collectedTreasures}/{totalTreasures}");

            if (chatController != null)
                chatController.AppendHistory(
                    $"<color=green>鈽呪槄鈽?GAME COMPLETE! 鈽呪槄鈽?/color>\n" +
                    $"Time: {elapsed:F0}s | Treasures: {collectedTreasures}/{totalTreasures} | Hider: Found!");

            currentMode = RobotMode.IDLE;
            currentGoal = "All objectives complete!";
        }
        else
        {
            string status = $"Treasures: {collectedTreasures}/{totalTreasures}, Hider: {(foundHider ? "Found" : "Missing")}";
            Debug.Log($"<color=yellow>[Game]</color> {status}");
        }
    }

    // 鈽?淇敼瑙嗚鎵弿锛岄泦鎴?TreasureIdentifier
    // 鍦?PerformVisualScanAsync 涓紝褰撳彂鐜板彲鐤戠墿浣撴椂锛?
    private void ProcessVisionForTreasureIdentification(
        string description, float credibility)
    {
        if (treasureIdentifier == null) return;

        // 鍙鍖呭惈娼滃湪瀹濈墿鐗瑰緛鐨勬弿杩拌繘琛屽垎鏋?
        string lower = description.ToLower();
        bool hasInterestingObject =
            lower.Contains("yellow") || lower.Contains("cube") || lower.Contains("box") ||
            lower.Contains("orange") || lower.Contains("block") || lower.Contains("sphere") ||
            lower.Contains("found");

        if (!hasInterestingObject) return;

        var result = treasureIdentifier.AnalyzeObservation(
            description, credibility, transform.position, transform.forward
        );

        switch (result.classification)
        {
            case "treasure":
                Debug.Log($"<color=green>[ID]</color> Confirmed treasure! Score: {result.identificationScore:F2}");
                if (currentMode == RobotMode.FIND_TREASURE)
                    HandleTreasureDiscovery(description);
                break;

            case "suspicious":
            case "likely_treasure":
                Debug.Log($"<color=yellow>[ID]</color> Suspicious object. Need more observations.");
                // 闈犺繎璋冩煡
                if (currentMode == RobotMode.FIND_TREASURE)
                {
                    currentGoal = "Investigate a suspicious object that might be treasure.";
                    Vector3 investigatePos = result.approximatePosition;
                    NavMeshHit hit;
                    if (NavMesh.SamplePosition(investigatePos, out hit, 15f, NavMesh.AllAreas))
                    {
                        scheduler.MoveToLocation(hit.position);
                    }
                }
                break;

            case "decoy_or_furniture":
                Debug.Log($"<color=grey>[ID]</color> Identified as decoy/furniture. Ignoring.");
                _ = AddLongTermMemoryAsync(
                $"[Credibility:0.8] Object analyzed and classified as DECOY/FURNITURE. Not a real treasure. Desc: {description}",
                currentLocationName
                );
                break;
        }
    }

    // =====================================================================
    // --- 2. 瑙嗚鎵弿寰幆 ---
    // =====================================================================
    private System.Collections.IEnumerator ActiveScanningRoutine()
    {
        yield return new WaitForSeconds(5.0f);

        while (autonomyRunning)
        {
            if (!isScanning && !isVisionBusy)
            {
                Debug.Log("<color=yellow>[Eye]</color> Triggering visual scan...");
                _ = PerformVisualScanAsync();
            }
            yield return new WaitForSeconds(scanInterval);
        }
    }

    private async Task PerformVisualScanAsync()
    {
        await PerformVisualScanCoreAsync(false, "", "autonomous");
    }

    /// <summary>
    /// Captures one formal observation while all autonomous action loops remain stopped.
    /// The observation still follows the assigned A/B/C/D memory policy, but it cannot
    /// dispatch navigation, treasure pursuit, or active-verification movement.
    /// </summary>
    public Task<bool> CaptureControlledObservationAsync(string observationId, string phase)
    {
        InitializeLifecycle();
        if (!experimentControlledStart || autonomyRunning || ExperimentRecorder == null ||
            !ExperimentRecorder.IsRunning)
            return Task.FromResult(false);
        return PerformVisualScanCoreAsync(true, observationId, phase);
    }

    private async Task<bool> PerformVisualScanCoreAsync(bool controlled,
        string requestedObservationId, string phase)
    {
        bool schemaValidObservation = false;
        string scanId = "";
        ExperimentGroundTruthSensor.Snapshot frameTruth = null;
        if ((!controlled && !CanActNow()) || isScanning || isVisionBusy) return false;
        int epoch = runEpoch;
        isScanning = true;
        isVisionBusy = true;

        try
        {
            string base64Image = robotEyes.CaptureBase64Image();
            if (string.IsNullOrEmpty(base64Image)) return false;

            scanId = !string.IsNullOrWhiteSpace(requestedObservationId)
                ? requestedObservationId.Trim()
                : $"scan_{Time.frameCount}_{System.DateTime.UtcNow.Ticks}";
            frameTruth = ExperimentRecorder != null
                ? ExperimentRecorder.CaptureGroundTruthSnapshot(scanId)
                : null;
            if (frameTruth != null && robotEyes != null)
            { frameTruth.image_width = robotEyes.resolutionWidth; frameTruth.image_height = robotEyes.resolutionHeight; }

            if (controlled && attackController != null)
            {
                string frameError;
                if (!attackController.ValidateControlledPreludeFrame(frameTruth, out frameError))
                {
                    if (ExperimentRecorder != null)
                        ExperimentRecorder.RecordProtocolEvent("controlled_frame_rejected", scanId + ":" + frameError);
                    return false;
                }
            }
            if (base64Image.Contains(",")) base64Image = base64Image.Split(',')[1];

            int imageSizeKB = base64Image.Length * 3 / 4 / 1024;
            Debug.Log($"<color=yellow>[Eye]</color> Image: {imageSizeKB}KB. Sending...");

            if (ExperimentRecorder != null) ExperimentRecorder.OnVLMCall();
            string desc = await visionClient.AnalyzeImageAsync(base64Image, VISION_PROMPT);
            if (!controlled && !CanAct(epoch)) return false;
            if (controlled && (ExperimentRecorder == null || !ExperimentRecorder.IsRunning))
                return false;
            string rawVisionResponse = desc;
            if (replayRecorder != null)
                replayRecorder.RecordVisionFrame(scanId, base64Image, VISION_PROMPT,
                    rawVisionResponse, frameTruth, visionClient);
            int descriptionLength = string.IsNullOrEmpty(desc) ? 0 : desc.Length;
            Debug.Log($"<color=yellow>[Eye]</color> Vision ({descriptionLength} chars): {desc}");

            bool invalidResponse = string.IsNullOrEmpty(desc) || desc.Length < 10 ||
                desc.Contains("Unable") || desc.Contains("Error") || desc.Contains("offline");
            ExperimentVisionAnalysis analysis = null;
            string schemaError = "";
            if (!invalidResponse && !ExperimentVisionProtocol.TryParse(desc, out analysis, out schemaError))
                invalidResponse = true;
            else if (invalidResponse) { analysis = null; schemaError = "invalid_transport_response"; }
            if (invalidResponse)
            {
                if (ExperimentRecorder != null)
                    ExperimentRecorder.RecordObservation(
                        scanId, desc, -1f, "invalid_response:" + schemaError, false,
                        currentLocationName, transform.position, false, frameTruth);
                return false;
            }

            schemaValidObservation = true;
            if (controlled && ExperimentRecorder != null)
                ExperimentRecorder.RecordProtocolEvent("controlled_observation",
                    (phase ?? "controlled") + ":" + scanId);

            bool scanClaimsTreasure = analysis.claims.Any(value => value != null && value.IsTreasureClaim);
            EmbodiedRagExperiment recorder = ExperimentRecorder;
            ExperimentGroundTruthSensor sensor = recorder != null ? recorder.groundTruthSensor : null;
            if (recorder == null || sensor == null || frameTruth == null)
                throw new InvalidOperationException("Object-level experiment instrumentation is unavailable.");
            List<ExperimentGroundTruthSensor.ClaimMatch> matches =
                sensor.MatchClaims(frameTruth, analysis.claims);

            if (UsesReliabilityAwareMethod && pendingVerificationMemory != null && !scanClaimsTreasure)
            {
                await QuarantinePendingFromCounterEvidenceAsync("independent_view_counter_evidence");
                if (!controlled && !CanAct(epoch)) return false;
                if (controlled && !recorder.IsRunning) return false;
            }

            VisualMemoryEntry bestActionCandidate = null;
            List<int> claimOrder = Enumerable.Range(0, analysis.claims.Length)
                .OrderBy(index => analysis.claims[index].claim_id, StringComparer.Ordinal).ToList();
            foreach (int index in claimOrder)
            {
                VisionObjectClaim claim = analysis.claims[index];
                ExperimentGroundTruthSensor.ClaimMatch match = matches[index];
                string claimDescription = BuildClaimPolicyDescription(claim);
                VisualMemoryEntry memEntry = AnalyzeAndCreateMemory(claimDescription, imageSizeKB);
                string eventMemoryId = "";
                float credibility = -1f, rawCredibility = -1f;
                bool memoryWritten = false;
                string memoryState = "skipped_duplicate_or_low_quality";

                if (memEntry == null)
                {
                    if (lastConfirmedMemory != null)
                    {
                        if (memoryClient != null && !string.IsNullOrEmpty(lastConfirmedMemory.backendId))
                        {
                            await memoryClient.UpdateMemoryStatusAsync(
                                lastConfirmedMemory.backendId, lastConfirmedMemory.status,
                                lastConfirmedMemory.credibility, lastConfirmedMemory.confirmationCount,
                                lastConfirmedMemory.contradictionCount,
                                lastConfirmationIndependent ? "independent_confirmation" : "correlated_repeat");
                            if (!controlled && !CanAct(epoch)) return false;
                            if (controlled && !recorder.IsRunning) return false;
                        }
                        eventMemoryId = string.IsNullOrEmpty(lastConfirmedMemory.backendId)
                            ? lastConfirmedMemory.id : lastConfirmedMemory.backendId;
                        credibility = lastConfirmedMemory.credibility;
                        rawCredibility = lastConfirmedMemory.rawCredibility;
                        memoryState = lastConfirmedMemory.status;
                        if (lastConfirmedMemory.containsTreasure && lastConfirmationIndependent && !controlled)
                        {
                            CompleteIndependentVerification(lastConfirmedMemory);
                            bestActionCandidate = SelectHigherCredibility(
                                bestActionCandidate, lastConfirmedMemory);
                        }
                    }
                }
                else
                {
                    memEntry.observationId = scanId + "__" + claim.claim_id;
                    memEntry.frameId = scanId;
                    memEntry.claimId = claim.claim_id;
                    memoryWritten = ShouldPersistObservation(memEntry);
                    if (memoryWritten)
                    {
                        AddVisualMemory(memEntry);
                        await StoreStructuredObservationAsync(memEntry);
                        if (!controlled && !CanAct(epoch)) return false;
                        if (controlled && !recorder.IsRunning) return false;
                    }
                    else if (UsesReliabilityAwareMethod)
                    {
                        memEntry.status = "rejected";
                    }
                    eventMemoryId = string.IsNullOrEmpty(memEntry.backendId)
                        ? memEntry.id : memEntry.backendId;
                    credibility = memEntry.credibility;
                    rawCredibility = memEntry.rawCredibility;
                    memoryState = memEntry.status;
                    if (memEntry.containsTreasure && !controlled)
                        bestActionCandidate = SelectHigherCredibility(bestActionCandidate, memEntry);
                }

                recorder.RecordObjectClaim(scanId, claim, match, eventMemoryId,
                    claimDescription, credibility, rawCredibility, memoryWritten, memoryState);
                Debug.Log($"<color=cyan>[Object Memory]</color> claim={claim.claim_id} mid={eventMemoryId} " +
                          $"state={memoryState} raw={rawCredibility:F2} calibrated={credibility:F2}");
            }

            RecordObjectMisses(scanId, frameTruth, analysis, matches);
            if (!controlled && bestActionCandidate != null)
                HandleTreasureDetection(bestActionCandidate);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"<color=orange>[Vision Error]</color> {e.Message}");
            // A controlled exposure is valid only after the object-level
            // observation/memory transaction completes. Retry failed frames
            // without advancing the attack dose.
            if (controlled) schemaValidObservation = false;
        }
        finally
        {
            if (schemaValidObservation) NotifyAttackExposure(frameTruth, scanId);
            isScanning = false;
            isVisionBusy = false;
        }
        return schemaValidObservation;
    }

    private static string BuildClaimPolicyDescription(VisionObjectClaim claim)
    {
        string appearance = claim.color + " " + claim.shape;
        Vector2 center = claim.Center;
        string horizontal = center.x < 0.34f ? "left" : (center.x > 0.66f ? "right" : "center");
        string vertical = center.y < 0.34f ? "top" : (center.y > 0.66f ? "bottom" : "middle");
        string box = $"bbox=({claim.bbox_xmin:F3},{claim.bbox_ymin:F3}," +
            $"{claim.bbox_xmax:F3},{claim.bbox_ymax:F3})";
        if (claim.IsTreasureClaim)
            return $"FOUND: yellow cube; VLM appearance={appearance}; region={horizontal}-{vertical}; " +
                $"{box}; confidence={claim.confidence:F3}.";
        return $"{claim.predicted_label.ToUpperInvariant()} candidate; VLM appearance={appearance}; " +
            $"region={horizontal}-{vertical}; {box}; confidence={claim.confidence:F3}.";
    }

    private static VisualMemoryEntry SelectHigherCredibility(
        VisualMemoryEntry current, VisualMemoryEntry candidate)
    {
        if (candidate == null) return current;
        if (current == null || candidate.credibility > current.credibility) return candidate;
        if (Mathf.Approximately(candidate.credibility, current.credibility) &&
            string.CompareOrdinal(candidate.id, current.id) < 0) return candidate;
        return current;
    }

    private void NotifyAttackExposure(ExperimentGroundTruthSensor.Snapshot truth, string scanId)
    {
        if (attackController == null || truth == null || ExperimentRecorder == null ||
            !ExperimentRecorder.IsRunning) return;
        Camera camera = robotEyes != null ? robotEyes.visionCamera : null;
        Vector3 cameraPosition = camera != null ? camera.transform.position : transform.position;
        Quaternion cameraRotation = camera != null ? camera.transform.rotation : transform.rotation;
        // Notify after the frame's object/memory events. The finally path still
        // runs after early returns and exceptions, so a schema-valid exposure is counted once.
        attackController.NotifyObservationCompleted(truth, cameraPosition, cameraRotation, scanId);
    }

    private void RecordObjectMisses(string scanId,
        ExperimentGroundTruthSensor.Snapshot truth, ExperimentVisionAnalysis analysis,
        List<ExperimentGroundTruthSensor.ClaimMatch> matches)
    {
        EmbodiedRagExperiment recorder = ExperimentRecorder;
        if (recorder == null || truth == null || analysis == null || matches == null) return;
        HashSet<string> detectedTreasures = new HashSet<string>();
        for (int index = 0; index < matches.Count; index++)
        {
            VisionObjectClaim claim = analysis.claims[index];
            ExperimentGroundTruthSensor.ClaimMatch match = matches[index];
            if (claim.IsTreasureClaim && match.alignment_status == "matched" &&
                match.matched_gt_class == "treasure")
                detectedTreasures.Add(match.matched_object_id);
        }
        if (truth.objects == null) return;
        foreach (ExperimentGroundTruthSensor.VisibleObjectTruth item in truth.objects)
        {
            if (item != null && item.matchable && item.object_class == "treasure" &&
                !detectedTreasures.Contains(item.object_id))
                recorder.RecordObjectMiss(scanId, item);
        }
    }

    // =====================================================================
    // --- 鈽?鍒涙柊鏍稿績锛氬缁村害鍙俊搴﹀垎鏋?---
    // =====================================================================

    /// <summary>
    /// 鍒嗘瀽瑙嗚鎻忚堪锛屽垱寤哄甫鍙俊搴﹁瘎鍒嗙殑璁板繂鏉＄洰
    /// </summary>
    private VisualMemoryEntry AnalyzeAndCreateMemory(string description, int imageSizeKB)
    {
        // --- 鍘婚噸妫€鏌?---
        lastConfirmationIndependent = false;
        lastConfirmedMemory = BoostSimilarMemoryCredibility(description);
        if (lastConfirmedMemory != null)
        {
            return null;
        }
        string loc = currentLocationName == "Unknown Area"
            ? $"Coord({transform.position.x:F0},{transform.position.z:F0})"
            : currentLocationName;

        // --- 缁村害1锛氭弿杩拌川閲忚瘎浼?---
        float descQuality = EvaluateDescriptionQuality(description);

        // --- 缁村害2锛氳瑙夋竻鏅板害锛堜粠鎻忚堪涓彁鍙?[Clarity:N]锛?---
        float visualClarity = ExtractClarityScore(description);

        // --- 缁村害3锛氬浘鐗囧ぇ灏忓悎鐞嗘€?---
        float imageSizeScore = EvaluateImageSize(imageSizeKB);

        // --- 缁村害4锛氱┖闂翠竴鑷存€э紙涓庢渶杩戣蹇嗙殑浣嶇疆鏄惁杩炶疮锛?---
        float spatialScore = EvaluateSpatialConsistency(transform.position);

        // --- 缁村害5锛氬唴瀹逛竴鑷存€э紙鏄惁涓庡凡鐭ョ幆澧冧俊鎭煕鐩撅級 ---
        float contentScore = EvaluateContentConsistency(description);

        // --- 缁煎悎鍙俊搴﹁绠楋紙鍔犳潈骞冲潎锛?---
        float rawCredibility =
            descQuality * 0.25f +
            visualClarity * 0.20f +
            imageSizeScore * 0.10f +
            spatialScore * 0.20f +
            contentScore * 0.25f;

        rawCredibility = Mathf.Clamp01(rawCredibility);
        string category = CategorizeMemory(description);
        bool hasTreasure = description.IndexOf(
            "FOUND:", System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (hasTreasure)
            rawCredibility = ValidateTreasureClaim(description, rawCredibility);

        float credibility = rawCredibility;
        if (UsesReliabilityAwareMethod && reliabilityCalibrator != null)
            credibility = reliabilityCalibrator.Calibrate(rawCredibility);
        var entry = new VisualMemoryEntry
        {
            id = $"mem_{memoryIdCounter++}",
            description = CleanDescription(description),
            location = loc,
            worldPosition = transform.position,
            credibility = credibility,
            rawCredibility = rawCredibility,
            timestamp = Time.time,
            confirmationCount = 0,
            contradictionCount = 0,
            status = UsesReliabilityAwareMethod && hasTreasure ? "candidate" : "accepted",
            backendId = "",
            sourceModel = visionClient != null ? visionClient.modelName : "unknown",
            lastObservationPosition = transform.position,
            lastObservationForward = transform.forward,
            lastObservationTime = Time.time,
            category = category,
            containsTreasure = hasTreasure,
            descriptionQuality = descQuality,
            spatialConsistency = spatialScore,
            temporalRelevance = 1.0f,
            visualClarity = visualClarity
        };

        Debug.Log($"<color=magenta>[Credibility Analysis]</color>\n" +
                  $"  Description Quality: {descQuality:F2}\n" +
                  $"  Visual Clarity: {visualClarity:F2}\n" +
                  $"  Image Size Score: {imageSizeScore:F2}\n" +
                  $"  Spatial Consistency: {spatialScore:F2}\n" +
                  $"  Content Consistency: {contentScore:F2}\n" +
                  $"  鈽?Final Credibility: {credibility:F2}\n" +
                  $"  Category: {category} | Treasure: {hasTreasure}");

        return entry;
    }

    /// <summary>
    /// 缁村害1锛氭弿杩拌川閲?鈥?闀垮害銆佺粏鑺備赴瀵屽害銆佽娉曞畬鏁存€?
    /// </summary>
    private float EvaluateDescriptionQuality(string desc)
    {
        float score = 0f;

        // 闀垮害璇勫垎锛堝お鐭垨澶暱閮戒笉濂斤級
        int len = desc.Length;
        if (len < 20) score += 0.1f;
        else if (len < 50) score += 0.3f;
        else if (len < 150) score += 0.7f;
        else if (len < 300) score += 1.0f;
        else if (len < 500) score += 0.9f;
        else score += 0.7f; // 澶暱鍙兘鏄够瑙?

        // 缁嗚妭璇嶆眹妫€娴?
        string lower = desc.ToLower();
        string[] detailWords = { "left", "right", "center", "near", "far", "behind",
                                  "wooden", "stone", "metal", "large", "small",
                                  "building", "path", "tree", "wall", "gate",
                                  "red", "blue", "green", "yellow", "brown" };

        int detailCount = detailWords.Count(w => lower.Contains(w));
        float detailScore = Mathf.Clamp01(detailCount / 5f);

        // 鍙ュ瓙鏁伴噺
        int sentenceCount = desc.Split('.', '!', '?').Count(s => s.Trim().Length > 5);
        float sentenceScore = Mathf.Clamp01(sentenceCount / 3f);

        return (score * 0.4f + detailScore * 0.35f + sentenceScore * 0.25f);
    }

    /// <summary>
    /// 缁村害2锛氫粠鎻忚堪涓彁鍙?[Clarity:N] 璇勫垎
    /// </summary>
    private float ExtractClarityScore(string desc)
    {
        // 瀵绘壘 [Clarity:N] 鏍煎紡
        int idx = desc.IndexOf("[Clarity:");
        if (idx >= 0)
        {
            int start = idx + 9;
            int end = desc.IndexOf(']', start);
            if (end > start)
            {
                string numStr = desc.Substring(start, end - start).Trim();
                if (float.TryParse(numStr, out float clarity))
                {
                    return Mathf.Clamp01(clarity / 10f);
                }
            }
        }

        // 娌℃湁鏍囨敞锛屾牴鎹弿杩伴棿鎺ユ帹鏂?
        string lower = desc.ToLower();
        if (lower.Contains("blurr") || lower.Contains("unclear") || lower.Contains("dark"))
            return 0.3f;
        if (lower.Contains("clear") || lower.Contains("bright") || lower.Contains("detailed"))
            return 0.8f;

        return 0.6f; // 榛樿涓瓑
    }

    /// <summary>
    /// 缁村害3锛氬浘鐗囧ぇ灏忓悎鐞嗘€?
    /// </summary>
    private float EvaluateImageSize(int sizeKB)
    {
        if (sizeKB < 5) return 0.1f;        // 澶皬锛屽彲鑳芥崯鍧?
        if (sizeKB < 20) return 0.4f;       // 鍋忓皬
        if (sizeKB < 50) return 0.7f;       // 姝ｅ父鍋忓皬
        if (sizeKB < 200) return 1.0f;      // 鐞嗘兂鑼冨洿
        if (sizeKB < 500) return 0.9f;      // 鍋忓ぇ浣嗗彲鎺ュ彈
        return 0.7f;                         // 澶ぇ锛屽彲鑳戒紶杈撴湁闂
    }

    /// <summary>
    /// 缁村害4锛氱┖闂翠竴鑷存€?鈥?褰撳墠浣嶇疆涓庢渶杩戣蹇嗕綅缃槸鍚﹁繛璐?
    /// </summary>
    private float EvaluateSpatialConsistency(Vector3 currentPos)
    {
        if (visualMemories.Count == 0) return 0.8f;

        // 鎵炬渶杩戜竴鏉¤蹇?
        var lastMemory = visualMemories[visualMemories.Count - 1];
        float dist = Vector3.Distance(currentPos, lastMemory.worldPosition);
        float timeDiff = Time.time - lastMemory.timestamp;

        if (timeDiff <= 0) return 0.8f;

        // 璁＄畻"閫熷害"锛堣窛绂?鏃堕棿锛夛紝鍒ゆ柇鏄惁鍚堢悊
        float speed = dist / timeDiff;

        // NavMeshAgent 姝ｅ父閫熷害澶х害 3-5 m/s
        if (speed < 8f) return 1.0f;        // 鍚堢悊閫熷害
        if (speed < 15f) return 0.7f;       // 鍋忓揩浣嗗彲鑳?
        if (speed < 30f) return 0.4f;       // 鍙枒锛屽彲鑳戒紶閫佷簡
        return 0.2f;                         // 闈炲父鍙枒
    }

    /// <summary>
    /// 缁村害5锛氬唴瀹逛竴鑷存€?鈥?鎻忚堪鏄惁涓庡凡鐭ョ幆澧冧俊鎭竴鑷?
    /// </summary>
    private float EvaluateContentConsistency(string desc)
    {
        string lower = desc.ToLower();
        float score = 0.7f; // 鍩虹鍒?

        // 妫€鏌ユ槸鍚︽湁鏄庢樉鐨勫够瑙夋爣蹇?
        string[] hallucinationMarkers = {
            "person standing", "people walking", "car driving", "modern city",
            "ocean", "beach", "snow mountain", "real photo", "photograph"
        };

        foreach (string marker in hallucinationMarkers)
        {
            if (lower.Contains(marker))
            {
                score -= 0.2f;
                Debug.Log($"<color=orange>[Credibility]</color> Hallucination marker: '{marker}'");
            }
        }

        // 涓庢父鎴忓満鏅竴鑷寸殑鎻忚堪鍔犲垎
        string[] consistentMarkers = {
            "3d", "render", "virtual", "game", "building", "stone", "path",
            "temple", "gate", "lantern", "wooden", "village", "town"
        };

        foreach (string marker in consistentMarkers)
        {
            if (lower.Contains(marker))
            {
                score += 0.05f;
            }
        }

        return Mathf.Clamp01(score);
    }

    /// <summary>
    /// 瀹濈墿澹版槑鐨勯澶栭獙璇?
    /// </summary>
    private float ValidateTreasureClaim(string desc, float baseCredibility)
    {
        string lower = desc.ToLower();
        float bonus = 0f;

        // 鎻忚堪涓寘鍚叿浣撲綅缃俊鎭?鈫?鏇村彲淇?
        if (lower.Contains("left") || lower.Contains("right") || lower.Contains("center"))
            bonus += 0.1f;

        // 鎻忚堪涓寘鍚窛绂讳俊鎭?鈫?鏇村彲淇?
        if (lower.Contains("near") || lower.Contains("far") || lower.Contains("close"))
            bonus += 0.05f;

        // 鎻忚堪涓寘鍚鑹茬‘璁?鈫?鏇村彲淇?
        if (lower.Contains("yellow") && lower.Contains("cube"))
            bonus += 0.1f;

        // 涔嬪墠鍦ㄩ檮杩戜篃鐪嬪埌杩囧疂鐗?鈫?澶у箙鍔犲垎
        int nearbyTreasureMemories = visualMemories.Count(m =>
            m.containsTreasure &&
            Vector3.Distance(m.worldPosition, transform.position) < 30f
        );
        if (nearbyTreasureMemories > 0)
            bonus += 0.15f;

        return Mathf.Clamp01(baseCredibility + bonus);
    }

    /// <summary>
    /// 璁板繂鍒嗙被
    /// </summary>
    private string CategorizeMemory(string desc)
    {
        string lower = desc.ToLower();

        if (lower.Contains("found") || lower.Contains("treasure") || lower.Contains("yellow cube"))
            return "treasure";
        if (lower.Contains("building") || lower.Contains("temple") || lower.Contains("tower") || lower.Contains("gate"))
            return "landmark";
        if (lower.Contains("path") || lower.Contains("road") || lower.Contains("bridge") || lower.Contains("street"))
            return "path";
        if (lower.Contains("tree") || lower.Contains("grass") || lower.Contains("water") || lower.Contains("sky"))
            return "ambient";

        return "object";
    }

    /// <summary>
    /// 娓呯悊鎻忚堪鏂囨湰锛堝幓鎺?Clarity 鏍囪绛夛級
    /// </summary>
    private string CleanDescription(string desc)
    {
        // 鍘绘帀 [Clarity:N]
        int idx = desc.IndexOf("[Clarity:");
        if (idx >= 0)
        {
            int end = desc.IndexOf(']', idx);
            if (end > idx)
            {
                desc = desc.Substring(0, idx) + desc.Substring(end + 1);
            }
        }
        return desc.Trim();
    }

    // =====================================================================
    // --- 璁板繂鍘婚噸涓庣‘璁ゆ満鍒?---
    // =====================================================================

    private bool IsSimilarToRecent(string newDesc)
    {
        string newLower = newDesc.ToLower().Trim();

        // 妫€鏌ユ渶杩?5 鏉¤蹇?
        int checkCount = Mathf.Min(5, visualMemories.Count);
        for (int i = visualMemories.Count - 1; i >= visualMemories.Count - checkCount && i >= 0; i--)
        {
            string oldLower = visualMemories[i].description.ToLower().Trim();

            if (newLower == oldLower &&
                (!UsesReliabilityAwareMethod || IsSameVerificationCluster(visualMemories[i]))) return true;

            // 閮芥槸鐭?FOUND 鏂囨湰
            if (!UsesReliabilityAwareMethod && newLower.Length < 30 && oldLower.Length < 30 &&
                newLower.Contains("found") && oldLower.Contains("found"))
                return true;

            // 鍏抽敭璇嶉噸鍙犲害
            var newWords = new HashSet<string>(
                newLower.Split(' ', ',', '.', '!', '?').Where(w => w.Length > 3));
            var oldWords = new HashSet<string>(
                oldLower.Split(' ', ',', '.', '!', '?').Where(w => w.Length > 3));

            if (newWords.Count > 0 && oldWords.Count > 0)
            {
                int overlap = newWords.Intersect(oldWords).Count();
                float similarity = (float)overlap / Mathf.Max(newWords.Count, oldWords.Count);
                if (similarity > 0.7f) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 鈽?纭鏈哄埗锛氶噸澶嶈瀵熷埌鐩镐技鍐呭鏃讹紝鎻愬崌宸叉湁璁板繂鐨勫彲淇″害
    /// </summary>
    private bool IsSameVerificationCluster(VisualMemoryEntry memory)
    {
        float minimumViewDistance = reliabilityCalibrator != null
            ? reliabilityCalibrator.IndependentViewDistance : 1.5f;
        float clusterRadius = Mathf.Max(4f, minimumViewDistance * 3f);
        return Vector3.Distance(memory.worldPosition, transform.position) <= clusterRadius;
    }

    private static bool HaveCompatibleTargetSignature(string left, string right)
    {
        string[] tokens = { "yellow", "orange", "brown", "red", "blue", "green",
                            "cube", "sphere", "cylinder", "box", "block" };
        bool hasSignature = false;
        foreach (string token in tokens)
        {
            bool inLeft = left.Contains(token);
            bool inRight = right.Contains(token);
            if (inLeft || inRight) hasSignature = true;
            if (inLeft != inRight) return false;
        }
        return hasSignature;
    }

    private VisualMemoryEntry BoostSimilarMemoryCredibility(string newDesc)
    {
        string newLower = newDesc.ToLower();
        for (int i = visualMemories.Count - 1; i >= Mathf.Max(0, visualMemories.Count - 5); i--)
        {
            VisualMemoryEntry memory = visualMemories[i];
            string oldLower = memory.description.ToLower();
            var newWords = new HashSet<string>(newLower.Split(' ', ',', '.').Where(w => w.Length > 3));
            var oldWords = new HashSet<string>(oldLower.Split(' ', ',', '.').Where(w => w.Length > 3));
            float similarity = newWords.Count > 0 && oldWords.Count > 0
                ? (float)newWords.Intersect(oldWords).Count() / Mathf.Max(newWords.Count, oldWords.Count)
                : 0f;
            bool foundPair = newLower.Contains("found") && oldLower.Contains("found");
            bool isSimilar = UsesReliabilityAwareMethod
                ? similarity > 0.6f && IsSameVerificationCluster(memory) &&
                  HaveCompatibleTargetSignature(newLower, oldLower)
                : foundPair || similarity > 0.6f;
            if (!isSimilar) continue;

            float distance = Vector3.Distance(memory.lastObservationPosition, transform.position);
            float angle = Vector3.Angle(memory.lastObservationForward, transform.forward);
            float requiredDistance = reliabilityCalibrator != null
                ? reliabilityCalibrator.IndependentViewDistance : 1.5f;
            float requiredAngle = reliabilityCalibrator != null
                ? reliabilityCalibrator.IndependentViewAngle : 25f;
            bool independent = distance >= requiredDistance || angle >= requiredAngle;

            if (UsesReliabilityAwareMethod)
            {
                lastConfirmationIndependent = independent;
                if (independent)
                {
                    memory.confirmationCount++;
                    memory.lastObservationPosition = transform.position;
                    memory.lastObservationForward = transform.forward;
                    memory.lastObservationTime = Time.time;
                    int required = reliabilityCalibrator != null
                        ? reliabilityCalibrator.MinimumIndependentConfirmations : 1;
                    if (memory.confirmationCount >= required) memory.status = "verified";
                }
            }
            else
            {
                memory.confirmationCount++;
                memory.credibility = Mathf.Clamp01(memory.credibility + confirmationBoost);
                lastConfirmationIndependent = true;
            }

            Debug.Log($"[Memory Confirmation] {memory.id} independent={independent} " +
                      $"count={memory.confirmationCount} state={memory.status}");
            return memory;
        }
        return null;
    }
    /// <summary>
    /// 娣诲姞璁板繂鍒版湰鍦板瓨鍌紙鑷姩娣樻卑浣庡彲淇″害鏃ц蹇嗭級
    /// </summary>
    private bool ShouldPersistObservation(VisualMemoryEntry entry)
    {
        if (!UsesLongTermMemory || memoryClient == null) return false;
        if (!UsesReliabilityAwareMethod) return true;
        float threshold = reliabilityCalibrator != null
            ? reliabilityCalibrator.WriteThreshold : credibilityThreshold;
        return entry.credibility >= threshold;
    }

    private async Task StoreStructuredObservationAsync(VisualMemoryEntry entry)
    {
        if (memoryClient == null) return;
        string runId = ExperimentRecorder != null && !string.IsNullOrEmpty(ExperimentRecorder.RunId)
            ? ExperimentRecorder.RunId : "runtime";
        entry.backendId = runId + "__" + entry.id;
        MemoryClient.StructuredMemoryRecord record = new MemoryClient.StructuredMemoryRecord
        {
            memory_id = entry.backendId,
            run_id = runId,
            observation_id = string.IsNullOrEmpty(entry.observationId) ? entry.id : entry.observationId,
            content = entry.description,
            location = entry.location,
            timestamp = System.DateTime.UtcNow.ToString("o"),
            category = entry.category,
            source_model = entry.sourceModel,
            status = entry.status,
            credibility = entry.credibility,
            raw_credibility = entry.rawCredibility,
            position_x = entry.worldPosition.x,
            position_y = entry.worldPosition.y,
            position_z = entry.worldPosition.z,
            confirmation_count = entry.confirmationCount,
            contradiction_count = entry.contradictionCount
        };
        string storedId = await memoryClient.AddStructuredMemoryAsync(record);
        if (!string.IsNullOrEmpty(storedId)) entry.backendId = storedId;
    }

    private void AddVisualMemory(VisualMemoryEntry entry)
    {
        visualMemories.Add(entry);

        // 瓒呰繃涓婇檺鏃讹紝娣樻卑鍙俊搴︽渶浣庣殑
        if (visualMemories.Count > MAX_MEMORIES)
        {
            float minCred = float.MaxValue;
            int minIdx = 0;

            for (int i = 0; i < visualMemories.Count; i++)
            {
                float eff = visualMemories[i].GetEffectiveCredibility(Time.time, decayRatePerMinute);
                if (eff < minCred)
                {
                    minCred = eff;
                    minIdx = i;
                }
            }

            Debug.Log($"<color=grey>[Memory Evict]</color> {visualMemories[minIdx].id} (C={minCred:F2})");
            visualMemories.RemoveAt(minIdx);
        }
    }

    // =====================================================================
    // --- 3. 璁板繂缁存姢鍗忕▼锛堝畾鏈熻“鍑?+ 娓呯悊锛?---
    // =====================================================================
    private System.Collections.IEnumerator MemoryMaintenanceRoutine()
    {
        while (autonomyRunning)
        {
            yield return new WaitForSeconds(60f); // 姣忓垎閽熺淮鎶や竴娆?

            if (!UsesLongTermMemory || ActiveMethod == EmbodiedRagMethodCondition.B_VanillaRag)
            {
                LogMemoryStatus();
                continue;
            }

            int removed = 0;
            for (int i = visualMemories.Count - 1; i >= 0; i--)
            {
                float eff = visualMemories[i].GetEffectiveCredibility(Time.time, decayRatePerMinute);

                // 娣樻卑浣庝簬闃堝€肩殑璁板繂
                if (eff < credibilityThreshold && visualMemories[i].category != "treasure")
                {
                    visualMemories.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
            {
                Debug.Log($"<color=grey>[Memory Maintenance]</color> Removed {removed} low-credibility memories. " +
                          $"Remaining: {visualMemories.Count}");
            }

            // 杈撳嚭璁板繂鐘舵€佹憳瑕?
            LogMemoryStatus();
        }
    }

    private void LogMemoryStatus()
    {
        if (visualMemories.Count == 0) return;

        float avgCred = visualMemories.Average(m => m.GetEffectiveCredibility(Time.time, decayRatePerMinute));
        int treasureCount = visualMemories.Count(m => m.containsTreasure);
        int highCred = visualMemories.Count(m => m.GetEffectiveCredibility(Time.time, decayRatePerMinute) > 0.7f);

        Debug.Log($"<color=magenta>[Memory Status]</color> Total: {visualMemories.Count} | " +
                  $"Avg Credibility: {avgCred:F2} | High-C: {highCred} | Treasure refs: {treasureCount}");
    }

    // =====================================================================
    // --- 鈽?鍙俊搴﹀姞鏉?RAG ---
    // =====================================================================

    /// <summary>
    /// 鍩轰簬鍙俊搴﹁繃婊ゅ拰鎺掑簭鐨?RAG 妫€绱?
    /// </summary>
    private string GetCredibilityWeightedContext(string query, int maxTokens = 300)
    {
        if (!UsesLongTermMemory) return "";
        float currentTime = Time.time;
        var scoredMemories = visualMemories
            .Select(m => new
            {
                memory = m,
                effectiveCredibility = m.GetEffectiveCredibility(currentTime, decayRatePerMinute),
                relevance = CalculateRelevance(m, query)
            })
            .Where(x => ActiveMethod == EmbodiedRagMethodCondition.B_VanillaRag ||
                (x.effectiveCredibility >= credibilityThreshold &&
                 (!UsesReliabilityAwareMethod || IsReliabilityRetrievableState(x.memory.status))))
            .OrderByDescending(x => ActiveMethod == EmbodiedRagMethodCondition.B_VanillaRag
                ? x.relevance
                : x.effectiveCredibility * 0.4f + x.relevance * 0.6f)
            .ToList();

        var contextLines = new List<string>();
        int charCount = 0;
        foreach (var item in scoredMemories)
        {
            string id = string.IsNullOrEmpty(item.memory.backendId) ? item.memory.id : item.memory.backendId;
            string line = ActiveMethod == EmbodiedRagMethodCondition.B_VanillaRag
                ? $"[MID:{id}|State:{item.memory.status}] {item.memory.description} @{item.memory.location}"
                : $"[MID:{id}|C:{item.effectiveCredibility:F2}|State:{item.memory.status}|{item.memory.category}] " +
                  $"{item.memory.description} @{item.memory.location}";
            if (charCount + line.Length > maxTokens * 4) break;
            contextLines.Add(line);
            lastPresentedEvidenceIds.Add(id);
            charCount += line.Length;
        }
        return string.Join("\n", contextLines);
    }

    private static bool IsReliabilityRetrievableState(string status)
    {
        return status == "accepted" || status == "verified";
    }
    private float CalculateRelevance(VisualMemoryEntry memory, string query)
    {
        string queryLower = query.ToLower();
        string memLower = memory.description.ToLower();

        // 鍏抽敭璇嶅尮閰?
        var queryWords = new HashSet<string>(
            queryLower.Split(' ', ',', '.', '?', '!').Where(w => w.Length > 3));
        var memWords = new HashSet<string>(
            memLower.Split(' ', ',', '.', '?', '!').Where(w => w.Length > 3));

        if (queryWords.Count == 0) return 0.5f;

        int overlap = queryWords.Intersect(memWords).Count();
        float wordRelevance = (float)overlap / queryWords.Count;

        // 绫诲埆鍖归厤
        float categoryBonus = 0f;
        if (queryLower.Contains("treasure") && memory.category == "treasure") categoryBonus = 0.3f;
        if (queryLower.Contains("explore") && memory.category == "landmark") categoryBonus = 0.2f;
        if (queryLower.Contains("path") && memory.category == "path") categoryBonus = 0.2f;

        // 璺濈鐩稿叧鎬э紙杩戠殑鏇寸浉鍏筹級
        float dist = Vector3.Distance(transform.position, memory.worldPosition);
        float distRelevance = Mathf.Clamp01(1f - dist / 100f);

        return Mathf.Clamp01(wordRelevance * 0.5f + categoryBonus + distRelevance * 0.2f);
    }

    // =====================================================================
    // --- 瀹濈墿妫€娴嬶紙鍩轰簬鍙俊搴︼級 ---
    // =====================================================================
    private void HandleTreasureDetection(VisualMemoryEntry memEntry)
    {
        if (currentMode != RobotMode.FIND_TREASURE && currentMode != RobotMode.INVESTIGATE)
        {
            if (ExperimentRecorder != null)
                ExperimentRecorder.RecordActionGate(MemoryEventId(memEntry), "defer", memEntry.credibility);
            return;
        }

        if (ActiveMethod == EmbodiedRagMethodCondition.A_NoMemory ||
            ActiveMethod == EmbodiedRagMethodCondition.B_VanillaRag)
        {
            if (ExperimentRecorder != null)
                ExperimentRecorder.RecordActionGate(MemoryEventId(memEntry), "pursue_reactive", memEntry.credibility);
            HandleTreasureDiscovery(memEntry.description);
            return;
        }

        if (ActiveMethod == EmbodiedRagMethodCondition.C_HeuristicTrust)
        {
            if (memEntry.credibility < 0.5f)
            {
                if (ExperimentRecorder != null)
                    ExperimentRecorder.RecordActionGate(MemoryEventId(memEntry), "verify", memEntry.credibility);
                return;
            }
            if (ExperimentRecorder != null)
                ExperimentRecorder.RecordActionGate(MemoryEventId(memEntry), "pursue", memEntry.credibility);
            HandleTreasureDiscovery(memEntry.description);
            return;
        }

        float verifyThreshold = reliabilityCalibrator != null
            ? reliabilityCalibrator.VerifyThreshold : 0.45f;
        float pursueThreshold = reliabilityCalibrator != null
            ? reliabilityCalibrator.PursueThreshold : 0.75f;
        if (memEntry.credibility < verifyThreshold || memEntry.status == "rejected")
        {
            memEntry.status = "quarantined";
            if (ExperimentRecorder != null)
                ExperimentRecorder.RecordActionGate(MemoryEventId(memEntry), "quarantine", memEntry.credibility);
            if (memoryClient != null && !string.IsNullOrEmpty(memEntry.backendId))
                _ = memoryClient.UpdateMemoryStatusAsync(
                    memEntry.backendId, memEntry.status, memEntry.credibility,
                    memEntry.confirmationCount, memEntry.contradictionCount, "below_verify_threshold");
            return;
        }

        int required = reliabilityCalibrator != null
            ? reliabilityCalibrator.MinimumIndependentConfirmations : 1;
        if (memEntry.status != "verified" || memEntry.confirmationCount < required)
        {
            BeginActiveVerification(memEntry);
            return;
        }

        if (memEntry.credibility < pursueThreshold)
        {
            if (ExperimentRecorder != null)
                ExperimentRecorder.RecordActionGate(MemoryEventId(memEntry), "abstain", memEntry.credibility);
            return;
        }

        if (ExperimentRecorder != null)
            ExperimentRecorder.RecordActionGate(MemoryEventId(memEntry), "pursue_verified", memEntry.credibility);
        HandleTreasureDiscovery(memEntry.description);
    }

    private void BeginActiveVerification(VisualMemoryEntry memory)
    {
        if (pendingVerificationMemory == memory) return;
        pendingVerificationMemory = memory;
        modeBeforeVerification = currentMode == RobotMode.INVESTIGATE
            ? RobotMode.FIND_TREASURE : currentMode;
        currentMode = RobotMode.INVESTIGATE;
        currentGoal = "Acquire an independent viewpoint before acting on a treasure claim.";
        if (ExperimentRecorder != null)
            ExperimentRecorder.RecordActionGate(MemoryEventId(memory), "verify", memory.credibility);

        Vector3 direction = EstimateDirectionFromDescription(memory.description);
        float baseline = reliabilityCalibrator != null
            ? reliabilityCalibrator.IndependentViewDistance : 1.5f;
        float side = (StableProtocolHash(memory.id) & 1u) == 0u ? 1f : -1f;
        Vector3 candidate = transform.position + direction * 3f + transform.right * baseline * side;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(candidate, out hit, 6f, NavMesh.AllAreas))
        {
            scheduler.StopImmediate();
            scheduler.MoveToLocation(hit.position);
            currentLocationName = "Verification_Viewpoint";
        }
        else
        {
            _ = PerformVisualScanAsync();
        }
        StartCoroutine(VerificationTimeout(memory.id));
    }

    private static string MemoryEventId(VisualMemoryEntry memory)
    {
        return memory == null ? "" : (string.IsNullOrEmpty(memory.backendId) ? memory.id : memory.backendId);
    }

    private static uint StableProtocolHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261u;
            string normalized = value ?? "";
            for (int index = 0; index < normalized.Length; index++)
            {
                hash ^= normalized[index];
                hash *= 16777619u;
            }
            return hash;
        }
    }

    private System.Collections.IEnumerator VerificationTimeout(string memoryId)
    {
        yield return new WaitForSeconds(Mathf.Max(5f, verificationTimeoutSeconds));
        if (pendingVerificationMemory == null || pendingVerificationMemory.id != memoryId) yield break;
        if (ExperimentRecorder != null)
            ExperimentRecorder.RecordActionGate(MemoryEventId(pendingVerificationMemory), "verify_timeout", pendingVerificationMemory.credibility);
        pendingVerificationMemory = null;
        currentMode = modeBeforeVerification;
        currentGoal = "Verification timed out; continue searching without using the candidate memory.";
    }

    private void CompleteIndependentVerification(VisualMemoryEntry memory)
    {
        if (pendingVerificationMemory != memory) return;
        pendingVerificationMemory = null;
        currentMode = modeBeforeVerification;
        currentGoal = "Independent evidence confirmed; continue the treasure task.";
    }

    private async Task QuarantinePendingFromCounterEvidenceAsync(string reason)
    {
        VisualMemoryEntry memory = pendingVerificationMemory;
        if (memory == null) return;
        memory.contradictionCount++;
        memory.status = "quarantined";
        if (ExperimentRecorder != null)
            ExperimentRecorder.RecordActionGate(MemoryEventId(memory), "quarantine", memory.credibility);
        if (memoryClient != null && !string.IsNullOrEmpty(memory.backendId))
            await memoryClient.UpdateMemoryStatusAsync(
                memory.backendId, memory.status, memory.credibility,
                memory.confirmationCount, memory.contradictionCount, reason);
        pendingVerificationMemory = null;
        currentMode = modeBeforeVerification;
        currentGoal = "Counter-evidence quarantined a treasure claim; continue searching.";
    }
    private void HandleTreasureDiscovery(string visualDesc)
    {
        Debug.LogWarning($"<color=red>[TREASURE SPOTTED]</color> {visualDesc}");

        scheduler.StopImmediate();
        isChasingTreasure = true;
        lockedTarget = null;
        arrivalRetryCount = 0;

        GameObject[] treasures = ExperimentNoOracle
            ? new GameObject[0]
            : GameObject.FindGameObjectsWithTag(treasureTag);
        float minDistance = float.MaxValue;
        GameObject closestTreasure = null;

        foreach (GameObject t in treasures)
        {
            float dist = Vector3.Distance(transform.position, t.transform.position);
            NavMeshHit hit;
            bool reachable = NavMesh.SamplePosition(t.transform.position, out hit, 30f, NavMesh.AllAreas);

            if (reachable && dist < minDistance)
            {
                minDistance = dist;
                closestTreasure = t;
                lockedTarget = t.transform;
            }
        }

        if (lockedTarget != null)
        {
            Debug.LogWarning($"<color=red>[TREASURE] LOCKED:</color> {closestTreasure.name}, dist={minDistance:F1}m");

            if (chatController != null)
                chatController.AppendHistory(
                    $"<color=yellow>鈽?TREASURE LOCKED!</color> {closestTreasure.name} 鈥?{minDistance:F1}m away!");

            NavigateToTreasure();

            _ = AddLongTermMemoryAsync(
                $"[Credibility:0.95][treasure] LOCKED: {closestTreasure.name}, dist={minDistance:F1}m. Pursuing!",
                currentLocationName
            );
        }
        else
        {
            Vector3 searchDir = EstimateDirectionFromDescription(visualDesc);
            Vector3 searchPoint = transform.position + searchDir * 10f;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(searchPoint, out hit, 15f, 1))
                scheduler.MoveToLocation(hit.position);

            currentLocationName = "Treasure_Search_Area";
            currentGoal = "Moving closer to investigate.";
            StartCoroutine(RescanAfterDelay(5f));
        }
    }

    private bool TryDirectTreasureSearch()
    {
        if (ExperimentNoOracle) return false;

        GameObject[] treasures = GameObject.FindGameObjectsWithTag(treasureTag);
        if (treasures.Length == 0) return false;

        float minDistance = float.MaxValue;
        GameObject closestTreasure = null;

        foreach (GameObject t in treasures)
        {
            float dist = Vector3.Distance(transform.position, t.transform.position);
            NavMeshHit hit;
            bool reachable = NavMesh.SamplePosition(t.transform.position, out hit, 30f, NavMesh.AllAreas);

            if (reachable && dist < minDistance)
            {
                minDistance = dist;
                closestTreasure = t;
                lockedTarget = t.transform;
            }
        }

        if (lockedTarget != null)
        {
            if (chatController != null)
                chatController.AppendHistory(
                    $"<color=yellow>鈽?TREASURE FOUND!</color> {closestTreasure.name} 鈥?{minDistance:F1}m away!");

            isChasingTreasure = true;
            arrivalRetryCount = 0;
            scheduler.StopImmediate();
            NavigateToTreasure();

            // 鍒涘缓楂樺彲淇″害璁板繂锛堢洿鎺ユ悳绱㈠埌鐨勭墿浣擄級
            var directMem = new VisualMemoryEntry
            {
                id = $"mem_{memoryIdCounter++}",
                description = $"Direct detection: {closestTreasure.name} at distance {minDistance:F1}m",
                location = currentLocationName,
                worldPosition = transform.position,
                credibility = 0.95f,
                timestamp = Time.time,
                confirmationCount = 1,
                category = "treasure",
                containsTreasure = true,
                descriptionQuality = 1f,
                spatialConsistency = 1f,
                temporalRelevance = 1f,
                visualClarity = 1f
            };
            AddVisualMemory(directMem);

            return true;
        }
        return false;
    }

    private Vector3 EstimateDirectionFromDescription(string desc)
    {
        desc = desc.ToLower();
        if (desc.Contains("left"))
            return (transform.forward + -transform.right).normalized;
        if (desc.Contains("right"))
            return (transform.forward + transform.right).normalized;
        return transform.forward;
    }

    private void NavigateToTreasure()
    {
        if (lockedTarget == null)
        {
            isChasingTreasure = false;
            return;
        }

        NavMeshHit hit;
        if (NavMesh.SamplePosition(lockedTarget.position, out hit, 30f, NavMesh.AllAreas))
        {
            scheduler.MoveToLocation(hit.position);
            currentLocationName = "Treasure_Location";
            currentGoal = "Approaching the yellow cube treasure!";
        }
        else
        {
            if (chatController != null)
                chatController.AppendHistory("<color=red>System:</color> Treasure unreachable!");
            isChasingTreasure = false;
            lockedTarget = null;
            currentGoal = "Treasure unreachable. Continue exploring.";
        }
    }

    private System.Collections.IEnumerator RescanAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (isChasingTreasure && lockedTarget == null)
            _ = PerformVisualScanAsync();
    }

    // =====================================================================
    // --- 3. 澶ц剳鍐崇瓥锛堜娇鐢ㄥ彲淇″害鍔犳潈 RAG锛?---
    // =====================================================================
    private async Task PlanNextMoveAsync()
    {
        if (!CanActNow() || isThinking || isChasingTreasure) return;
        int epoch = runEpoch;
        isThinking = true;
        try
        {
            string historyStr = visitedHistory.Count > 0 ? string.Join(", ", visitedHistory) : "None";
            string ragQuery = currentMode == RobotMode.FIND_TREASURE
                ? "Where was treasure or yellow cube seen?"
                : $"Which locations have I visited? Unexplored areas? Recent: {historyStr}";

            lastPresentedEvidenceIds.Clear();
            string localContext = GetCredibilityWeightedContext(ragQuery);
            string chromaContext = "";
            if (UsesLongTermMemory && memoryClient != null)
            {
                if (UsesReliabilityAwareMethod)
                {
                    float minCredibility = reliabilityCalibrator != null
                        ? reliabilityCalibrator.WriteThreshold : credibilityThreshold;
                    chromaContext = await memoryClient.RetrieveReliabilityAwareMemories(
                        ragQuery, transform.position, retrievalTopK,
                        retrievalTrustWeight, retrievalRecencyWeight, retrievalSpatialWeight,
                        minCredibility, false);
                }
                else if (ActiveMethod == EmbodiedRagMethodCondition.C_HeuristicTrust)
                {
                    chromaContext = await memoryClient.RetrieveHeuristicTrustMemories(
                        ragQuery, transform.position, retrievalTopK, credibilityThreshold);
                    chromaContext = FilterRagContext(chromaContext);
                }
                else
                {
                    chromaContext = await memoryClient.RetrieveRelevantMemories(
                        ragQuery, retrievalTopK, "", 0f);
                }
                foreach (string id in memoryClient.LastRetrievedMemoryIds)
                    lastPresentedEvidenceIds.Add(id);
            }
            if (!CanAct(epoch)) return;

            if (ExperimentRecorder != null)
                ExperimentRecorder.RecordRetrieval(ragQuery, localContext, chromaContext);

            string combinedContext = UsesLongTermMemory
                ? $"=== Local memory ===\n{localContext}\n=== Vector memory ===\n{chromaContext}"
                : "NO LONG-TERM MEMORY IS AVAILABLE IN THIS CONDITION.";
            string mapInfo = townMap.GetLocationListPrompt();
            var msgs = new List<LLMClient.Message>
            {
                new LLMClient.Message { role = "system", content =
                    $"You are {robotName}. {personaDescription}\n" +
                    $"GOAL: {currentGoal}\n" +
                    $"CONTEXT: Recently Visited: {historyStr} | Known Map Nodes: {mapInfo}\n" +
                    $"MEMORY CONDITION: {ActiveMethod}\n{combinedContext}\n" +
                    "RULES:\n" +
                    "1. Choose an UNVISITED destination from Known Map Nodes.\n" +
                    "2. If all nodes were visited, output RANDOM_WANDER.\n" +
                    "3. Copy only exact MID values that actually influenced the decision.\n" +
                    "4. If memory did not influence the decision, use an empty evidence array.\n" +
                    "5. OUTPUT JSON ONLY: { \"thought\": \"...\", \"destination\": \"...\", " +
                    "\"used_evidence_ids\": [\"exact MID\"] }"
                },
                new LLMClient.Message { role = "user", content =
                    $"I am at {currentLocationName}. What is my next move?" }
            };

            if (ExperimentRecorder != null) ExperimentRecorder.OnLLMCall();
            float temperature = ExperimentRecorder != null ? 0f : 0.7f;
            string plannerMessagesJson = JsonConvert.SerializeObject(msgs);
            string response = await llmClient.ChatAsync(msgs, temperature);
            if (!CanAct(epoch)) return;
            if (!string.IsNullOrEmpty(response)) ProcessDecision(response, plannerMessagesJson);
            else
            {
                string decisionId = ExperimentRecorder != null
                    ? ExperimentRecorder.RecordDecision(currentMode.ToString(), "RANDOM_WANDER", "planner_empty_response") : "";
                FallbackWander(decisionId);
            }
        }
        catch (System.Exception error)
        {
            Debug.LogError("[Think] Error: " + error.Message);
            if (CanAct(epoch)) FallbackWander();
        }
        finally
        {
            isThinking = false;
        }
    }

    [System.Serializable]
    private class DecisionJson
    {
        public string thought;
        public string destination;
        public string[] used_evidence_ids;
    }

    private string ValidatePresentedEvidenceIds(string[] claimedIds)
    {
        if (claimedIds == null || claimedIds.Length == 0) return "";
        var valid = new List<string>();
        foreach (string rawId in claimedIds)
        {
            string id = (rawId ?? "").Trim();
            if (lastPresentedEvidenceIds.Contains(id) && !valid.Contains(id)) valid.Add(id);
        }
        return string.Join(",", valid);
    }
    private void ProcessDecision(string jsonResponse, string plannerMessagesJson = "")
    {
        if (!CanActNow()) return;
        string rawResponse = jsonResponse;
        try
        {
            jsonResponse = jsonResponse.Replace("```json", "").Replace("```", "").Trim();
            int s = jsonResponse.IndexOf('{'); int e = jsonResponse.LastIndexOf('}');
            if (s != -1 && e > s) jsonResponse = jsonResponse.Substring(s, e - s + 1);

            DecisionJson decision = JsonUtility.FromJson<DecisionJson>(jsonResponse);
            string evidenceIds = ValidatePresentedEvidenceIds(decision.used_evidence_ids);
            Debug.Log($"<color=magenta>[Think]</color> Thought: {decision.thought} | Dest: {decision.destination} | Evidence: {evidenceIds}");
            string decisionId = ExperimentRecorder != null
                ? ExperimentRecorder.RecordDecision(
                    currentMode.ToString(), decision.destination, decision.thought, evidenceIds) : "";
            if (replayRecorder != null)
                replayRecorder.RecordPlannerExchange(decisionId, plannerMessagesJson,
                    rawResponse, evidenceIds);

            if (decision.destination == "RANDOM_WANDER")
            {
                FallbackWander(decisionId);
                return;
            }

            Transform targetTF = townMap.GetWaypointByName(decision.destination)
                                ?? FindBestMatchingLocation(decision.destination);

            if (targetTF != null)
            {
                currentLocationName = targetTF.name;
                activeNavigationDecisionId = decisionId;
                scheduler.MoveToLocation(targetTF.position);
                if (ExperimentRecorder != null)
                    ExperimentRecorder.RecordActionDispatched(decisionId, targetTF.name, "planner_waypoint");
                Debug.Log($"<color=green>[Move]</color> Heading to: {targetTF.name}");
            }
            else
            {
                FallbackWander(decisionId);
            }

            if (!visitedHistory.Contains(currentLocationName))
            {
                visitedHistory.Add(currentLocationName);
                if (visitedHistory.Count > 10) visitedHistory.RemoveAt(0);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Decision Error: {ex.Message}");
            FallbackWander();
        }
    }

    private string FilterRagContext(string rawContext)
    {
        if (string.IsNullOrEmpty(rawContext)) return "";

        string[] lines = rawContext.Split('\n');
        var filteredLines = new List<string>();
        var seenSummaries = new HashSet<string>();
        int foundCount = 0;

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (trimmed.ToLower().Contains("found"))
            {
                foundCount++;
                if (foundCount > 2) continue;
            }

            if (trimmed.Contains("System initialized") || trimmed.Contains("ONLINE"))
                continue;

            string summary = trimmed.Length > 40 ? trimmed.Substring(0, 40) : trimmed;
            if (seenSummaries.Contains(summary)) continue;
            seenSummaries.Add(summary);

            filteredLines.Add(trimmed);
        }

        return string.Join("\n", filteredLines);
    }

    private void FallbackWander(string decisionId = "")
    {
        if (!CanActNow()) return;
        Vector3 point = GetRandomNavMeshPoint();
        activeNavigationDecisionId = decisionId;
        scheduler.MoveToLocation(point);
        if (ExperimentRecorder != null)
            ExperimentRecorder.RecordActionDispatched(decisionId, "RANDOM_WANDER",
                $"target=({point.x:F3},{point.y:F3},{point.z:F3})");
        currentLocationName = "Unknown Area";
        Debug.Log($"<color=blue>[Wander]</color> Random wander to ({point.x:F0}, {point.z:F0})");
    }

    private Vector3 GetRandomNavMeshPoint()
    {
        Vector3 dir = UnityEngine.Random.insideUnitSphere * wanderRadius + transform.position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(dir, out hit, wanderRadius, 1)) return hit.position;
        return transform.position;
    }

    // =====================================================================
    // --- 鍒拌揪鐩殑鍦?---
    // =====================================================================
    private void HandleArrival()
    {
        if (!CanActNow()) return;
        if (!string.IsNullOrEmpty(activeNavigationDecisionId) && ExperimentRecorder != null)
            ExperimentRecorder.RecordActionCompleted(activeNavigationDecisionId,
                currentLocationName, "scheduler_destination_reached");
        activeNavigationDecisionId = "";
        Debug.Log($"<color=green>[Event]</color> Destination Reached: {currentLocationName}");

        if (isChasingTreasure)
        {
            if (lockedTarget != null)
            {
                float dist = Vector3.Distance(transform.position, lockedTarget.position);

                if (dist < treasureStopDistance + 3f)
                {
                    Debug.LogWarning($"<color=red>鈽呪槄鈽?TREASURE COLLECTED! 鈽呪槄鈽?/color> dist={dist:F1}m");

                    if (chatController != null)
                        chatController.AppendHistory("<color=green>鈽呪槄鈽?TREASURE COLLECTED! 鈽呪槄鈽?/color>");

                    // 瀛樺叆楂樺彲淇″害璁板繂
                    var collectMem = new VisualMemoryEntry
                    {
                        id = $"mem_{memoryIdCounter++}",
                        description = $"SUCCESS! Collected treasure at ({lockedTarget.position.x:F0}, {lockedTarget.position.z:F0})",
                        location = "Treasure_Location",
                        worldPosition = lockedTarget.position,
                        credibility = 1.0f,
                        timestamp = Time.time,
                        confirmationCount = 5,
                        category = "treasure",
                        containsTreasure = true,
                        descriptionQuality = 1f,
                        spatialConsistency = 1f,
                        temporalRelevance = 1f,
                        visualClarity = 1f
                    };
                    AddVisualMemory(collectMem);
                    _ = AddLongTermMemoryAsync(collectMem.description, "Treasure_Location");

                    isChasingTreasure = false;
                    lockedTarget = null;
                    arrivalRetryCount = 0;
                    currentMode = RobotMode.EXPLORE;
                    currentGoal = "Treasure collected! Continue exploring.";
                }
                else
                {
                    arrivalRetryCount++;
                    if (arrivalRetryCount >= MAX_ARRIVAL_RETRIES)
                    {
                        if (chatController != null)
                            chatController.AppendHistory("<color=orange>System:</color> Cannot reach treasure. Resuming.");
                        isChasingTreasure = false;
                        lockedTarget = null;
                        arrivalRetryCount = 0;
                        currentMode = RobotMode.EXPLORE;
                        currentGoal = "Treasure unreachable. Continue exploring.";
                    }
                    else
                    {
                        NavigateToTreasure();
                    }
                }
            }
            else
            {
                isChasingTreasure = false;
                arrivalRetryCount = 0;
                _ = PerformVisualScanAsync();
            }
            return;
        }

        arrivalRetryCount = 0;
        if (!isScanning && !isVisionBusy)
            _ = PerformVisualScanAsync();
    }

    private Transform FindBestMatchingLocation(string rawTarget)
    {
        if (string.IsNullOrEmpty(rawTarget)) return null;
        rawTarget = rawTarget.ToLower().Trim();

        foreach (Transform t in townMap.transform)
            if (t.name.ToLower() == rawTarget) return t;
        foreach (Transform t in townMap.transform)
            if (t.name.ToLower().Contains(rawTarget) || rawTarget.Contains(t.name.ToLower())) return t;
        return null;
    }

    // =====================================================================
    // --- 4. 浜や簰瀵硅瘽绯荤粺锛堜娇鐢ㄥ彲淇″害璁板繂锛?---
    // =====================================================================

    [System.Serializable]
    private class InteractionIntent
    {
        public string intent;
        public string target_location;
        public string reply;
    }

    public async void OnUserInteract(string userText)
    {
        if (experimentControlledStart)
        {
            Debug.LogWarning("[Experiment Agent] Interactive task changes are disabled during a formal trial.");
            return;
        }
        Debug.Log($"[User Input] {userText}");

        await AddLongTermMemoryAsync($"User asked: \"{userText}\"", currentLocationName);

        // 鈽?浣跨敤鍙俊搴﹀姞鏉?RAG
        lastPresentedEvidenceIds.Clear();
        string localContext = GetCredibilityWeightedContext(userText, 200);
        string chromaContext = "";
        if (UsesLongTermMemory && memoryClient != null)
        {
            if (UsesReliabilityAwareMethod)
            {
                chromaContext = await memoryClient.RetrieveReliabilityAwareMemories(
                    userText, transform.position, retrievalTopK,
                    retrievalTrustWeight, retrievalRecencyWeight, retrievalSpatialWeight,
                    reliabilityCalibrator != null ? reliabilityCalibrator.WriteThreshold : credibilityThreshold,
                    false);
            }
            else if (ActiveMethod == EmbodiedRagMethodCondition.C_HeuristicTrust)
            {
                chromaContext = await memoryClient.RetrieveHeuristicTrustMemories(
                    userText, transform.position, retrievalTopK, credibilityThreshold);
                chromaContext = FilterRagContext(chromaContext);
            }
            else
            {
                chromaContext = await memoryClient.RetrieveRelevantMemories(
                    userText, retrievalTopK, "", 0f);
            }
            foreach (string id in memoryClient.LastRetrievedMemoryIds)
                lastPresentedEvidenceIds.Add(id);
        }
        if (ExperimentRecorder != null)
            ExperimentRecorder.RecordRetrieval(userText, localContext, chromaContext);

        string combinedContext = UsesLongTermMemory
            ? $"=== Local Memories ===\n{localContext}\n=== Vector Memories ===\n{chromaContext}"
            : "No long-term memory is available in this condition.";
        string mapInfo = townMap.GetLocationListPrompt();

        // 鈽?璁板繂鍙俊搴︾粺璁★紝渚?LLM 鍙傝€?
        float avgCred = visualMemories.Count > 0
            ? visualMemories.Average(m => m.GetEffectiveCredibility(Time.time, decayRatePerMinute))
            : 0f;
        int totalMem = visualMemories.Count;
        int trustedMem = visualMemories.Count(m => m.GetEffectiveCredibility(Time.time, decayRatePerMinute) > 0.6f);

        var msgs = new List<LLMClient.Message>
        {
            new LLMClient.Message { role = "system", content =
                $"You are {robotName}, a smart AI robot dog. {personaDescription}\n" +
                $"CURRENT MODE: {currentMode}\n" +
                $"KNOWN LOCATIONS: {mapInfo}\n" +
                $"MEMORY STATUS: {totalMem} total memories, {trustedMem} trusted (>60% credibility), avg credibility: {avgCred:P0}\n" +
                $"YOUR MEMORIES:\n{combinedContext}\n\n" +
                $"INSTRUCTIONS:\n" +
                $"1. Determine intent: 'GO_TO_LOCATION', 'FIND_TREASURE', 'EXPLORE', or 'CHAT'.\n" +
                $"2. If 'GO_TO_LOCATION', set 'target_location' from KNOWN LOCATIONS.\n" +
                $"3. When replying, mention memory credibility if relevant (e.g., 'I'm fairly confident I saw...').\n" +
                $"4. OUTPUT JSON:\n" +
                $"{{\n  \"intent\": \"...\",\n  \"target_location\": \"...\",\n  \"reply\": \"...\"\n}}"
            },
            new LLMClient.Message { role = "user", content = userText }
        };

        try
        {
            if (ExperimentRecorder != null) ExperimentRecorder.OnLLMCall();
            string json = await llmClient.ChatAsync(msgs, 0.3f);

            json = json.Replace("```json", "").Replace("```", "").Trim();
            int s = json.IndexOf('{'); int e = json.LastIndexOf('}');
            if (s != -1 && e > s) json = json.Substring(s, e - s + 1);

            var settings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore };
            InteractionIntent parsedIntent = JsonConvert.DeserializeObject<InteractionIntent>(json, settings);
            if (parsedIntent == null) return;
            if (ExperimentRecorder != null)
                ExperimentRecorder.RecordDecision(
                    parsedIntent.intent, parsedIntent.target_location, parsedIntent.reply);

            await AddLongTermMemoryAsync($"I replied: {parsedIntent.reply}", currentLocationName);
            if (chatController != null)
                chatController.AppendHistory($"<color=cyan>{robotName}:</color> {parsedIntent.reply}");

            Debug.Log($"<color=magenta>[Intent]</color> {parsedIntent.intent}");

            switch (parsedIntent.intent)
            {
                case "FIND_TREASURE":
                    currentMode = RobotMode.FIND_TREASURE;
                    currentGoal = "Search for the yellow cube treasure!";
                    isChasingTreasure = false;
                    lockedTarget = null;

                    if (chatController != null)
                        chatController.AppendHistory("<color=yellow>Mode 鈫?FIND_TREASURE</color>");

                    bool foundDirectly = TryDirectTreasureSearch();
                    if (!foundDirectly)
                    {
                        _ = PerformVisualScanAsync();
                        RestartThinking();
                    }
                    break;

                case "EXPLORE":
                    currentMode = RobotMode.EXPLORE;
                    currentGoal = "Autonomously explore the unknown areas.";
                    isChasingTreasure = false;
                    lockedTarget = null;

                    if (chatController != null)
                        chatController.AppendHistory("<color=green>Mode 鈫?EXPLORE</color> (treasure tracking off)");

                    RestartThinking();
                    break;

                case "GO_TO_LOCATION":
                    Transform targetTF = townMap.GetWaypointByName(parsedIntent.target_location)
                                        ?? FindBestMatchingLocation(parsedIntent.target_location);
                    if (targetTF != null)
                    {
                        currentMode = RobotMode.GO_TO_LOCATION;
                        currentGoal = $"USER ORDER: Go to {targetTF.name}";
                        isChasingTreasure = false;
                        lockedTarget = null;
                        scheduler.StopImmediate();
                        currentLocationName = targetTF.name;
                        scheduler.MoveToLocation(targetTF.position);
                        RestartThinking();
                    }
                    else
                    {
                        if (chatController != null)
                            chatController.AppendHistory($"<color=red>System:</color> Cannot find '{parsedIntent.target_location}'.");
                    }
                    break;

                case "CHAT":
                default:
                    break;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Interact Error] {ex.Message}");
        }
    }

    private void RestartThinking()
    {
        if (!CanActNow()) return;
        scheduler.StopImmediate();
        isThinking = false;
        isWaitingForLLM = false;
        if (thinkingCoroutine != null) StopCoroutine(thinkingCoroutine);
        thinkingCoroutine = StartCoroutine(ThinkAndActRoutine());
    }
}
