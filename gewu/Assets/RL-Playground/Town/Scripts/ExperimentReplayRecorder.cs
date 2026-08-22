using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// Persists fixed replay artifacts outside the compact event log. Raw image
/// bytes are stored as files, while JSONL contains hashes and relative paths.
/// </summary>
[DisallowMultipleComponent]
public sealed class ExperimentReplayRecorder : MonoBehaviour
{
    public bool saveRgbFrames = true;
    public bool saveRawModelResponses = true;
    [Tooltip("Set this from CI/build metadata. Never put credentials here.")]
    public string codeRevision = "unversioned";

    public bool IsReady { get; private set; }
    public string ReplayDirectory { get; private set; }

    private EmbodiedRagExperiment recorder;
    private string framesDirectory;
    private string visionDirectory;
    private string plannerDirectory;
    private string frameManifestPath;

    [Serializable]
    public sealed class FrameArtifact
    {
        public string frame_id;
        public string rgb_relative_path;
        public string rgb_sha256;
        public string prompt_sha256;
        public int image_bytes;
    }

    [Serializable]
    private sealed class FrameRecord
    {
        public string run_id;
        public string frame_id;
        public string utc_timestamp;
        public string method;
        public string scene;
        public string attack_level;
        public int seed;
        public string rgb_relative_path;
        public string rgb_sha256;
        public string prompt_sha256;
        public int image_bytes;
        public string vision_model;
        public float vision_temperature;
        public int vision_seed;
        public string code_revision;
        public string unity_version;
        public string application_version;
        public string prompt;
        public string raw_response;
        public ExperimentGroundTruthSensor.Snapshot ground_truth;
    }

    [Serializable]
    private sealed class PlannerRecord
    {
        public string run_id;
        public string decision_id;
        public string utc_timestamp;
        public string prompt_sha256;
        public string response_sha256;
        public string messages_json;
        public string raw_response;
        public string retrieval_ids;
        public bool counterfactual;
        public string mask_spec;
    }

    public void BeginTrial(EmbodiedRagExperiment experiment)
    {
        recorder = experiment;
        IsReady = recorder != null && recorder.IsRunning &&
            !string.IsNullOrEmpty(recorder.OutputDirectory);
        if (!IsReady) return;

        ReplayDirectory = Path.Combine(recorder.OutputDirectory, "replay");
        framesDirectory = Path.Combine(ReplayDirectory, "frames");
        visionDirectory = Path.Combine(ReplayDirectory, "vision");
        plannerDirectory = Path.Combine(ReplayDirectory, "planner");
        Directory.CreateDirectory(framesDirectory);
        Directory.CreateDirectory(visionDirectory);
        Directory.CreateDirectory(plannerDirectory);
        frameManifestPath = Path.Combine(ReplayDirectory, "frames.jsonl");
        WriteRunManifest();
    }

