using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Threading.Tasks;

public class VisionClient : MonoBehaviour
{
    [Header("Server Config")]
    public string serverUrl = "http://localhost:8000";
    public string modelName = "llava";

    [Header("Reproducible inference")]
    public int seed = 42;
    [Range(0f, 2f)] public float temperature = 0f;

    [Serializable]
    private class VisionRequest
    {
        public string model_name;
        public string image_base64;
        public string prompt;
        public int seed;
        public float temperature;
    }

    [Serializable]
    public class VisionResponse
    {
        public string description;
    }

    [Serializable]
    private class HealthResponse
    {
        public string status;
        public bool healthy;
        public string[] issues;
    }

    public async Task<string> AnalyzeImageAsync(
        string base64Image,
        string taskInstruction = "Describe the scene concisely.")
    {
        if (!await IsServerHealthy())
        {
            Debug.LogWarning("[VisionClient] Server is not healthy; observation rejected.");
            return "";
        }

        VisionRequest payload = new VisionRequest
        {
            model_name = modelName,
            image_base64 = base64Image,
            prompt = taskInstruction,
            seed = seed,
            temperature = temperature
        };

        using (UnityWebRequest request = new UnityWebRequest(serverUrl + "/vision", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(
                Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 95;

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[VisionClient] {request.error}; response={request.downloadHandler.text}");
                return "";
            }

            try
            {
                VisionResponse response = JsonUtility.FromJson<VisionResponse>(request.downloadHandler.text);
                return response != null ? response.description : "";
            }
            catch (Exception error)
            {
                Debug.LogError("[VisionClient] Parse error: " + error.Message);
                return "";
            }
        }
    }

    private async Task<bool> IsServerHealthy()
    {
        try
        {
            using (UnityWebRequest request = UnityWebRequest.Get(serverUrl + "/health"))
            {
                request.timeout = 5;
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();
                if (request.result != UnityWebRequest.Result.Success) return false;
                HealthResponse response = JsonUtility.FromJson<HealthResponse>(request.downloadHandler.text);
                return response != null && response.healthy;
            }
        }
        catch (Exception error)
        {
            Debug.LogWarning("[VisionClient] Health check failed: " + error.Message);
            return false;
        }
    }
}
