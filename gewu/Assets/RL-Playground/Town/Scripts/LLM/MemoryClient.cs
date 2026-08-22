using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Client for structured embodied memories. Vanilla and reliability-aware retrieval use the
/// same stored records, so experimental conditions differ only in declared retrieval policy.
/// </summary>
public class MemoryClient : MonoBehaviour
{
    [Header("Server Config")]
    public string serverUrl = "http://localhost:8000";

    [Serializable]
    public class StructuredMemoryRecord
    {
        public string memory_id;
        public string run_id;
        public string observation_id;
        public string content;
        public string location;
        public string timestamp;
        public double created_unix;
        public string category = "observation";
        public string source_model = "unknown";
        public string status = "accepted";
        public float credibility = 1f;
        public float raw_credibility = 1f;
        public float position_x;
        public float position_y;
        public float position_z;
        public int confirmation_count;
        public int contradiction_count;
    }

    [Serializable]
    private class QueryPayload
    {
        public string query_text;
        public int n_results;
        public string filter_location;
        public string retrieval_mode;
        public float trust_weight;
        public float recency_weight;
        public float spatial_weight;
        public float min_credibility;
        public bool include_candidates;
        public float query_x;
        public float query_y;
        public float query_z;
    }

    [Serializable]
    private class StatusUpdatePayload
    {
        public string memory_id;
        public string status;
        public float credibility;
        public int confirmation_count;
        public int contradiction_count;
        public string reason;
    }

    [Serializable]
    private class AddResponse
    {
        public string status;
        public string id;
    }

    [Serializable]
    private class ServerResponseList
    {
        public List<ServerResponseMemoryItem> results;
    }

    [Serializable]
    public class ServerResponseMemoryItem
    {
        public string memory_id;
        public string content;
        public string location;
        public string timestamp;
        public string category;
        public string source_model;
        public string status;
        public float credibility;
        public float raw_credibility;
        public float distance;
        public float semantic_score;
        public float trust_score;
        public float recency_score;
        public float spatial_score;
        public float final_score;
        public int rank;
    }

    private readonly List<string> lastRetrievedMemoryIds = new List<string>();
    public IReadOnlyList<string> LastRetrievedMemoryIds { get { return lastRetrievedMemoryIds; } }

    public async Task AddMemoryAsync(string content, string location)
    {
        StructuredMemoryRecord record = new StructuredMemoryRecord
        {
            content = content,
            location = location,
            timestamp = DateTime.UtcNow.ToString("o"),
            created_unix = UnixTimeNow(),
            category = "dialogue_or_system",
            source_model = "system",
            status = "accepted",
            credibility = 1f,
            raw_credibility = 1f
        };
        await AddStructuredMemoryAsync(record);
    }

    public async Task<string> AddStructuredMemoryAsync(StructuredMemoryRecord record)
    {
        if (record == null || string.IsNullOrEmpty(record.content)) return "";
        if (string.IsNullOrEmpty(record.timestamp)) record.timestamp = DateTime.UtcNow.ToString("o");
        if (record.created_unix <= 0.0) record.created_unix = UnixTimeNow();

        string responseText = await SendPostRequest(
            serverUrl + "/memory/add", JsonUtility.ToJson(record));
        if (string.IsNullOrEmpty(responseText)) return "";
        try
        {
            AddResponse response = JsonUtility.FromJson<AddResponse>(responseText);
            return response != null ? response.id : "";
        }
        catch (Exception error)
        {
            Debug.LogError("[MemoryClient] Add response parse error: " + error.Message);
            return "";
        }
    }

    public Task<string> RetrieveRelevantMemories(
        string queryText,
        int limit = 5,
        string filterLocation = "",
        float recencyWeight = 0f)
    {
        return RetrieveAsync(queryText, limit, filterLocation, "semantic", Vector3.zero,
            0f, recencyWeight, 0f, 0f, true);
    }

    public Task<string> RetrieveHeuristicTrustMemories(
        string queryText,
        Vector3 queryPosition,
        int limit = 5,
        float minCredibility = 0.3f)
    {
        return RetrieveAsync(queryText, limit, "", "heuristic_trust", queryPosition,
            0f, 0f, 0f, minCredibility, true);
    }

