using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 挂载到场景中，自动记录实验指标
/// </summary>
public class ExperimentTracker : MonoBehaviour
{
    [Header("实验设置")]
    public string methodName = "D";     // A/B/C/D
    public string sceneName = "hard";   // easy/medium/hard
    public int trialId = 1;

    [Header("References")]
    public Transform seeker;
    public string serverUrl = "http://localhost:9230";

    // 跟踪数据
    private float startTime;
    private float totalPathLength;
    private Vector3 lastPosition;
    private int treasuresFound;
    private int decoysRejected;
    private int decoysFalseAccepted;
    private bool hiderFound;
    private float hiderFindTime;
    private int llmCalls;
    private int vlmCalls;
    private int totalObservations;
    private int hallucinationCount;
    private List<float> credibilityScores = new List<float>();

    private bool experimentRunning = true;

    void Start()
    {
        startTime = Time.time;
        lastPosition = seeker != null ? seeker.position : Vector3.zero;
    }

    void Update()
    {
        if (!experimentRunning || seeker == null) return;

        // 累计路径长度
        float dist = Vector3.Distance(seeker.position, lastPosition);
        if (dist > 0.1f && dist < 10f) // 过滤传送
        {
            totalPathLength += dist;
        }
        lastPosition = seeker.position;
    }

    // --- 外部调用接口 ---

    public void OnTreasureCollected()
    {
        treasuresFound++;
    }

    public void OnDecoyRejected()
    {
        decoysRejected++;
    }

    public void OnDecoyFalseAccepted()
    {
        decoysFalseAccepted++;
    }

    public void OnHiderFound()
    {
        hiderFound = true;
        hiderFindTime = Time.time - startTime;
    }

    public void OnLLMCall()
    {
        llmCalls++;
    }

    public void OnVLMCall(float credibility, bool isHallucination)
    {
        vlmCalls++;
        totalObservations++;
        credibilityScores.Add(credibility);
        if (isHallucination) hallucinationCount++;
    }

    public void OnExperimentComplete()
    {
        experimentRunning = false;
        StartCoroutine(SubmitResults());
    }

    /// <summary>
    /// 提交实验结果到 Python 服务器
    /// </summary>
    private IEnumerator SubmitResults()
    {
        float totalTime = Time.time - startTime;

        string json = JsonUtility.ToJson(new ExperimentResult
        {
            method = methodName,
            scene = sceneName,
            trial_id = trialId,
            total_time = totalTime,
            path_length = totalPathLength,
            treasures_found = treasuresFound,
            decoys_correctly_rejected = decoysRejected,
            decoys_falsely_accepted = decoysFalseAccepted,
            hider_found = hiderFound,
            hider_find_time = hiderFindTime,
            llm_calls = llmCalls,
            vlm_calls = vlmCalls,
            total_observations = totalObservations,
            hallucination_count = hallucinationCount,
        });

        using (UnityWebRequest www = new UnityWebRequest(
            $"{serverUrl}/experiment/submit", "POST"))
        {
            byte[] raw = Encoding.UTF8.GetBytes(json);
            www.uploadHandler = new UploadHandlerRaw(raw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.timeout = 30;
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
                Debug.Log($"<color=green>[Experiment]</color> Results submitted: {json}");
            else
                Debug.LogError($"[Experiment] Submit failed: {www.error}");
        }

        // 同时保存到本地文件
        string path = $"experiment_{methodName}_{sceneName}_{trialId}.json";
        System.IO.File.WriteAllText(path, json);
        Debug.Log($"[Experiment] Saved to {path}");
    }

    [System.Serializable]
    private class ExperimentResult
    {
        public string method;
        public string scene;
        public int trial_id;
        public float total_time;
        public float path_length;
        public int treasures_found;
        public int decoys_correctly_rejected;
        public int decoys_falsely_accepted;
        public bool hider_found;
        public float hider_find_time;
        public int llm_calls;
        public int vlm_calls;
        public int total_observations;
        public int hallucination_count;
    }
}