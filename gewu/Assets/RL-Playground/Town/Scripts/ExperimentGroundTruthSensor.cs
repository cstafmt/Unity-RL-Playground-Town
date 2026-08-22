using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Evaluation-only object visibility and claim matching; never expose to the policy.</summary>
[DisallowMultipleComponent]
public sealed class ExperimentGroundTruthSensor : MonoBehaviour
{
    public Camera visionCamera;
    public string treasureTag = "Treasure";
    public string decoyTag = "Decoy";
    public LayerMask occlusionMask = ~0;
    [Range(0f, 0.1f)] public float visibilityPadding = 0.02f;
    [Range(0f, 1f)] public float minimumVisibleFraction = 0.2f;
    [Range(0f, 0.1f)] public float minimumProjectedArea = 0.0005f;
    [Range(0f, 1f)] public float minimumIoU = 0.10f;
    [Range(0f, 0.25f)] public float maximumCenterDistance = 0.08f;
    [Range(0f, 0.25f)] public float ambiguityMargin = 0.10f;

    [Serializable]
    public sealed class VisibleObjectTruth
    {
        public string object_id, object_class, object_name, semantic_color, semantic_shape;
        public float bbox_xmin, bbox_ymin, bbox_xmax, bbox_ymax, center_u, center_v;
        public float distance_m, projected_area_ratio, visible_fraction;
        public bool matchable;
    }

    [Serializable]
    public sealed class Snapshot
    {
        public string frame_id = "";
        public int unity_frame, image_width, image_height;
        public Vector3 camera_position;
        public Quaternion camera_rotation;
        public float camera_fov;
        public bool treasure_visible, decoy_visible;
        public int treasure_visible_count, decoy_visible_count;
        public float nearest_treasure_distance = -1f, nearest_decoy_distance = -1f;
        public string visible_object_names = "";
        public VisibleObjectTruth[] objects = new VisibleObjectTruth[0];
    }

    [Serializable]
    public sealed class ClaimMatch
    {
        public int claim_index;
        public string claim_id, alignment_status = "unmatched", matched_object_id = "", matched_gt_class = "";
        public float match_iou, center_distance = -1f, best_match_score, second_match_score, ambiguity_margin;
    }

    private sealed class Pair
    {
        public int claimIndex, objectIndex;
        public float iou, distance, score;
        public string claimId, objectId;
    }

    public Snapshot CaptureSnapshot(string frameId = "")
    {
        EnsureCamera();
        Snapshot snapshot = new Snapshot { frame_id = frameId ?? "", unity_frame = Time.frameCount };
        if (visionCamera == null) return snapshot;
        snapshot.image_width = visionCamera.pixelWidth;
        snapshot.image_height = visionCamera.pixelHeight;
        snapshot.camera_position = visionCamera.transform.position;
        snapshot.camera_rotation = visionCamera.transform.rotation;
        snapshot.camera_fov = visionCamera.fieldOfView;
        List<VisibleObjectTruth> objects = new List<VisibleObjectTruth>();
        EvaluateTaggedObjects(treasureTag, ExperimentTargetClass.Treasure, snapshot, objects);
        EvaluateTaggedObjects(decoyTag, ExperimentTargetClass.Decoy, snapshot, objects);
        objects.Sort((a, b) => string.CompareOrdinal(a.object_id, b.object_id));
        snapshot.objects = objects.ToArray();
        snapshot.treasure_visible = snapshot.treasure_visible_count > 0;
        snapshot.decoy_visible = snapshot.decoy_visible_count > 0;
        List<string> names = new List<string>();
        foreach (VisibleObjectTruth item in objects) names.Add(item.object_class + ":" + item.object_id);
        snapshot.visible_object_names = string.Join(",", names.ToArray());
        return snapshot;
    }

