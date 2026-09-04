namespace Lotse.Core.Model;

/// <summary>
/// What the learner is working towards. It changes how far above B2 the planner may reach, nothing else:
/// the placement still calibrates the B1/B2 boundary and the exam pages stay Goethe-B2, because a C1 blueprint
/// this app does not have would be a promise it cannot keep.
/// </summary>
public enum TargetLevel
{
    B2,
    C1,
}

public static class TargetLevelExtensions
{
    /// <summary>Highest band the focus ranking may put in front of the learner.</summary>
    public static CefrBand FocusCap(this TargetLevel level) => level == TargetLevel.C1 ? CefrBand.C1 : CefrBand.B2_2;

    public static string Label(this TargetLevel level) => level == TargetLevel.C1 ? "C1" : "B2";
}