    public Task<string> RetrieveReliabilityAwareMemories(
        string queryText,
        Vector3 queryPosition,
        int limit = 5,
        float trustWeight = 0.35f,
        float recencyWeight = 0.10f,
        float spatialWeight = 0.10f,
        float minCredibility = 0.25f,
        bool includeCandidates = false)
    {
        return RetrieveAsync(queryText, limit, "", "reliability_aware", queryPosition,
            trustWeight, recencyWeight, spatialWeight, minCredibility, includeCandidates);
    }

    private async Task<string> RetrieveAsync(
        string queryText,
        int limit,
        string filterLocation,
        string retrievalMode,
        Vector3 queryPosition,
        float trustWeight,
        float recencyWeight,
        float spatialWeight,
        float minCredibility,
        bool includeCandidates)
    {
        lastRetrievedMemoryIds.Clear();
        QueryPayload payload = new QueryPayload
        {
            query_text = queryText,
            n_results = Mathf.Max(1, limit),
            filter_location = filterLocation,
            retrieval_mode = retrievalMode,
            trust_weight = Mathf.Clamp01(trustWeight),
            recency_weight = Mathf.Clamp01(recencyWeight),
            spatial_weight = Mathf.Clamp01(spatialWeight),
            min_credibility = Mathf.Clamp01(minCredibility),
            include_candidates = includeCandidates,
            query_x = queryPosition.x,
            query_y = queryPosition.y,
            query_z = queryPosition.z
        };

        string responseText = await SendPostRequest(
            serverUrl + "/memory/query", JsonUtility.ToJson(payload));
        if (string.IsNullOrEmpty(responseText)) return "";

        try
        {
            ServerResponseList data = JsonUtility.FromJson<ServerResponseList>(responseText);
            if (data == null || data.results == null || data.results.Count == 0) return "";

            StringBuilder context = new StringBuilder();
            foreach (ServerResponseMemoryItem memory in data.results)
            {
                if (!string.IsNullOrEmpty(memory.memory_id))
                    lastRetrievedMemoryIds.Add(memory.memory_id);
                if (retrievalMode == "semantic")
                {
                    context.AppendLine(
                        $"- [MID:{memory.memory_id}|Rank:{memory.rank}|Sem:{memory.semantic_score:F3}] " +
                        $"[Time:{memory.timestamp}|Loc:{memory.location}] {memory.content}");
                }
                else if (retrievalMode == "heuristic_trust")
                {
                    context.AppendLine(
                        $"- [MID:{memory.memory_id}|Rank:{memory.rank}|Score:{memory.final_score:F3}|" +
                        $"Sem:{memory.semantic_score:F3}|C:{memory.credibility:F3}] " +
                        $"[Time:{memory.timestamp}|Loc:{memory.location}] {memory.content}");
                }
                else
                {
                    context.AppendLine(
                        $"- [MID:{memory.memory_id}|Rank:{memory.rank}|Score:{memory.final_score:F3}|" +
                        $"Sem:{memory.semantic_score:F3}|C:{memory.credibility:F3}|State:{memory.status}] " +
                        $"[Time:{memory.timestamp}|Loc:{memory.location}] {memory.content}");
                }
            }
            return context.ToString();
        }
        catch (Exception error)
        {
            Debug.LogError("[MemoryClient] Query response parse error: " + error.Message);
            return "";
        }
    }

    public async Task<bool> UpdateMemoryStatusAsync(
        string memoryId,
        string status,
        float credibility,
        int confirmationCount,
        int contradictionCount,
        string reason)
    {
        if (string.IsNullOrEmpty(memoryId)) return false;
        StatusUpdatePayload payload = new StatusUpdatePayload
        {
            memory_id = memoryId,
            status = status,
            credibility = Mathf.Clamp01(credibility),
            confirmation_count = Mathf.Max(0, confirmationCount),
            contradiction_count = Mathf.Max(0, contradictionCount),
            reason = reason ?? ""
        };
        string response = await SendPostRequest(
            serverUrl + "/memory/update-status", JsonUtility.ToJson(payload));
        return !string.IsNullOrEmpty(response);
    }

    private async Task<string> SendPostRequest(string url, string jsonBody)
    {
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 30;

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[MemoryClient] {url}: {request.error}; response={request.downloadHandler.text}");
                return null;
            }
            return request.downloadHandler.text;
        }
    }

    private static double UnixTimeNow()
    {
        return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
    }
}
