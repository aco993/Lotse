using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Core.Telc;
using Lotse.Infrastructure.Content;

namespace Lotse.Core.Tests;

/// <summary>
/// The rule-based analyzer against every piece of correct German in the content bank: model answers, reading and
/// listening texts, the vocabulary cards' example sentences, the accepted answers of the sentence drills and the
/// telc texts. A finding there is a false alarm by definition - the bank is the reference the learner copies from.
/// The planted mistakes of the golden set measure recall; this measures precision, on far more text than any rule
/// was written against.
/// </summary>
public sealed class AnalyzerPrecisionTests
{
    private static readonly ContentCatalog Catalog = ContentLoader.Load(ContentLoader.ResolveContentDirectory());
    private static readonly TelcContent Telc = TelcContentLoader.Load(ContentLoader.ResolveContentDirectory(), Catalog);
    private static readonly Lexicon Lex = Lexicon.FromCatalog(Catalog);

    /// <summary>Every correct text in the bank, with where it came from and whether it is a formal letter.</summary>
    internal static IEnumerable<(string Source, string Text, bool Formal)> Corpus()
    {
        foreach (var e in Catalog.Exercises.Where(e => e.Type != ExerciseType.SpotError))
        {
            if (e.ModelAnswer is { Length: > 0 } m) yield return ($"{e.Id}.model", m, false);
            if (e.Text is { Length: > 0 } t) yield return ($"{e.Id}.text", t, false);
            if (e.ExampleDe is { Length: > 0 } x) yield return ($"{e.Id}.example", x, false);
            if (e.Type is ExerciseType.Transform or ExerciseType.Translate or ExerciseType.WordOrder && e.Answers.Count > 0)
                // The fullest accepted answer: some accept a variant without the comma the rules ask for.
                yield return ($"{e.Id}.answer", e.Answers.MaxBy(a => a.Count(c => c == ','))!, false);
        }
        foreach (var l in Telc.Lesen1) for (var i = 0; i < l.Texts.Count; i++) yield return ($"{l.Id}.{i}", l.Texts[i], false);
        foreach (var l in Telc.Lesen2) yield return (l.Id, l.Text, false);
        foreach (var l in Telc.Lesen3) for (var i = 0; i < l.Ads.Count; i++) yield return ($"{l.Id}.ad{i}", l.Ads[i].Text, false);
        foreach (var d in Telc.Diskussionen) yield return (d.Id, d.Text, false);
        foreach (var e in Telc.Erfahrungen.Where(e => e.ModelAnswer is not null)) yield return ($"{e.Id}.model", e.ModelAnswer!, false);
        foreach (var w in Telc.Schreiben.SelectMany(p => new[] { p.A, p.B }).Where(w => w.ModelAnswer is not null))
            yield return ($"{w.Id}.model", w.ModelAnswer!, w.Formal);
    }

    private static readonly Exercise Task = new() { Id = "precision", Type = ExerciseType.FreeWrite, NodeId = "SC.FORMELL", Band = CefrBand.B2_1, Prompt = "precision" };

    // Codes the rules emit about the text as a whole or as advice, not about one quoted mistake.
    private static readonly HashSet<string> NotAboutAQuote = ["TXT_LAENGE", "TXT_KOHAERENZ"];

    [Fact]
    public void The_corpus_is_large_enough_to_mean_something()
    {
        var corpus = Corpus().ToList();
        var words = corpus.Sum(c => AnswerChecker.CountWords(c.Text));
        Assert.True(corpus.Count > 1300 && words > 20_000, $"{corpus.Count} Texte, {words} Wörter");
    }

    [Fact]
    public void Correct_german_from_the_content_bank_raises_no_finding()
    {
        var alarms = Corpus()
            .SelectMany(c => FreeTextAnalyzer.Analyze(c.Text, Task, Lex, formal: c.Formal).Findings
                .Where(f => !NotAboutAQuote.Contains(f.Code))
                .Select(f => $"{c.Source}: {f.Code} „{f.Snippet}“ → „{f.Correction}“"))
            .ToList();
        Assert.True(alarms.Count == 0, $"{alarms.Count} Fehlalarme:\n" + string.Join("\n", alarms.Take(80)));
    }

    [Fact]
    public void No_text_in_the_content_bank_reads_as_an_instruction_to_the_rater()
    {
        // The guard locks every A, so a false alarm would cost a good letter its grade.
        var alarms = Corpus().Where(c => TelcWritingCheck.TalksToRater(c.Text)).Select(c => c.Source).ToList();
        Assert.True(alarms.Count == 0, "Fehlalarme: " + string.Join(", ", alarms));
    }
}
