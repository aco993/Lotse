namespace Lotse.Core.Model;

/// <summary>
/// Which B2 exam the learner is preparing for. It decides which blueprint the Prüfung page, the readiness forecast
/// and the plan count in: Goethe passes module by module, telc adds up the written half and passes the oral half on
/// its own. The training itself is the same either way - the language is.
/// </summary>
public enum ExamProvider
{
    Goethe,
    Telc,
}

public static class ExamProviderExtensions
{
    public static string Label(this ExamProvider exam) => exam == ExamProvider.Telc ? "telc Deutsch B2" : "Goethe-Zertifikat B2";
}
