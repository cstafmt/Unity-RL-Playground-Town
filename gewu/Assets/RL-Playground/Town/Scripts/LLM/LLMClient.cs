using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class LLMClient : MonoBehaviour
{
    [Header("API Settings")]
    public string apiKey = "";
    public string apiKeyEnvironmentVariable = "DEEPSEEK_API_KEY";
    public string apiUrl = "https://api.deepseek.com/v1/chat/completions"; // 可替换为本地模型地址
    public string modelName = "deepseek-chat";

    [System.Serializable]
    public class Message
    {
        public string role; // "system", "user", "assistant"
        public string content;
    }

    // 封装请求体
    private class RequestBody
    {
        public string model;
        public List<Message> messages;
        public float temperature;
    }

    // 封装响应体
    private class ResponseBody
    {
        public Choice[] choices;
    }

    private class Choice
    {
        public Message message;
    }

    public bool HasConfiguredApiKey { get { return !string.IsNullOrWhiteSpace(ResolveApiKey()); } }

    private string ResolveApiKey()
    {
        string fromEnvironment = string.IsNullOrWhiteSpace(apiKeyEnvironmentVariable)
            ? "" : System.Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(fromEnvironment)
            ? fromEnvironment.Trim() : (apiKey ?? "").Trim();
    }

    public async Task<string> ChatAsync(List<Message> conversationHistory, float temperature = 0.7f)
    {
        string resolvedApiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(resolvedApiKey))
        {
            Debug.LogError("LLM API key missing. Set DEEPSEEK_API_KEY or configure an untracked local key.");
            return null;
        }

        RequestBody reqBody = new RequestBody
        {
            model = modelName,
            messages = conversationHistory,
            temperature = temperature
        };

        string json = JsonConvert.SerializeObject(reqBody);
        Debug.Log($"[LLM Request]: {json}");

        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + resolvedApiKey);
            request.timeout = 90;

            var operation = request.SendWebRequest();

            while (!operation.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"LLM Error: {request.error}\nResponse: {request.downloadHandler.text}");
                return null;
            }

            var response = JsonConvert.DeserializeObject<ResponseBody>(request.downloadHandler.text);
            return response.choices[0].message.content;
        }
    }
}
