using System;

/// <summary>
/// Canonical experimental conditions. The condition controls behavior; it is not a log-only label.
/// </summary>
public enum EmbodiedRagMethodCondition
{
    A_NoMemory = 0,
    B_VanillaRag = 1,
    C_HeuristicTrust = 2,
    D_ReliabilityAware = 3
}

public static class EmbodiedRagMethodProfile
{
    public static string CanonicalName(EmbodiedRagMethodCondition condition)
    {
        switch (condition)
        {
            case EmbodiedRagMethodCondition.A_NoMemory: return "A_no_memory";
            case EmbodiedRagMethodCondition.B_VanillaRag: return "B_vanilla_rag";
            case EmbodiedRagMethodCondition.C_HeuristicTrust: return "C_heuristic_trust";
            case EmbodiedRagMethodCondition.D_ReliabilityAware: return "D_reliability_aware";
            default: return "unset";
        }
    }

    public static bool TryParse(string value, out EmbodiedRagMethodCondition condition)
    {
        string normalized = (value ?? "").Trim().ToLowerInvariant().Replace('-', '_');
        switch (normalized)
        {
            case "a":
            case "a_no_memory":
            case "no_memory":
                condition = EmbodiedRagMethodCondition.A_NoMemory;
                return true;
            case "b":
            case "b_vanilla":
            case "b_vanilla_rag":
            case "vanilla_rag":
                condition = EmbodiedRagMethodCondition.B_VanillaRag;
                return true;
            case "c":
            case "b_heuristic":
            case "c_heuristic":
            case "c_heuristic_trust":
            case "heuristic_trust":
                condition = EmbodiedRagMethodCondition.C_HeuristicTrust;
                return true;
            case "d":
            case "d_proposed":
            case "d_reliability_aware":
            case "proposed":
                condition = EmbodiedRagMethodCondition.D_ReliabilityAware;
                return true;
            default:
                condition = EmbodiedRagMethodCondition.C_HeuristicTrust;
                return false;
        }
    }

    public static bool UsesLongTermMemory(this EmbodiedRagMethodCondition condition)
    {
        return condition != EmbodiedRagMethodCondition.A_NoMemory;
    }

    public static bool UsesTrustScore(this EmbodiedRagMethodCondition condition)
    {
        return condition == EmbodiedRagMethodCondition.C_HeuristicTrust ||
               condition == EmbodiedRagMethodCondition.D_ReliabilityAware;
    }

    public static bool UsesCalibratedReliability(this EmbodiedRagMethodCondition condition)
    {
        return condition == EmbodiedRagMethodCondition.D_ReliabilityAware;
    }
}
