using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Fails closed when a scene would produce invalid or mislabeled formal runs.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-900)]
public sealed class ExperimentSetupValidator : MonoBehaviour
{
    public EmbodiedRagExperiment recorder;
    public RobotAgent_VRAG agent;
    public ReliabilityCalibrator calibrator;
    public ExperimentTrialController trialController;
    public EmbodiedRagAttackController attackController;
    public ExperimentReplayRecorder replayRecorder;
    public bool validateOnStart = false;
    public bool failClosed = true;
    [TextArea(4, 16)] public string lastReport = "not validated";
    public bool LastValidationPassed { get; private set; }

    private void Start()
    {
        if (validateOnStart) ValidateNow();
    }

    [ContextMenu("Validate Experiment Scene")]
    public bool ValidateNow()
    {
        Discover();
        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();
        RobotAgent_VRAG[] activeAgents = FindObjectsOfType<RobotAgent_VRAG>();
        EmbodiedRagExperiment[] activeRecorders = FindObjectsOfType<EmbodiedRagExperiment>();
        ExperimentTrialController[] controllers = FindObjectsOfType<ExperimentTrialController>();
        ExperimentSetupValidator[] validators = FindObjectsOfType<ExperimentSetupValidator>();
        EmbodiedRagAttackController[] attacks = FindObjectsOfType<EmbodiedRagAttackController>();
        ExperimentReplayRecorder[] replays = FindObjectsOfType<ExperimentReplayRecorder>();
        ExperimentGroundTruthSensor[] sensors = FindObjectsOfType<ExperimentGroundTruthSensor>();

        if (activeAgents.Length != 1)
            errors.Add("Exactly one active RobotAgent_VRAG is required; found " + activeAgents.Length + ".");
        if (activeRecorders.Length != 1)
            errors.Add("Exactly one active EmbodiedRagExperiment is required; found " + activeRecorders.Length + ".");
        if (controllers.Length != 1)
            errors.Add("Exactly one active ExperimentTrialController is required; found " + controllers.Length + ".");
        if (validators.Length != 1)
            errors.Add("Exactly one active ExperimentSetupValidator is required; found " + validators.Length + ".");
        if (attacks.Length != 1)
            errors.Add("Exactly one active EmbodiedRagAttackController is required; found " + attacks.Length + ".");
        if (replays.Length != 1)
            errors.Add("Exactly one active ExperimentReplayRecorder is required; found " + replays.Length + ".");
        if (sensors.Length != 1)
            errors.Add("Exactly one active ExperimentGroundTruthSensor is required; found " + sensors.Length + ".");
        if (activeAgents.Length == 1 && agent != activeAgents[0])
            errors.Add("Validator Agent reference does not point to the unique active agent.");
        if (activeRecorders.Length == 1 && recorder != activeRecorders[0])
            errors.Add("Validator Recorder reference does not point to the unique active recorder.");
        if (controllers.Length == 1 && trialController != controllers[0])
            errors.Add("Validator Trial Controller reference does not point to the unique active controller.");
        if (validators.Length == 1 && validators[0] != this)
            errors.Add("This validator is not the unique active validator.");
        if (attacks.Length == 1 && attackController != attacks[0])
            errors.Add("Validator Attack Controller reference does not point to the unique active controller.");
        if (replays.Length == 1 && replayRecorder != replays[0])
            errors.Add("Validator Replay Recorder reference does not point to the unique active recorder.");
        if (agent != null && !agent.isActiveAndEnabled)
            errors.Add("The formal RobotAgent_VRAG component must be active and enabled.");
        if (recorder != null && !recorder.isActiveAndEnabled)
            errors.Add("The formal experiment recorder must be active and enabled.");
        if (trialController != null && !trialController.isActiveAndEnabled)
            errors.Add("The formal trial controller must be active and enabled.");
        if (recorder == null) errors.Add("Experiment recorder is missing.");
        if (agent == null) errors.Add("RobotAgent_VRAG is missing.");
        if (trialController == null) errors.Add("ExperimentTrialController is missing.");
        if (replayRecorder == null) errors.Add("ExperimentReplayRecorder is missing.");
        if (attackController == null) errors.Add("EmbodiedRagAttackController is missing.");

        if (recorder != null)
        {
            if (!recorder.enforceNoOracleLookup)
                errors.Add("Enforce No Oracle Lookup must be enabled.");
            if (recorder.autoStartOnSceneLoad)
                errors.Add("Recorder Auto Start On Scene Load must be disabled; the trial controller owns startup.");
            if (recorder.IsRunning)
                errors.Add("Recorder was already running during validation.");
        }

        if (agent != null)
        {
            if (agent.experimentRecorder != recorder)
                errors.Add("RobotAgent_VRAG.Experiment Recorder is not bound to this recorder.");
            if (!agent.experimentControlledStart)
                errors.Add("RobotAgent_VRAG Experiment Controlled Start must be enabled.");
            if (agent.IsAutonomyRunning)
                errors.Add("Agent autonomy started before validation.");
            if (agent.robotEyes == null || agent.robotEyes.visionCamera == null)
                errors.Add("Robot camera/vision camera is not assigned.");
            if (agent.visionClient == null) errors.Add("VisionClient is missing.");
            if (agent.llmClient == null) errors.Add("LLMClient is missing.");
            else if (!agent.llmClient.HasConfiguredApiKey)
                errors.Add("Planner API key is missing from the configured environment variable.");
            if (agent.memoryClient == null) errors.Add("MemoryClient is required for the preflight reset.");
            if (agent.scheduler == null) errors.Add("RouteScheduler is missing.");
            if (agent.townMap == null) errors.Add("TownMap is missing.");
            if (agent.attackController != attackController)
                errors.Add("RobotAgent_VRAG Attack Controller is not bound to this attack controller.");
            if (agent.replayRecorder != replayRecorder)
                errors.Add("RobotAgent_VRAG Replay Recorder is not bound to this replay recorder.");
            if (float.IsNaN(agent.formalCollectionContactDistance) ||
                float.IsInfinity(agent.formalCollectionContactDistance) ||
                agent.formalCollectionContactDistance <= 0f ||
                agent.formalCollectionContactDistance > 0.5f)
                errors.Add("Formal Collection Contact Distance must be finite and in (0, 0.5] metres.");
            if (agent.formalCollectionLineOfSightMask.value != ~0)
                errors.Add("Formal Collection Line Of Sight Mask must include all physics layers.");
            if (recorder != null && recorder.seeker != agent.transform)
                errors.Add("Recorder Seeker must be the formal agent transform.");
        }

        if (trialController != null)
        {
            if (trialController.recorder != recorder || trialController.agent != agent ||
                trialController.validator != this)
                errors.Add("TrialController Recorder/Agent/Validator references are inconsistent.");
            if (trialController.attackController != attackController ||
                trialController.replayRecorder != replayRecorder)
                errors.Add("TrialController Attack/Replay references are inconsistent.");
            if (agent != null &&
                (trialController.memoryClient != agent.memoryClient ||
                 trialController.visionClient != agent.visionClient))
                errors.Add("TrialController Memory/Vision clients must match the agent clients.");
            if (string.IsNullOrWhiteSpace(trialController.taskId) ||
                string.IsNullOrWhiteSpace(trialController.taskPrompt))
                errors.Add("Frozen Task ID and Task Prompt are required.");
            if (!trialController.resetMemoryBeforeEveryTrial)
                errors.Add("Formal trials must reset memory before every run.");
            if (trialController.controlledPreludeObservations < 1)
                errors.Add("Controlled Prelude Observations must be positive.");
            if (trialController.maximumControlledObservationAttempts <
                trialController.controlledPreludeObservations)
                errors.Add("Maximum Controlled Observation Attempts must be at least the prelude count.");
        }

        if (replayRecorder != null)
        {
            if (!replayRecorder.saveRgbFrames)
                errors.Add("Formal replay requires Save RGB Frames.");
            if (!replayRecorder.saveRawModelResponses)
                errors.Add("Formal replay requires Save Raw Model Responses.");
            if (string.IsNullOrWhiteSpace(replayRecorder.codeRevision) ||
                string.Equals(replayRecorder.codeRevision.Trim(), "unversioned", StringComparison.OrdinalIgnoreCase))
                errors.Add("Replay Code Revision must be a frozen commit or experiment revision, not 'unversioned'.");
        }

        ExperimentGroundTruthSensor sensor = sensors.Length == 1 ? sensors[0] : null;
        if (sensor == null || sensor.visionCamera == null)
            errors.Add("ExperimentGroundTruthSensor with the exact screenshot camera is required.");
        if (recorder != null && recorder.groundTruthSensor != sensor)
            errors.Add("Recorder Ground Truth Sensor must be the unique active sensor.");
        if (attackController != null &&
            (attackController.recorder != recorder || attackController.sensor != sensor))
            errors.Add("Attack Controller Recorder/Sensor references are inconsistent.");
        if (recorder != null && recorder.methodCondition == EmbodiedRagMethodCondition.D_ReliabilityAware &&
            agent != null && agent.reliabilityCalibrator != calibrator)
            errors.Add("D method requires the agent to reference this ReliabilityCalibrator.");
        if (sensor != null && sensor.visionCamera != null && agent != null &&
            agent.robotEyes != null && agent.robotEyes.visionCamera != null &&
            sensor.visionCamera != agent.robotEyes.visionCamera)
            errors.Add("Ground-truth sensor must use the exact screenshot camera instance.");

        int treasureCount = CountTaggedObjects("Treasure", errors);
        int decoyCount = CountTaggedObjects("Decoy", errors);
        ValidateAllTargetIdentities(errors);
        if (agent != null && treasureCount >= 0 && agent.totalTreasures != treasureCount)
            errors.Add("Agent Total Treasures=" + agent.totalTreasures +
                " but active Treasure objects=" + treasureCount + ".");

        if (attackController != null)
        {
            string attackError;
            if (!attackController.IsConfigurationValid(out attackError)) errors.Add(attackError);
            if (recorder != null && recorder.attackLevel != attackController.CanonicalAttackName)
                errors.Add("Recorder Attack Level must equal AttackController condition: " +
                    attackController.CanonicalAttackName + ".");
            if (attackController.condition == EmbodiedRagAttackCondition.L0_Clean && decoyCount > 0)
                errors.Add("L0 clean condition contains " + decoyCount + " active Decoy object(s).");
            if (trialController != null &&
                attackController.condition == EmbodiedRagAttackCondition.L2_RepeatedExposure &&
                attackController.requiredExposureCount != trialController.controlledPreludeObservations)
                errors.Add("L2 exposure count must equal the matched controlled-prelude count.");
            if (trialController != null &&
                attackController.condition == EmbodiedRagAttackCondition.L3_StaleConflict &&
                attackController.staleExposureCount != trialController.controlledPreludeObservations)
                errors.Add("L3 stale exposure count must equal the matched controlled-prelude count.");
            if (attackController.condition == EmbodiedRagAttackCondition.L3_StaleConflict &&
                attackController.staleTreasure != null && !attackController.staleTreasure.activeInHierarchy)
                errors.Add("L3 Stale Treasure must be active during validation so Total Treasures uses the same target set as the trial.");
        }

        if (recorder != null && agent != null)
        {
            string recorderUrl = NormalizeUrl(recorder.serverUrl);
            if (agent.visionClient != null && NormalizeUrl(agent.visionClient.serverUrl) != recorderUrl)
                errors.Add("Recorder and VisionClient server URLs differ.");
            if (agent.memoryClient != null && NormalizeUrl(agent.memoryClient.serverUrl) != recorderUrl)
                errors.Add("Recorder and MemoryClient server URLs differ.");
            ValidateCalibration(errors);
        }

        foreach (MonoBehaviour behaviour in FindObjectsOfType<MonoBehaviour>())
            if (behaviour != null && behaviour.enabled && behaviour.GetType().Name == "HiderAgent")
                errors.Add("Active HiderAgent is forbidden in deterministic treasure trials.");

        if (treasureCount == 0) warnings.Add("No active Treasure objects were found.");
        lastReport = BuildReport(errors, warnings, treasureCount, decoyCount);
        LastValidationPassed = errors.Count == 0;
        if (!LastValidationPassed)
        {
            Debug.LogError("[Experiment Validator]\n" + lastReport, this);
            if (failClosed)
            {
                if (agent != null) agent.StopAutonomy("scene_validation_failed");
                if (recorder != null) recorder.InvalidateConfiguration(string.Join(" | ", errors));
            }
            return false;
        }
        Debug.Log("[Experiment Validator] PASS\n" + lastReport, this);
        return true;
    }

