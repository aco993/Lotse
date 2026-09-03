using System.Text.Json;
using System.Text.RegularExpressions;
using Lotse.Core.Model;

namespace Lotse.Core.Tests;

/// <summary>
/// Schützt die gemessene Grammatik-Abdeckung: keine <c>Muss</c>-Stelle des B2-Referenzinventars
/// (<c>tools/b2-grammar-reference.json</c>) darf unter Note 2 fallen, also unter vier Übungen.
/// Der Bericht selbst entsteht mit <c>tools/grammar-coverage.py</c>; dieser Test hält nur die
/// Untergrenze fest, damit ein Umbau der Inhalte die Pflichtthemen nicht unbemerkt ausdünnt.
/// </summary>
public class GrammarCoverageTests(CatalogFixture fx) : IClassFixture<CatalogFixture>
{
    /// <summary>Note 2 verlangt mindestens vier Übungen an der Stelle.</summary>
    private const int MinimumExercisesPerMustItem = 4;

    private sealed record ReferenceItem(string Id, string Title, string Priority, string[] Nodes, string? Pattern);
    /// <summary>Eine Struktur, die die Rubriken der Produktionsaufgaben ausdrücklich verlangen sollen.</summary>
    private sealed record ProductionDemand(string Name, string Pattern);

    private static readonly Lazy<IReadOnlyList<ReferenceItem>> Reference = new(Load);
    private static readonly Lazy<(int Minimum, IReadOnlyList<ProductionDemand> Structures)> Demands = new(LoadDemands);

    [Fact]
    public void Reference_inventory_is_readable_and_points_at_known_nodes()
    {
        var items = Reference.Value;
        Assert.InRange(items.Count, 35, 55);
        Assert.Contains(items, i => i.Priority == "Muss");
        var nodeIds = new HashSet<string>(fx.Catalog.Nodes.Select(n => n.Id), StringComparer.Ordinal);
        foreach (var item in items)
            foreach (var node in item.Nodes)
                Assert.True(nodeIds.Contains(node), $"{item.Id}: unbekannter Knoten '{node}'");
    }

    [Fact]
    public void Every_must_item_of_the_b2_inventory_has_at_least_four_exercises()
    {
        var thin = new List<string>();
        foreach (var item in Reference.Value.Where(i => i.Priority == "Muss"))
        {
            var count = fx.Catalog.Exercises.Count(e => Matches(item, e));
            if (count < MinimumExercisesPerMustItem) thin.Add($"{item.Id} ({item.Title}): nur {count} Übungen");
        }
        Assert.True(thin.Count == 0, "Muss-Stellen unter Note 2:\n" + string.Join("\n", thin));
    }

    [Fact]
    public void Every_target_structure_is_demanded_by_several_production_tasks()
    {
        var (minimum, structures) = Demands.Value;
        Assert.NotEmpty(structures);
        var tasks = fx.Catalog.Exercises.Where(e => e.IsProduction).ToList();
        Assert.True(tasks.Count >= 50, $"nur {tasks.Count} Produktionsaufgaben");

        var thin = new List<string>();
        foreach (var s in structures)
        {
            var count = tasks.Count(t => t.Rubric.Any(r => Regex.IsMatch(r, s.Pattern)));
            if (count < minimum) thin.Add($"„{s.Name}“ nur in {count} Rubriken (mindestens {minimum})");
        }
        Assert.True(thin.Count == 0, "Zu wenig Produktionsdruck:\n" + string.Join("\n", thin));
    }

    [Fact]
    public void Every_production_task_names_at_least_one_structure()
    {
        var (_, structures) = Demands.Value;
        var patterns = structures.Select(s => s.Pattern).ToList();
        var vague = fx.Catalog.Exercises
            .Where(e => e.IsProduction && !e.Rubric.Any(r => patterns.Any(p => Regex.IsMatch(r, p))))
            .Select(e => e.Id)
            .ToList();
        Assert.True(vague.Count == 0, "Produktionsaufgaben ohne benannte Struktur: " + string.Join(", ", vague));
    }

    private static bool Matches(ReferenceItem item, Exercise e)
    {
        if (item.Nodes.Length > 0 && !item.Nodes.Contains(e.NodeId, StringComparer.Ordinal)) return false;
        return item.Pattern is null || Regex.IsMatch(SearchableText(e), item.Pattern);
    }

    /// <summary>
    /// Muss dieselbe Auswahl treffen wie <c>exercise_blob</c> in <c>tools/grammar-coverage.py</c>:
    /// ohne Rubrik und Musterlösung, und bei Lesetexten ohne Textkörper und Fragen.
    /// </summary>
    private static string SearchableText(Exercise e)
    {
        var parts = new List<string?> { e.Prompt, e.Instruction, e.Explanation, e.Hint, e.SerbianNote, e.ExampleDe };
        if (e.Type != ExerciseType.Reading)
        {
            parts.Add(e.Text);
            foreach (var q in e.Questions)
            {
                parts.Add(q.Question);
                parts.AddRange(q.Options);
            }
        }
        parts.AddRange(e.Answers);
        parts.AddRange(e.Options);
        foreach (var line in e.Lines)
        {
            parts.Add(line.Text);
            if (line.Options is not null) parts.AddRange(line.Options);
            if (line.Feedback is not null) parts.AddRange(line.Feedback);
        }
        foreach (var pair in e.Pairs)
        {
            parts.Add(pair.Left);
            parts.Add(pair.Right);
        }
        return string.Join("\n", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    private static IReadOnlyList<ReferenceItem> Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ResolvePath()));
        return doc.RootElement.GetProperty("items").EnumerateArray().Select(i => new ReferenceItem(
            i.GetProperty("id").GetString()!,
            i.GetProperty("title").GetString()!,
            i.GetProperty("priority").GetString()!,
            i.TryGetProperty("nodes", out var n) ? n.EnumerateArray().Select(x => x.GetString()!).ToArray() : [],
            i.TryGetProperty("pattern", out var p) ? p.GetString() : null)).ToList();
    }

    private static (int, IReadOnlyList<ProductionDemand>) LoadDemands()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ResolvePath()));
        var node = doc.RootElement.GetProperty("productionDemands");
        var structures = node.GetProperty("structures").EnumerateArray()
            .Select(s => new ProductionDemand(s.GetProperty("name").GetString()!, s.GetProperty("pattern").GetString()!))
            .ToList();
        return (node.GetProperty("minTasksPerStructure").GetInt32(), structures);
    }

    private static string ResolvePath()
    {
        const string relative = "tools/b2-grammar-reference.json";
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, relative) };
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, relative));
        return candidates.FirstOrDefault(File.Exists)
               ?? throw new FileNotFoundException($"Referenzinventar nicht gefunden: {relative}");
    }
}
