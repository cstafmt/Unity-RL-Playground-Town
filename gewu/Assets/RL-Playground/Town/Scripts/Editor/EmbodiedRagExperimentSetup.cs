#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EmbodiedRagExperimentSetup
{
    private const string RigName = "VRAG Experiment Rig";

    [MenuItem("Tools/Embodied RAG/Install or Repair Experiment Rig")]
    public static void InstallOrRepair()
    {
        EnsureTag("Decoy");
        EnsureTag("Hider");
        RobotAgent_VRAG[] agents = UnityEngine.Object.FindObjectsOfType<RobotAgent_VRAG>();
        if (agents.Length != 1)
        {
            Debug.LogError("[VRAG Setup] Enable exactly one RobotAgent_VRAG. Found " + agents.Length + ".");
            return;
        }
        RobotAgent_VRAG agent = agents[0];
        GameObject rig = GameObject.Find(RigName);
        if (rig == null)
        {
            rig = new GameObject(RigName);
            Undo.RegisterCreatedObjectUndo(rig, "Create VRAG Experiment Rig");
        }

        ExperimentGroundTruthSensor sensor = GetOrAdd<ExperimentGroundTruthSensor>(rig);
        EmbodiedRagExperiment recorder = GetOrAdd<EmbodiedRagExperiment>(rig);
        ReliabilityCalibrator calibrator = GetOrAdd<ReliabilityCalibrator>(rig);
        ExperimentSetupValidator validator = GetOrAdd<ExperimentSetupValidator>(rig);
        EmbodiedRagAttackController attack = GetOrAdd<EmbodiedRagAttackController>(rig);
        ExperimentReplayRecorder replay = GetOrAdd<ExperimentReplayRecorder>(rig);
        ExperimentTrialController controller = GetOrAdd<ExperimentTrialController>(rig);

        AssignStableIdentities(EditorSceneManager.GetActiveScene());

        recorder.sceneName = EditorSceneManager.GetActiveScene().name;
        recorder.attackLevel = attack.CanonicalAttackName;
        recorder.enforceNoOracleLookup = true;
        recorder.requireHiderForCompletion = false;
        recorder.submitToServer = false;
        recorder.autoStartOnSceneLoad = false;
        recorder.seeker = agent.transform;
        recorder.groundTruthSensor = sensor;

        if (agent.robotEyes != null) sensor.visionCamera = agent.robotEyes.visionCamera;
        agent.experimentRecorder = recorder;
        agent.reliabilityCalibrator = calibrator;
        agent.formalCollectionContactDistance = 0.35f;
        agent.formalCollectionLineOfSightMask = ~0;
        replay.saveRgbFrames = true;
        replay.saveRawModelResponses = true;
        agent.replayRecorder = replay;
        agent.attackController = attack;
        agent.experimentControlledStart = true;

        string templatePath = AssetDatabase.GUIDToAssetPath("72a03d078fef4cf6af2c06ab11cba1e5");
        if (!string.IsNullOrEmpty(templatePath))
            calibrator.parametersAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(templatePath);

        attack.recorder = recorder;
        attack.sensor = sensor;

        controller.recorder = recorder;
        controller.validator = validator;
        controller.agent = agent;
        controller.memoryClient = agent.memoryClient;
        controller.visionClient = agent.visionClient;
        controller.attackController = attack;
        controller.replayRecorder = replay;
        controller.autoBeginOnStart = true;
        controller.requireHealthyBackend = true;
        controller.resetMemoryBeforeEveryTrial = true;
        controller.controlledPreludeObservations = 5;
        controller.maximumControlledObservationAttempts = 15;
        controller.controlledObservationIntervalSeconds = 0.25f;
        attack.requiredExposureCount = attack.staleExposureCount = controller.controlledPreludeObservations;

        validator.recorder = recorder;
        validator.agent = agent;
        validator.calibrator = calibrator;
        validator.trialController = controller;
        validator.attackController = attack;
        validator.replayRecorder = replay;
        validator.validateOnStart = false;
        validator.failClosed = true;

        EditorUtility.SetDirty(rig);
        EditorUtility.SetDirty(agent);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = rig;
        Debug.Log("[VRAG Setup] Formal rig installed. Configure object appearance and attack refs, save, then Validate Open Scene.");
    }

    [MenuItem("Tools/Embodied RAG/Validate Open Scene")]
    public static void ValidateOpenScene()
    {
        ExperimentSetupValidator validator = UnityEngine.Object.FindObjectOfType<ExperimentSetupValidator>();
        if (validator == null)
        {
            Debug.LogError("[VRAG Setup] No validator. Run Install or Repair first.");
            return;
        }
        validator.ValidateNow();
    }

    private static void AssignStableIdentities(Scene scene)
    {
        List<GameObject> targets = new List<GameObject>();
        foreach (GameObject value in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (value == null || value.scene != scene || EditorUtility.IsPersistent(value)) continue;
            if (HasTag(value, "Treasure") || HasTag(value, "Decoy")) targets.Add(value);
        }
        targets.Sort((a, b) => string.CompareOrdinal(HierarchyPath(a.transform), HierarchyPath(b.transform)));
        HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameObject value in targets)
        {
            ExperimentTargetIdentity existing = value.GetComponent<ExperimentTargetIdentity>();
            if (existing != null && !string.IsNullOrWhiteSpace(existing.ObjectId)) used.Add(existing.ObjectId);
        }
        int treasureIndex = 1, decoyIndex = 1;
        foreach (GameObject value in targets)
        {
            ExperimentTargetClass kind = HasTag(value, "Treasure")
                ? ExperimentTargetClass.Treasure : ExperimentTargetClass.Decoy;
            ExperimentTargetIdentity identity = GetOrAdd<ExperimentTargetIdentity>(value);
            if (string.IsNullOrWhiteSpace(identity.ObjectId))
            {
                string prefix = kind == ExperimentTargetClass.Treasure ? "T" : "D";
                int index = kind == ExperimentTargetClass.Treasure ? treasureIndex : decoyIndex;
                string id;
                do { id = prefix + "_" + Safe(scene.name) + "_" + index.ToString("D3"); index++; }
                while (used.Contains(id));
                if (kind == ExperimentTargetClass.Treasure) treasureIndex = index; else decoyIndex = index;
                identity.EditorAssignIdentity(id, kind);
                used.Add(id);
            }
            else identity.EditorAssignIdentity(identity.ObjectId, kind);
            EditorUtility.SetDirty(identity);
        }
    }

    private static T GetOrAdd<T>(GameObject host) where T : Component
    {
        T component = host.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(host);
    }

    private static bool HasTag(GameObject value, string tagName)
    { try { return value.CompareTag(tagName); } catch (UnityException) { return false; } }

    private static string HierarchyPath(Transform value)
    {
        string result = value.name;
        for (Transform parent = value.parent; parent != null; parent = parent.parent)
            result = parent.name + "/" + result;
        return result;
    }

    private static string Safe(string value)
    {
        if (string.IsNullOrEmpty(value)) return "scene";
        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-') chars[i] = '_';
        return new string(chars);
    }

    private static void EnsureTag(string tagName)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return;
        SerializedObject manager = new SerializedObject(assets[0]);
        SerializedProperty tags = manager.FindProperty("tags");
        for (int index = 0; index < tags.arraySize; index++)
            if (tags.GetArrayElementAtIndex(index).stringValue == tagName) return;
        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tagName;
        manager.ApplyModifiedProperties();
    }
}
#endif
