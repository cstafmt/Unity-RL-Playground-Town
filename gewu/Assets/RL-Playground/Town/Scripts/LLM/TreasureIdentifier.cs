using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// ★ 多宝物区分系统
///
/// 场景中存在三类物体：
///   真宝物 (Treasure) — Tag: Treasure
///   假宝物 (Decoy)    — Tag: Decoy
///   环境物品           — Tag: Furniture / Untagged
///
/// VLM 无法直接知道 Tag，只能通过视觉描述区分。
/// 本系统通过多次观察 + 特征匹配 + 可信度投票来判断物体真伪。
/// </summary>
public class TreasureIdentifier : MonoBehaviour
{
    [Header("Target Definition")]
    [Tooltip("真宝物的视觉特征描述")]
    public TreasureProfile trueTreasureProfile;

    [Header("Identification Settings")]
    [Tooltip("判定为真宝物所需的最低匹配分")]
    [Range(0f, 1f)] public float identificationThreshold = 0.6f;
    [Tooltip("需要多少次独立观察才做最终判定")]
    public int requiredObservations = 2;

    [Header("Credibility Model (观测可信度)")]
    [Tooltip("超过此距离观测可信度开始明显下降")]
    public float reliableDistance = 6f;
    [Tooltip("可信度衰减的最远距离，超过基本不可信")]
    public float maxUsefulDistance = 18f;

    [Header("Defense: Memory Quarantine")]
    [Tooltip("被判定为 decoy 后是否触发记忆检疫回调")]
    public bool enableQuarantine = true;
    // 由外部（如 RobotAgent）注入：传入一段被污染观测的特征文本，要求 RAG 隔离
    public System.Action<ObjectObservation> OnDecoyConfirmed;

    /// <summary>
    /// 宝物视觉特征档案
    /// </summary>
    [System.Serializable]
    public class TreasureProfile
    {
        public string primaryColor = "yellow";
        public string shape = "cube";
        public string[] additionalKeywords = { "bright", "square", "block", "box" };
        public string[] exclusionKeywords = { "round", "sphere", "cylinder", "barrel", "orange", "brown" };
    }

    /// <summary>
    /// 对一个物体的观察记录
    /// </summary>
    public class ObjectObservation
    {
        public string objectId;
        public Vector3 approximatePosition;
        public List<SingleObservation> observations = new List<SingleObservation>();
        public float identificationScore; // 综合判定分
        public string classification;      // "treasure", "decoy", "unknown", "furniture"
        public bool isFinalJudgment;       // 是否已做最终判定
    }

    public class SingleObservation
    {
        public string visionDescription;
        public float credibility;
        public float matchScore;
        public float timestamp;
    }

    // 已知物体的观察记录
    private Dictionary<string, ObjectObservation> knownObjects = new Dictionary<string, ObjectObservation>();

    /// <summary>
    /// ★ 真实可信度计算：替代之前外部凭空传入的 credibility。
    /// 可信度 = 距离因子 × 视角因子 × VLM自报置信度因子，全部归一化到 [0,1]。
    ///
    ///   distance        : 观测者到被观测物体的估计距离（米）
    ///   viewDotForward  : 物体方向与观测者前向的点积 (1=正前方, 0=侧面)，
    ///                     反映物体是否在画面中心 / 是否被边缘畸变影响
    ///   vlmConfidence   : VLM 返回的自评置信度 [0,1]；若无则传 1f
    /// </summary>
    public float ComputeCredibility(float distance, float viewDotForward, float vlmConfidence = 1f)
    {
        // 距离因子：reliableDistance 内≈1，之后线性衰减到 maxUsefulDistance 处≈0
        float distFactor = Mathf.Clamp01(
            1f - Mathf.Max(0f, distance - reliableDistance) / Mathf.Max(0.01f, maxUsefulDistance - reliableDistance)
        );

        // 视角因子：正前方最高；点积<0（背后）直接判 0
        float viewFactor = Mathf.Clamp01(viewDotForward);

        // VLM 置信度因子
        float confFactor = Mathf.Clamp01(vlmConfidence);

        // 任一维度极差都应拉低整体可信度，故用乘积；下限留一点底噪
        float credibility = distFactor * viewFactor * confFactor;
        return Mathf.Clamp01(credibility);
    }