    public FrameArtifact RecordVisionFrame(string frameId, string base64Jpeg, string prompt,
        string rawResponse, ExperimentGroundTruthSensor.Snapshot truth, VisionClient client)
    {
        if (!IsReady || string.IsNullOrWhiteSpace(base64Jpeg)) return null;
        string safeId = Safe(frameId);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64Jpeg); }
        catch (FormatException) { return null; }

        FrameArtifact artifact = new FrameArtifact
        {
            frame_id = frameId,
            rgb_relative_path = "frames/" + safeId + ".jpg",
            rgb_sha256 = Sha256(bytes),
            prompt_sha256 = Sha256(prompt ?? ""),
            image_bytes = bytes.Length
        };
        if (saveRgbFrames)
            File.WriteAllBytes(Path.Combine(framesDirectory, safeId + ".jpg"), bytes);

        FrameRecord record = new FrameRecord
        {
            run_id = recorder.RunId,
            frame_id = frameId,
            utc_timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            method = recorder.CanonicalMethodName,
            scene = recorder.sceneName,
            attack_level = recorder.attackLevel,
            seed = recorder.randomSeed,
            rgb_relative_path = artifact.rgb_relative_path,
            rgb_sha256 = artifact.rgb_sha256,
            prompt_sha256 = artifact.prompt_sha256,
            image_bytes = artifact.image_bytes,
            vision_model = client != null ? client.modelName : "unknown",
            vision_temperature = client != null ? client.temperature : -1f,
            vision_seed = client != null ? client.seed : -1,
            code_revision = codeRevision,
            unity_version = Application.unityVersion,
            application_version = Application.version,
            prompt = prompt ?? "",
            raw_response = saveRawModelResponses ? rawResponse ?? "" : "",
            ground_truth = truth
        };
        string json = JsonUtility.ToJson(record);
        File.AppendAllText(frameManifestPath, json + Environment.NewLine, Encoding.UTF8);
        File.WriteAllText(Path.Combine(visionDirectory, safeId + ".json"),
            JsonUtility.ToJson(record, true), Encoding.UTF8);
        recorder.RecordFrameCaptured(frameId, artifact.rgb_relative_path, artifact.rgb_sha256,
            artifact.prompt_sha256, truth);
        return artifact;
    }

    public void RecordPlannerExchange(string decisionId, string messagesJson, string rawResponse,
        string retrievalIds, bool counterfactual = false, string maskSpec = "")
    {
        if (!IsReady) return;
        PlannerRecord record = new PlannerRecord
        {
            run_id = recorder.RunId,
            decision_id = decisionId,
            utc_timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            prompt_sha256 = Sha256(messagesJson ?? ""),
            response_sha256 = Sha256(rawResponse ?? ""),
            messages_json = messagesJson ?? "",
            raw_response = saveRawModelResponses ? rawResponse ?? "" : "",
            retrieval_ids = retrievalIds ?? "",
            counterfactual = counterfactual,
            mask_spec = maskSpec ?? ""
        };
        string suffix = counterfactual ? "__counterfactual" : "__factual";
        File.WriteAllText(Path.Combine(plannerDirectory, Safe(decisionId) + suffix + ".json"),
            JsonUtility.ToJson(record, true), Encoding.UTF8);
    }

    private void WriteRunManifest()
    {
        string manifest = "{\n" +
            "  \"schema_version\": 1,\n" +
            "  \"run_id\": \"" + Escape(recorder.RunId) + "\",\n" +
            "  \"method\": \"" + Escape(recorder.CanonicalMethodName) + "\",\n" +
            "  \"scene\": \"" + Escape(recorder.sceneName) + "\",\n" +
            "  \"attack_level\": \"" + Escape(recorder.attackLevel) + "\",\n" +
            "  \"seed\": " + recorder.randomSeed + ",\n" +
            "  \"code_revision\": \"" + Escape(codeRevision) + "\",\n" +
            "  \"unity_version\": \"" + Escape(Application.unityVersion) + "\",\n" +
            "  \"created_utc\": \"" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\"\n" +
            "}\n";
        File.WriteAllText(Path.Combine(ReplayDirectory, "manifest.json"), manifest, Encoding.UTF8);
    }

    private static string Sha256(byte[] value)
    {
        using (SHA256 algorithm = SHA256.Create())
        {
            byte[] digest = algorithm.ComputeHash(value ?? new byte[0]);
            StringBuilder builder = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; index++) builder.Append(digest[index].ToString("x2"));
            return builder.ToString();
        }
    }

    private static string Sha256(string value)
    {
        return Sha256(Encoding.UTF8.GetBytes(value ?? ""));
    }

    private static string Safe(string value)
    {
        if (string.IsNullOrEmpty(value)) return "unnamed";
        StringBuilder result = new StringBuilder(value.Length);
        foreach (char character in value)
            result.Append(char.IsLetterOrDigit(character) || character == '-' || character == '_'
                ? character : '_');
        return result.ToString();
    }

    private static string Escape(string value)
    {
        return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
