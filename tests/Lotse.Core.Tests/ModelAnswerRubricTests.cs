using System.Text.RegularExpressions;
using Lotse.Core.Model;

namespace Lotse.Core.Tests;

/// <summary>
/// A rubric that demands a structure and a model answer that does not show it teaches the learner the wrong lesson:
/// they compare their text against the model, see the structure missing there, and conclude it was optional.
///
/// Only rubric lines that name a structure detectably are checked - most lines ("Alle vier Leitpunkte", "Sachlich,
/// ohne Vorwurf") are about content and rightly beyond a keyword test. Where a rubric offers alternatives ("Passiv
/// oder Perfekt"), any of them counts.
/// </summary>
public class ModelAnswerRubricTests(CatalogFixture fx) : IClassFixture<CatalogFixture>
{
    private sealed record Rule(string Name, Regex Rubric, string[] Markers, Regex? Unless = null);

    private static readonly string[] Konjunktiv = ["könnt", "hätt", "wäre", "würd", "müsst", "sollt", "dürft", "ließe"];
    private static readonly string[] Passiv = ["wird ", "werden", "wurde", "worden", "lässt sich", "ließe sich", "ist zu ", "sind zu ", "machbar", "lösbar", "vermeidbar"];
    private static readonly string[] Perfekt = ["habe ", "hat ", "haben ", "ist ", "sind ", "bin "];
    private static readonly string[] Relativ = [", der ", ", die ", ", das ", ", den ", ", dem ", ", denen ", ", welche", ", für die", ", von denen", ", mit dem", ", an dem", ", bei dem", ", auf die"];
    private static readonly string[] Temporal = ["nachdem", "seitdem", "bevor", "als ", "sobald", "während"];
    private static readonly string[] Plusquamperfekt = ["hatte", "war ", "waren", "worden war"];
    private static readonly string[] Vergleich = ["während", "im gegensatz", "je ", "desto", "verglichen", "anders als"];

    private static readonly Rule[] Rules =
    [
        new("Konjunktiv II", new Regex(@"konjunktiv\s*ii", RegexOptions.IgnoreCase), Konjunktiv),
        new("Passiv oder Perfekt", new Regex(@"passiv.*perfekt|perfekt.*passiv", RegexOptions.IgnoreCase), [.. Passiv, .. Perfekt]),
        new("Passiv", new Regex("passiv", RegexOptions.IgnoreCase), Passiv, new Regex("perfekt", RegexOptions.IgnoreCase)),
        new("Relativsatz", new Regex("relativsatz", RegexOptions.IgnoreCase), Relativ),
        new("Temporaler Nebensatz", new Regex(@"nachdem|seitdem|temporaler nebensatz", RegexOptions.IgnoreCase), Temporal),
        new("Plusquamperfekt", new Regex(@"plusquamperfekt|vorzeitigkeit", RegexOptions.IgnoreCase), Plusquamperfekt),
        new("Vergleichsstrukturen", new Regex("vergleichsstruktur", RegexOptions.IgnoreCase), Vergleich),
    ];

    [Fact]
    public void Every_model_answer_shows_the_structures_its_rubric_names()
    {
        var failures = new List<string>();
        var checks = 0;

        foreach (var e in fx.Catalog.Exercises.Where(x => x.Type is ExerciseType.FreeWrite or ExerciseType.Speak))
        {
            var model = (e.ModelAnswer ?? "").ToLowerInvariant();
            foreach (var line in e.Rubric)
            {
                foreach (var rule in Rules)
                {
                    if (!rule.Rubric.IsMatch(line)) continue;
                    if (rule.Unless is not null && rule.Unless.IsMatch(line)) continue;
                    checks++;
                    if (!rule.Markers.Any(model.Contains)) failures.Add($"{e.Id}: Rubrik verlangt {rule.Name} („{line}“), Musterlösung zeigt es nicht");
                }
            }
        }

        Assert.True(checks >= 30, $"Nur {checks} Rubrik-Zeilen geprüft – die Regeln greifen nicht mehr.");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