    /// <summary>
    /// 便捷重载：自动从观测几何计算可信度后再分析。
    /// objectWorldPos 可用 EstimateObjectPosition 的结果或真值（仅用于算几何）。
    /// </summary>
    public ObjectObservation AnalyzeObservation(
        string visionDescription,
        Vector3 observerPosition,
        Vector3 observerForward,
        Vector3 objectWorldPos,
        float vlmConfidence = 1f)
    {
        float distance = Vector3.Distance(observerPosition, objectWorldPos);
        Vector3 dirToObj = (objectWorldPos - observerPosition).normalized;
        float dot = Vector3.Dot(observerForward.normalized, dirToObj);
        float credibility = ComputeCredibility(distance, dot, vlmConfidence);
        return AnalyzeObservation(visionDescription, credibility, observerPosition, observerForward);
    }

    /// <summary>
    /// ★ 核心方法：分析视觉描述，判断是否为真宝物
    /// </summary>
    public ObjectObservation AnalyzeObservation(
        string visionDescription,
        float credibility,
        Vector3 observerPosition,
        Vector3 observerForward)
    {
        // Step 1: 估算被观察物体的大致位置
        Vector3 approxPos = EstimateObjectPosition(
            visionDescription, observerPosition, observerForward
        );

        // Step 2: 查找是否已有这个物体的记录
        string objectId = FindOrCreateObjectRecord(approxPos);
        ObjectObservation record = knownObjects[objectId];

        // Step 3: 计算本次观察与真宝物特征的匹配度
        float matchScore = ComputeFeatureMatch(visionDescription);

        // Step 4: 记录本次观察
        var obs = new SingleObservation
        {
            visionDescription = visionDescription,
            credibility = credibility,
            matchScore = matchScore,
            timestamp = Time.time
        };
        record.observations.Add(obs);

        // Step 5: 如果观察次数足够，做最终判定
        if (record.observations.Count >= requiredObservations && !record.isFinalJudgment)
        {
            FinalizeClassification(record);
        }
        else
        {
            // 临时分类
            record.identificationScore = matchScore * credibility;
            record.classification = matchScore > identificationThreshold ? "likely_treasure" : "unknown";
        }

        Debug.Log($"<color=cyan>[Identifier]</color> Object {objectId}: " +
                  $"match={matchScore:F2}, cred={credibility:F2}, " +
                  $"class={record.classification}, " +
                  $"obs={record.observations.Count}/{requiredObservations}");

        return record;
    }

    /// <summary>
    /// ★ 特征匹配：将视觉描述与真宝物特征对比
    /// </summary>
    private float ComputeFeatureMatch(string description)
    {
        string lower = description.ToLower();
        float score = 0f;
        float maxScore = 0f;

        // --- 主颜色匹配（权重最高） ---
        maxScore += 0.35f;
        if (lower.Contains(trueTreasureProfile.primaryColor))
        {
            score += 0.35f;

            // 颜色精确性加分：如果只说了目标颜色而没有其他颜色
            string[] otherColors = { "orange", "red", "brown", "green", "blue", "purple" };
            bool hasOtherColor = otherColors.Any(c =>
                c != trueTreasureProfile.primaryColor && lower.Contains(c)
            );
            if (!hasOtherColor) score += 0.05f;
            maxScore += 0.05f;
        }

        // --- 形状匹配 ---
        maxScore += 0.30f;
        if (lower.Contains(trueTreasureProfile.shape))
        {
            score += 0.30f;
        }
        // 形状近似词
        else if (lower.Contains("box") || lower.Contains("block") || lower.Contains("square"))
        {
            score += 0.15f;
        }

        // --- 附加特征匹配 ---
        maxScore += 0.20f;
        int additionalMatches = trueTreasureProfile.additionalKeywords
            .Count(k => lower.Contains(k));
        score += 0.20f * Mathf.Min(1f, additionalMatches / 2f);

        // --- 排除特征（惩罚） ---
        int exclusionMatches = trueTreasureProfile.exclusionKeywords
            .Count(k => lower.Contains(k));
        float penalty = exclusionMatches * 0.15f;
        score = Mathf.Max(0f, score - penalty);

        // --- 归一化 ---
        return Mathf.Clamp01(score / maxScore);
    }