    private void Discover()
    {
        if (recorder == null) recorder = FindObjectOfType<EmbodiedRagExperiment>();
        if (agent == null) agent = FindObjectOfType<RobotAgent_VRAG>();
        if (calibrator == null) calibrator = FindObjectOfType<ReliabilityCalibrator>();
        if (trialController == null) trialController = FindObjectOfType<ExperimentTrialController>();
        if (attackController == null) attackController = FindObjectOfType<EmbodiedRagAttackController>();
        if (replayRecorder == null) replayRecorder = FindObjectOfType<ExperimentReplayRecorder>();
    }

    private void ValidateCalibration(List<string> errors)
    {
        if (recorder.methodCondition != EmbodiedRagMethodCondition.D_ReliabilityAware) return;
        if (calibrator == null) errors.Add("D method requires ReliabilityCalibrator.");
        else if (!calibrator.IsFitted) errors.Add("D method requires a fitted held-out calibration JSON.");
        else if (calibrator.allowIdentityFallbackForDebug) errors.Add("Formal D runs must disable identity fallback.");
        else if (string.IsNullOrWhiteSpace(calibrator.FittedSplit)) errors.Add("Calibration must name a frozen split.");
        else if (calibrator.SampleCount < 100) errors.Add("Calibration requires at least 100 observations.");
        else if (!(calibrator.WriteThreshold <= calibrator.VerifyThreshold &&
            calibrator.VerifyThreshold <= calibrator.PursueThreshold))
            errors.Add("Calibration thresholds must satisfy write <= verify <= pursue.");
    }