    public List<ClaimMatch> MatchClaims(Snapshot snapshot, VisionObjectClaim[] claims)
    {
        VisionObjectClaim[] safeClaims = claims ?? new VisionObjectClaim[0];
        VisibleObjectTruth[] objects = snapshot != null && snapshot.objects != null
            ? snapshot.objects : new VisibleObjectTruth[0];
        List<ClaimMatch> results = new List<ClaimMatch>();
        List<Pair> pairs = new List<Pair>();
        for (int ci = 0; ci < safeClaims.Length; ci++)
        {
            VisionObjectClaim claim = safeClaims[ci];
            ClaimMatch result = new ClaimMatch { claim_index = ci, claim_id = claim != null ? claim.claim_id : "c" + ci };
            results.Add(result);
            if (claim == null) continue;
            List<Pair> candidates = new List<Pair>();
            for (int oi = 0; oi < objects.Length; oi++)
            {
                VisibleObjectTruth truth = objects[oi];
                if (truth == null || !truth.matchable) continue;
                float iou = IoU(claim, truth);
                float distance = Vector2.Distance(claim.Center, new Vector2(truth.center_u, truth.center_v));
                if (iou < minimumIoU && !(Inside(claim.Center, truth, visibilityPadding) &&
                    distance <= maximumCenterDistance)) continue;
                Pair pair = new Pair
                {
                    claimIndex = ci, objectIndex = oi, iou = iou, distance = distance,
                    score = 0.7f * iou + 0.3f * Mathf.Exp(-(distance * distance) / 0.02f),
                    claimId = claim.claim_id, objectId = truth.object_id
                };
                candidates.Add(pair); pairs.Add(pair);
            }
            candidates.Sort(ComparePairs);
            if (candidates.Count > 0)
            {
                result.best_match_score = candidates[0].score;
                result.second_match_score = candidates.Count > 1 ? candidates[1].score : 0f;
                result.ambiguity_margin = result.best_match_score - result.second_match_score;
                if (candidates.Count > 1 && result.ambiguity_margin < ambiguityMargin)
                    result.alignment_status = "ambiguous_overlap";
            }
        }
        pairs.Sort(ComparePairs);
        HashSet<int> usedClaims = new HashSet<int>(), usedObjects = new HashSet<int>();
        foreach (Pair pair in pairs)
        {
            ClaimMatch result = results[pair.claimIndex];
            if (result.alignment_status == "ambiguous_overlap" || usedClaims.Contains(pair.claimIndex) ||
                usedObjects.Contains(pair.objectIndex)) continue;
            usedClaims.Add(pair.claimIndex); usedObjects.Add(pair.objectIndex);
            VisibleObjectTruth truth = objects[pair.objectIndex];
            result.alignment_status = "matched"; result.matched_object_id = truth.object_id;
            result.matched_gt_class = truth.object_class; result.match_iou = pair.iou;
            result.center_distance = pair.distance;
        }
        return results;
    }

    private void EnsureCamera()
    {
        if (visionCamera != null) return;
        RobotCamera robotCamera = FindObjectOfType<RobotCamera>();
        if (robotCamera != null) visionCamera = robotCamera.visionCamera;
        if (visionCamera == null) visionCamera = Camera.main;
    }

    private void EvaluateTaggedObjects(string tagName, ExperimentTargetClass kind,
        Snapshot snapshot, List<VisibleObjectTruth> output)
    {
        foreach (GameObject value in FindObjectsWithTagSafe(tagName))
        {
            VisibleObjectTruth truth;
            if (!TryBuildTruth(value, kind, out truth)) continue;
            output.Add(truth);
            if (kind == ExperimentTargetClass.Treasure)
            {
                snapshot.treasure_visible_count++;
                if (snapshot.nearest_treasure_distance < 0f || truth.distance_m < snapshot.nearest_treasure_distance)
                    snapshot.nearest_treasure_distance = truth.distance_m;
            }
            else
            {
                snapshot.decoy_visible_count++;
                if (snapshot.nearest_decoy_distance < 0f || truth.distance_m < snapshot.nearest_decoy_distance)
                    snapshot.nearest_decoy_distance = truth.distance_m;
            }
        }
    }

