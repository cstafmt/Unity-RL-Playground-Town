using UnityEngine;

/// <summary>
/// Stable, evaluation-only identity for a physical treasure or decoy.
/// Never expose ObjectId or TargetClass to the policy, prompts, or RAG store.
/// </summary>
public enum ExperimentTargetClass
{
    Treasure,
    Decoy
}

[DisallowMultipleComponent]
public sealed class ExperimentTargetIdentity : MonoBehaviour
{
    [SerializeField] private string objectId = "";
    public ExperimentTargetClass targetClass = ExperimentTargetClass.Treasure;
    [Tooltip("Human-auditable appearance label; evaluation only.")]
    public string semanticColor = "unknown";
    [Tooltip("Human-auditable appearance label; evaluation only.")]
    public string semanticShape = "unknown";

    public string ObjectId { get { return objectId; } }

#if UNITY_EDITOR
    public void EditorAssignIdentity(string stableId, ExperimentTargetClass kind)
    {
        objectId = stableId == null ? "" : stableId.Trim();
        targetClass = kind;
    }
#endif
}