    /// <summary>
    /// ★ 最终判定：基于多次观察的加权投票
    /// </summary>
    private void FinalizeClassification(ObjectObservation record)
    {
        // 可信度加权投票
        float weightedSum = 0f;
        float weightTotal = 0f;

        foreach (var obs in record.observations)
        {
            float weight = obs.credibility;
            weightedSum += obs.matchScore * weight;
            weightTotal += weight;
        }

        float finalScore = weightTotal > 0 ? weightedSum / weightTotal : 0f;
        record.identificationScore = finalScore;

        // 一致性检查：如果多次观察结果差异很大，降低置信度
        if (record.observations.Count >= 2)
        {
            float variance = 0f;
            float mean = record.observations.Average(o => o.matchScore);
            foreach (var obs in record.observations)
            {
                float diff = obs.matchScore - mean;
                variance += diff * diff;
            }
            variance /= record.observations.Count;

            // 高方差 = 观察不一致 = 可能是干扰物
            if (variance > 0.1f)
            {
                finalScore *= 0.7f;
                Debug.Log($"<color=orange>[Identifier]</color> High variance ({variance:F3}), " +
                          $"reducing confidence.");
            }
        }

        // 最终分类
        if (finalScore >= identificationThreshold)
        {
            record.classification = "treasure";
        }
        else if (finalScore >= identificationThreshold * 0.6f)
        {
            record.classification = "suspicious";
        }
        else
        {
            record.classification = "decoy_or_furniture";
        }

        record.isFinalJudgment = true;

        Debug.Log($"<color=green>[Identifier]</color> FINAL: {record.objectId} → " +
                  $"{record.classification} (score={finalScore:F2})");

        // ★ 防御：判定为诱饵/家具时，触发记忆检疫，阻断 L2 污染放大
        if (enableQuarantine
            && record.classification == "decoy_or_furniture"
            && OnDecoyConfirmed != null)
        {
            Debug.Log($"<color=red>[Defense]</color> Quarantining memories of decoy {record.objectId}");
            OnDecoyConfirmed.Invoke(record);
        }
    }

    /// <summary>
    /// 根据位置查找或创建物体记录
    /// </summary>
    private string FindOrCreateObjectRecord(Vector3 position)
    {
        // 查找附近已有的记录（10 米内视为同一物体）
        foreach (var kvp in knownObjects)
        {
            if (Vector3.Distance(kvp.Value.approximatePosition, position) < 10f)
            {
                // 更新位置估计（取平均）
                kvp.Value.approximatePosition = Vector3.Lerp(
                    kvp.Value.approximatePosition, position, 0.3f
                );
                return kvp.Key;
            }
        }

        // 新物体
        string newId = $"obj_{knownObjects.Count}";
        knownObjects[newId] = new ObjectObservation
        {
            objectId = newId,
            approximatePosition = position,
            classification = "unknown"
        };
        return newId;
    }

    /// <summary>
    /// 估算被观察物体的位置
    /// </summary>
    private Vector3 EstimateObjectPosition(
        string description, Vector3 observerPos, Vector3 observerForward)
    {
        string lower = description.ToLower();
        Vector3 direction = observerForward;
        float estimatedDistance = 8f;

        if (lower.Contains("left"))
            direction = (observerForward - Vector3.Cross(Vector3.up, observerForward) * 0.5f).normalized;
        else if (lower.Contains("right"))
            direction = (observerForward + Vector3.Cross(Vector3.up, observerForward) * 0.5f).normalized;

        if (lower.Contains("near") || lower.Contains("close"))
            estimatedDistance = 5f;
        else if (lower.Contains("far") || lower.Contains("distance"))
            estimatedDistance = 15f;

        return observerPos + direction * estimatedDistance;
    }

    /// <summary>
    /// 获取所有被判定为真宝物的物体
    /// </summary>
    public List<ObjectObservation> GetConfirmedTreasures()
    {
        return knownObjects.Values
            .Where(o => o.classification == "treasure")
            .OrderByDescending(o => o.identificationScore)
            .ToList();
    }

    /// <summary>
    /// 获取可疑物体（需要进一步调查）
    /// </summary>
    public List<ObjectObservation> GetSuspiciousObjects()
    {
        return knownObjects.Values
            .Where(o => o.classification == "suspicious" || o.classification == "likely_treasure")
            .ToList();
    }

    /// <summary>
    /// 获取状态摘要（用于 LLM Prompt）
    /// </summary>
    public string GetStatusSummary()
    {
        var confirmed = GetConfirmedTreasures();
        var suspicious = GetSuspiciousObjects();
        int decoyCount = knownObjects.Values.Count(o => o.classification == "decoy_or_furniture");

        return $"Confirmed treasures: {confirmed.Count} | " +
               $"Suspicious: {suspicious.Count} | " +
               $"Identified decoys: {decoyCount} | " +
               $"Total objects tracked: {knownObjects.Count}";
    }
}