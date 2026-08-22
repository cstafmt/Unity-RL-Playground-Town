using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Versioned object-level response expected from the VLM. Coordinates are
/// normalized xyxy with the image top-left as (0,0).
/// </summary>
[Serializable]
public sealed class ExperimentVisionAnalysis
{
    public int schema_version = 1;
    public string scene_description = "";
    public VisionObjectClaim[] claims = new VisionObjectClaim[0];
}

[Serializable]
public sealed class VisionObjectClaim
{
    public string claim_id = "";
    public string predicted_label = "unknown";
    public string color = "unknown";
    public string shape = "unknown";
    public float confidence;
    public float bbox_xmin;
    public float bbox_ymin;
    public float bbox_xmax;
    public float bbox_ymax;

    public bool IsTreasureClaim
    {
        get { return string.Equals(predicted_label, "treasure", StringComparison.OrdinalIgnoreCase); }
    }

    public Vector2 Center
    {
        get { return new Vector2((bbox_xmin + bbox_xmax) * 0.5f, (bbox_ymin + bbox_ymax) * 0.5f); }
    }
}

public static class ExperimentVisionProtocol
{
    public const int SchemaVersion = 1;

    public static bool TryParse(string raw, out ExperimentVisionAnalysis analysis, out string error)
    {
        analysis = null;
        error = "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "empty_response";
            return false;
        }

        string json = raw.Replace("```json", "").Replace("```", "").Trim();
        int first = json.IndexOf('{');
        int last = json.LastIndexOf('}');
        if (first < 0 || last <= first)
        {
            error = "missing_json_object";
            return false;
        }
        json = json.Substring(first, last - first + 1);
        if (!TryValidateRawSchema(json, out error)) return false;

        try { analysis = JsonUtility.FromJson<ExperimentVisionAnalysis>(json); }
        catch (Exception exception)
        {
            error = "json_parse_error:" + exception.GetType().Name;
            return false;
        }

        if (analysis == null || analysis.schema_version != SchemaVersion)
        {
            error = "unsupported_schema_version";
            return false;
        }
        if (analysis.claims == null) analysis.claims = new VisionObjectClaim[0];

        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < analysis.claims.Length; index++)
        {
            VisionObjectClaim claim = analysis.claims[index];
            if (claim == null)
            {
                error = "null_claim_" + index;
                return false;
            }
            if (string.IsNullOrWhiteSpace(claim.claim_id))
            {
                error = "missing_claim_id:" + index;
                return false;
            }
            claim.claim_id = claim.claim_id.Trim();
            if (!ids.Add(claim.claim_id))
            {
                error = "duplicate_claim_id:" + claim.claim_id;
                return false;
            }
            claim.predicted_label = (claim.predicted_label ?? "unknown").Trim().ToLowerInvariant();
            if (claim.predicted_label != "treasure" && claim.predicted_label != "decoy" &&
                claim.predicted_label != "unknown")
            {
                error = "invalid_label:" + claim.predicted_label;
                return false;
            }
            if (!Finite01(claim.bbox_xmin) || !Finite01(claim.bbox_ymin) ||
                !Finite01(claim.bbox_xmax) || !Finite01(claim.bbox_ymax) ||
                claim.bbox_xmax <= claim.bbox_xmin || claim.bbox_ymax <= claim.bbox_ymin)
            {
                error = "invalid_bbox:" + claim.claim_id;
                return false;
            }
            if (!Finite01(claim.confidence))
            {
                error = "invalid_confidence:" + claim.claim_id;
                return false;
            }
        }
        return true;
    }

    private static bool TryValidateRawSchema(string json, out string error)
    {
        error = "";
        JObject root;
        try { root = JObject.Parse(json); }
        catch (Exception exception)
        {
            error = "strict_json_parse_error:" + exception.GetType().Name;
            return false;
        }
        if (root["schema_version"] == null || root["schema_version"].Type != JTokenType.Integer)
        { error = "missing_or_invalid_field:schema_version"; return false; }
        if (root["scene_description"] == null || root["scene_description"].Type != JTokenType.String)
        { error = "missing_or_invalid_field:scene_description"; return false; }
        if (root["claims"] == null || root["claims"].Type != JTokenType.Array)
        { error = "missing_or_invalid_field:claims"; return false; }
        JArray claims = (JArray)root["claims"];
        string[] stringFields = { "claim_id", "predicted_label", "color", "shape" };
        string[] numberFields = { "confidence", "bbox_xmin", "bbox_ymin", "bbox_xmax", "bbox_ymax" };
        for (int index = 0; index < claims.Count; index++)
        {
            JObject claim = claims[index] as JObject;
            if (claim == null) { error = "invalid_claim_object:" + index; return false; }
            foreach (string field in stringFields)
            {
                JToken token = claim[field];
                if (token == null || token.Type != JTokenType.String ||
                    (field == "claim_id" && string.IsNullOrWhiteSpace(token.Value<string>())))
                { error = "missing_or_invalid_field:claims[" + index + "]." + field; return false; }
            }
            foreach (string field in numberFields)
            {
                JToken token = claim[field];
                if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
                { error = "missing_or_invalid_field:claims[" + index + "]." + field; return false; }
                double value = token.Value<double>();
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d || value > 1d)
                { error = "out_of_range_field:claims[" + index + "]." + field; return false; }
            }
        }
        return true;
    }

    private static bool Finite01(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;
    }
}