    private static void ValidateAllTargetIdentities(List<string> errors)
    {
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject[] values = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject value in values)
        {
            if (value == null || !value.scene.IsValid() || value.scene != activeScene) continue;
            bool isTreasure = HasTag(value, "Treasure");
            bool isDecoy = HasTag(value, "Decoy");
            if (!isTreasure && !isDecoy) continue;
            ExperimentTargetClass expected = isTreasure
                ? ExperimentTargetClass.Treasure : ExperimentTargetClass.Decoy;
            ExperimentTargetIdentity identity = value.GetComponent<ExperimentTargetIdentity>();
            if (identity == null || string.IsNullOrWhiteSpace(identity.ObjectId))
            { errors.Add(value.name + " is missing a stable ExperimentTargetIdentity."); continue; }
            if (identity.targetClass != expected)
                errors.Add(identity.ObjectId + " target class disagrees with Unity tag " +
                    (isTreasure ? "Treasure" : "Decoy") + ".");
            if (!ids.Add(identity.ObjectId)) errors.Add("Duplicate stable object ID: " + identity.ObjectId + ".");
        }
    }

    private static bool HasTag(GameObject value, string tagName)
    {
        try { return value.CompareTag(tagName); }
        catch (UnityException) { return false; }
    }

    private static int CountTaggedObjects(string tagName, List<string> errors)
    {
        try { return GameObject.FindGameObjectsWithTag(tagName).Length; }
        catch (UnityException) { errors.Add("Unity tag is not defined: " + tagName + "."); return -1; }
    }

    private static string NormalizeUrl(string value)
    { return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().TrimEnd('/').ToLowerInvariant(); }

    private string BuildReport(List<string> errors, List<string> warnings, int treasures, int decoys)
    {
        List<string> lines = new List<string>
        {
            "method=" + (recorder != null ? recorder.CanonicalMethodName : "missing"),
            "scene=" + (recorder != null ? recorder.sceneName : "missing"),
            "attack=" + (recorder != null ? recorder.attackLevel : "missing"),
            "active_treasures=" + treasures + ", active_decoys=" + decoys
        };
        foreach (string warning in warnings) lines.Add("WARNING: " + warning);
        foreach (string error in errors) lines.Add("ERROR: " + error);
        lines.Add(errors.Count == 0 ? "RESULT: PASS" : "RESULT: FAIL_CLOSED");
        return string.Join("\n", lines);
    }
}
