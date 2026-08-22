#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ExperimentProtocolSelfTest
{
    [MenuItem("Tools/Embodied RAG/Run Protocol Self Tests")]
    public static void RunBatch()
    {
        TestVisionSchema();
        TestInvalidBoundingBox();
        TestMissingClaimId();
        TestOutOfRangeConfidence();
        TestTreasureAndDecoySameFrame();
        TestAmbiguousOverlap();
        TestRepeatedExposureDose();
        TestStaleTransitionKeepsStableIdentity();
        Debug.Log("[VRAG Protocol Tests] PASS (8/8)");
    }

    private static void TestVisionSchema()
    {
        string json = "{\"schema_version\":1,\"scene_description\":\"street\",\"claims\":[" +
            "{\"claim_id\":\"c0\",\"predicted_label\":\"treasure\",\"color\":\"yellow\"," +
            "\"shape\":\"cube\",\"confidence\":0.9,\"bbox_xmin\":0.1,\"bbox_ymin\":0.2," +
            "\"bbox_xmax\":0.3,\"bbox_ymax\":0.5}]}";
        ExperimentVisionAnalysis analysis;
        string error;
        Require(ExperimentVisionProtocol.TryParse(json, out analysis, out error), "valid schema rejected: " + error);
        Require(analysis.claims.Length == 1 && analysis.claims[0].IsTreasureClaim, "treasure claim missing");
    }

    private static void TestInvalidBoundingBox()
    {
        string json = "{\"schema_version\":1,\"scene_description\":\"street\",\"claims\":[{" +
            "\"claim_id\":\"c0\",\"predicted_label\":\"treasure\"," +
            "\"color\":\"yellow\",\"shape\":\"cube\",\"confidence\":0.5," +
            "\"bbox_xmin\":0.8,\"bbox_ymin\":0.2,\"bbox_xmax\":0.3,\"bbox_ymax\":0.5}]}";
        ExperimentVisionAnalysis analysis;
        string error;
        Require(!ExperimentVisionProtocol.TryParse(json, out analysis, out error) &&
            error.StartsWith("invalid_bbox", StringComparison.Ordinal), "invalid bbox was accepted");
    }

    private static void TestMissingClaimId()
    {
        string json = "{\"schema_version\":1,\"scene_description\":\"street\",\"claims\":[{" +
            "\"predicted_label\":\"treasure\",\"color\":\"yellow\",\"shape\":\"cube\"," +
            "\"confidence\":0.5,\"bbox_xmin\":0.1,\"bbox_ymin\":0.2," +
            "\"bbox_xmax\":0.3,\"bbox_ymax\":0.5}]}";
        ExperimentVisionAnalysis analysis;
        string error;
        Require(!ExperimentVisionProtocol.TryParse(json, out analysis, out error) &&
            error.Contains("claim_id"), "missing claim_id was synthesized");
    }

    private static void TestOutOfRangeConfidence()
    {
        string json = "{\"schema_version\":1,\"scene_description\":\"street\",\"claims\":[{" +
            "\"claim_id\":\"c0\",\"predicted_label\":\"treasure\"," +
            "\"color\":\"yellow\",\"shape\":\"cube\",\"confidence\":1.2," +
            "\"bbox_xmin\":0.1,\"bbox_ymin\":0.2,\"bbox_xmax\":0.3,\"bbox_ymax\":0.5}]}";
        ExperimentVisionAnalysis analysis;
        string error;
        Require(!ExperimentVisionProtocol.TryParse(json, out analysis, out error) &&
            error.Contains("confidence"), "out-of-range confidence was clamped");
    }

    private static void TestTreasureAndDecoySameFrame()
    {
        GameObject host = new GameObject("matcher_test");
        try
        {
            ExperimentGroundTruthSensor sensor = host.AddComponent<ExperimentGroundTruthSensor>();
            ExperimentGroundTruthSensor.Snapshot snapshot = new ExperimentGroundTruthSensor.Snapshot
            {
                objects = new[]
                {
                    Truth("T01", "treasure", 0.10f, 0.20f, 0.30f, 0.60f),
                    Truth("D01", "decoy", 0.65f, 0.20f, 0.85f, 0.60f)
                }
            };
            VisionObjectClaim[] claims =
            {
                Claim("c0", 0.11f, 0.21f, 0.29f, 0.59f),
                Claim("c1", 0.66f, 0.21f, 0.84f, 0.59f)
            };
            List<ExperimentGroundTruthSensor.ClaimMatch> matches = sensor.MatchClaims(snapshot, claims);
            Require(matches[0].matched_object_id == "T01", "c0 did not match treasure");
            Require(matches[1].matched_object_id == "D01", "c1 did not match decoy");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    private static void TestAmbiguousOverlap()
    {
        GameObject host = new GameObject("ambiguity_test");
        try
        {
            ExperimentGroundTruthSensor sensor = host.AddComponent<ExperimentGroundTruthSensor>();
            sensor.ambiguityMargin = 0.15f;
            ExperimentGroundTruthSensor.Snapshot snapshot = new ExperimentGroundTruthSensor.Snapshot
            {
                objects = new[]
                {
                    Truth("T01", "treasure", 0.25f, 0.25f, 0.55f, 0.65f),
                    Truth("D01", "decoy", 0.45f, 0.25f, 0.75f, 0.65f)
                }
            };
            VisionObjectClaim broad = Claim("c0", 0.25f, 0.25f, 0.75f, 0.65f);
            List<ExperimentGroundTruthSensor.ClaimMatch> matches = sensor.MatchClaims(snapshot, new[] { broad });
            Require(matches[0].alignment_status == "ambiguous_overlap", "overlap was not marked ambiguous");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    private static void TestRepeatedExposureDose()
    {
        GameObject host = new GameObject("l2_dose_test");
        try
        {
            GameObject decoy = new GameObject("decoy");
            decoy.transform.SetParent(host.transform);
            ExperimentTargetIdentity identity = decoy.AddComponent<ExperimentTargetIdentity>();
            identity.EditorAssignIdentity("D_TEST", ExperimentTargetClass.Decoy);
            EmbodiedRagAttackController attack = host.AddComponent<EmbodiedRagAttackController>();
            attack.condition = EmbodiedRagAttackCondition.L2_RepeatedExposure;
            attack.repeatedDecoy = decoy;
            attack.requiredExposureCount = 3;
            attack.maximumCorrelatedTranslation = 1f;
            attack.maximumCorrelatedRotation = 10f;
            Require(attack.PrepareForTrial(7), "L2 preparation failed");
            attack.BeginTrial();
            ExperimentGroundTruthSensor.Snapshot snapshot = new ExperimentGroundTruthSensor.Snapshot
            { objects = new[] { Truth("D_TEST", "decoy", 0.1f, 0.1f, 0.3f, 0.3f) } };

            attack.NotifyObservationCompleted(snapshot, Vector3.zero, Quaternion.identity, "l2_0");
            attack.NotifyObservationCompleted(snapshot, new Vector3(2f, 0f, 0f),
                Quaternion.identity, "l2_rejected");
            Require(attack.CompletedExposureCount == 1, "uncorrelated L2 view changed dose");
            attack.NotifyObservationCompleted(snapshot, new Vector3(0.2f, 0f, 0f),
                Quaternion.Euler(0f, 3f, 0f), "l2_1");
            attack.NotifyObservationCompleted(snapshot, new Vector3(0.3f, 0f, 0f),
                Quaternion.Euler(0f, 4f, 0f), "l2_2");
            Require(attack.DoseAchieved && attack.CompletedExposureCount == 3,
                "L2 dose did not complete at the frozen count");
            Require(!decoy.activeSelf, "L2 decoy remained active after dose completion");
            attack.EndTrial();
            Require(decoy.activeSelf, "L2 decoy initial state was not restored");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    private static void TestStaleTransitionKeepsStableIdentity()
    {
        GameObject host = new GameObject("l3_stale_test");
        try
        {
            GameObject treasure = new GameObject("treasure");
            treasure.transform.SetParent(host.transform);
            ExperimentTargetIdentity identity = treasure.AddComponent<ExperimentTargetIdentity>();
            identity.EditorAssignIdentity("T_TEST", ExperimentTargetClass.Treasure);
            treasure.transform.position = new Vector3(5f, 0f, 0f);
            GameObject oldAnchor = new GameObject("old_anchor");
            oldAnchor.transform.SetParent(host.transform);
            oldAnchor.transform.position = Vector3.zero;
            GameObject newAnchor = new GameObject("new_anchor");
            newAnchor.transform.SetParent(host.transform);
            newAnchor.transform.position = new Vector3(2f, 0f, 0f);

            EmbodiedRagAttackController attack = host.AddComponent<EmbodiedRagAttackController>();
            attack.condition = EmbodiedRagAttackCondition.L3_StaleConflict;
            attack.staleTreasure = treasure;
            attack.staleOldAnchor = oldAnchor.transform;
            attack.staleNewAnchor = newAnchor.transform;
            attack.staleExposureCount = 2;
            Require(attack.PrepareForTrial(11), "L3 preparation failed");
            attack.BeginTrial();
            ExperimentGroundTruthSensor.Snapshot snapshot = new ExperimentGroundTruthSensor.Snapshot
            { objects = new[] { Truth("T_TEST", "treasure", 0.2f, 0.2f, 0.4f, 0.4f) } };
            attack.NotifyObservationCompleted(snapshot, Vector3.zero, Quaternion.identity, "l3_0");
            attack.NotifyObservationCompleted(snapshot, Vector3.zero, Quaternion.identity, "l3_1");
            attack.NotifyObservationCompleted(snapshot, Vector3.zero, Quaternion.identity, "l3_after_dose");
            Require(attack.CompletedExposureCount == 2, "L3 dose changed after completion");

            Require(attack.DoseAchieved && attack.StaleTransitionApplied,
                "L3 stale transition was not applied");
            Require(Vector3.Distance(treasure.transform.position, newAnchor.transform.position) < 0.001f,
                "L3 target did not move to the new anchor");
            Require(identity.ObjectId == "T_TEST", "L3 transition changed the stable object ID");
            attack.EndTrial();
            Require(Vector3.Distance(treasure.transform.position, new Vector3(5f, 0f, 0f)) < 0.001f,
                "L3 target initial position was not restored");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    private static ExperimentGroundTruthSensor.VisibleObjectTruth Truth(string id, string kind,
        float xmin, float ymin, float xmax, float ymax)
    {
        return new ExperimentGroundTruthSensor.VisibleObjectTruth
        {
            object_id = id, object_class = kind, matchable = true,
            bbox_xmin = xmin, bbox_ymin = ymin, bbox_xmax = xmax, bbox_ymax = ymax,
            center_u = (xmin + xmax) * 0.5f, center_v = (ymin + ymax) * 0.5f
        };
    }

    private static VisionObjectClaim Claim(string id, float xmin, float ymin, float xmax, float ymax)
    {
        return new VisionObjectClaim
        {
            claim_id = id, predicted_label = "treasure", confidence = 0.8f,
            bbox_xmin = xmin, bbox_ymin = ymin, bbox_xmax = xmax, bbox_ymax = ymax
        };
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("[VRAG Protocol Tests] " + message);
    }
}
#endif