    private bool TryBuildTruth(GameObject candidate, ExperimentTargetClass kind, out VisibleObjectTruth truth)
    {
        truth = null;
        if (candidate == null || !candidate.activeInHierarchy || visionCamera == null) return false;
        Renderer[] renderers = candidate.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return false;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        Vector3[] points = BoundsPoints(bounds);
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        int projected = 0, visible = 0;
        foreach (Vector3 point in points)
        {
            Vector3 viewport = visionCamera.WorldToViewportPoint(point);
            if (viewport.z <= 0f) continue;
            projected++;
            float topY = 1f - viewport.y;
            minX = Mathf.Min(minX, viewport.x); maxX = Mathf.Max(maxX, viewport.x);
            minY = Mathf.Min(minY, topY); maxY = Mathf.Max(maxY, topY);
            if (HasLineOfSight(candidate, point)) visible++;
        }
        if (projected == 0 || visible == 0 || maxX < -visibilityPadding || minX > 1f + visibilityPadding ||
            maxY < -visibilityPadding || minY > 1f + visibilityPadding) return false;
        minX = Mathf.Clamp01(minX); minY = Mathf.Clamp01(minY);
        maxX = Mathf.Clamp01(maxX); maxY = Mathf.Clamp01(maxY);
        float area = Mathf.Max(0f, maxX - minX) * Mathf.Max(0f, maxY - minY);
        float fraction = (float)visible / projected;
        ExperimentTargetIdentity identity = candidate.GetComponent<ExperimentTargetIdentity>();
        string id = identity != null && !string.IsNullOrWhiteSpace(identity.ObjectId)
            ? identity.ObjectId : "UNIDENTIFIED:" + HierarchyPath(candidate.transform);
        truth = new VisibleObjectTruth
        {
            object_id = id, object_class = kind == ExperimentTargetClass.Treasure ? "treasure" : "decoy",
            object_name = candidate.name, semantic_color = identity != null ? identity.semanticColor : "unknown",
            semantic_shape = identity != null ? identity.semanticShape : "unknown",
            bbox_xmin = minX, bbox_ymin = minY, bbox_xmax = maxX, bbox_ymax = maxY,
            center_u = (minX + maxX) * 0.5f, center_v = (minY + maxY) * 0.5f,
            distance_m = Vector3.Distance(visionCamera.transform.position, bounds.center),
            projected_area_ratio = area, visible_fraction = fraction,
            matchable = identity != null && !string.IsNullOrWhiteSpace(identity.ObjectId) &&
                area >= minimumProjectedArea && fraction >= minimumVisibleFraction
        };
        return true;
    }

    private bool HasLineOfSight(GameObject candidate, Vector3 point)
    {
        Vector3 direction = point - visionCamera.transform.position;
        float distance = direction.magnitude;
        if (distance <= 0.001f) return true;
        RaycastHit hit;
        if (!Physics.Raycast(visionCamera.transform.position, direction.normalized, out hit,
            distance + 0.25f, occlusionMask)) return true;
        return hit.transform == candidate.transform || hit.transform.IsChildOf(candidate.transform);
    }

    private static Vector3[] BoundsPoints(Bounds b)
    {
        Vector3 c = b.center, e = b.extents;
        return new[] { c, c+new Vector3(-e.x,-e.y,-e.z), c+new Vector3(-e.x,-e.y,e.z),
            c+new Vector3(-e.x,e.y,-e.z), c+new Vector3(-e.x,e.y,e.z), c+new Vector3(e.x,-e.y,-e.z),
            c+new Vector3(e.x,-e.y,e.z), c+new Vector3(e.x,e.y,-e.z), c+new Vector3(e.x,e.y,e.z) };
    }

    private static float IoU(VisionObjectClaim c, VisibleObjectTruth t)
    {
        float x1=Mathf.Max(c.bbox_xmin,t.bbox_xmin), y1=Mathf.Max(c.bbox_ymin,t.bbox_ymin);
        float x2=Mathf.Min(c.bbox_xmax,t.bbox_xmax), y2=Mathf.Min(c.bbox_ymax,t.bbox_ymax);
        float intersection=Mathf.Max(0f,x2-x1)*Mathf.Max(0f,y2-y1);
        float union=(c.bbox_xmax-c.bbox_xmin)*(c.bbox_ymax-c.bbox_ymin)+
            (t.bbox_xmax-t.bbox_xmin)*(t.bbox_ymax-t.bbox_ymin)-intersection;
        return union > 0f ? intersection/union : 0f;
    }

    private static bool Inside(Vector2 p, VisibleObjectTruth t, float d)
    { return p.x>=t.bbox_xmin-d && p.x<=t.bbox_xmax+d && p.y>=t.bbox_ymin-d && p.y<=t.bbox_ymax+d; }

    private static int ComparePairs(Pair a, Pair b)
    {
        int score=b.score.CompareTo(a.score); if(score!=0) return score;
        int claim=string.CompareOrdinal(a.claimId,b.claimId);
        return claim!=0 ? claim : string.CompareOrdinal(a.objectId,b.objectId);
    }

    private static GameObject[] FindObjectsWithTagSafe(string tagName)
    { try { return GameObject.FindGameObjectsWithTag(tagName); } catch(UnityException) { return new GameObject[0]; } }

    private static string HierarchyPath(Transform value)
    {
        string result=value!=null?value.name:"missing";
        for(Transform parent=value!=null?value.parent:null; parent!=null; parent=parent.parent)
            result=parent.name+"/"+result;
        return result;
    }
}
