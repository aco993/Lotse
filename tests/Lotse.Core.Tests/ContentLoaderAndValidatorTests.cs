using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Infrastructure.Content;

namespace Lotse.Core.Tests;

public class ExerciseValidatorTests
{
    private static Exercise Base(ExerciseType type) => new() { Id = "x", Type = type, NodeId = "GR.PASSIV", Band = CefrBand.B2_1, Prompt = "p" };

    [Fact]
    public void Cloze_without_answers_is_invalid()
        => Assert.NotEmpty(ExerciseValidator.Validate(Base(ExerciseType.Cloze)));

    [Fact]
    public void Multiple_choice_needs_a_valid_correct_index()
    {
        Assert.NotEmpty(ExerciseValidator.Validate(Base(ExerciseType.MultipleChoice) with { Options = ["a", "b"], CorrectIndex = 2 }));
        Assert.Empty(ExerciseValidator.Validate(Base(ExerciseType.MultipleChoice) with { Options = ["a", "b"], CorrectIndex = 1 }));
    }

    [Fact]
    public void Unknown_node_is_reported_when_node_set_is_given()
    {
        var problems = ExerciseValidator.Validate(Base(ExerciseType.Cloze) with { Answers = ["a"] }, new HashSet<string> { "OTHER" });
        Assert.Contains(problems, p => p.Contains("unbekannter Knoten"));
    }

    [Fact]
    public void Reading_needs_text_and_consistent_questions()
    {
        var bad = Base(ExerciseType.Reading) with { Text = "t", Questions = [new ReadingQuestion("q", ["a"], 0)] };
        Assert.NotEmpty(ExerciseValidator.Validate(bad));
        var good = Base(ExerciseType.Reading) with { Text = "t", Questions = [new ReadingQuestion("q", ["a", "b"], 1)] };
        Assert.Empty(ExerciseValidator.Validate(good));
    }

    [Fact]
    public void Production_tasks_need_rubric_and_free_write_needs_min_words()
    {
        Assert.NotEmpty(ExerciseValidator.Validate(Base(ExerciseType.FreeWrite) with { Rubric = ["r"] }));
        Assert.Empty(ExerciseValidator.Validate(Base(ExerciseType.FreeWrite) with { Rubric = ["r"], MinWords = 40 }));
        Assert.NotEmpty(ExerciseValidator.Validate(Base(ExerciseType.Speak)));
    }

    [Fact]
    public void Estimated_seconds_scale_with_task_size()
    {
        Assert.True(Base(ExerciseType.Vocab).EstimatedSeconds < Base(ExerciseType.Translate).EstimatedSeconds);
        var shortWrite = Base(ExerciseType.FreeWrite) with { MinWords = 40 };
        var longWrite = Base(ExerciseType.FreeWrite) with { MinWords = 150 };
        Assert.True(shortWrite.EstimatedSeconds < longWrite.EstimatedSeconds);
    }
}

public class ContentLoaderTests
{
    private static string TempContent(string taxonomy, string exercises)
    {
        var dir = Path.Combine(Path.GetTempPath(), "lotse-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "exercises"));
        File.WriteAllText(Path.Combine(dir, "taxonomy.json"), taxonomy);
        File.WriteAllText(Path.Combine(dir, "exercises", "a.json"), exercises);
        return dir;
    }

    private const string Taxonomy = """
        { "nodes": [ { "id": "N1", "area": "Grammatik", "title": "T", "description": "d", "band": "B1_1", "serbianInterference": false, "interferenceNote": null, "prerequisites": [] } ],
          "errorTypes": [ { "code": "E1", "nodeId": "N1", "title": "t", "description": "d", "severity": 1 } ] }
        """;

    [Fact]
    public void Loads_a_minimal_valid_catalog()
    {
        var dir = TempContent(Taxonomy, """{ "exercises": [ { "id": "a", "type": "cloze", "nodeId": "N1", "band": "B1_1", "prompt": "___", "answers": ["x"] } ] }""");
        var catalog = ContentLoader.Load(dir);
        Assert.Single(catalog.Exercises);
        Assert.Equal(ExerciseType.Cloze, catalog.Exercise("a")!.Type);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Rejects_duplicate_ids()
    {
        var dir = TempContent(Taxonomy, """{ "exercises": [ { "id": "a", "type": "cloze", "nodeId": "N1", "band": "B1_1", "prompt": "___", "answers": ["x"] }, { "id": "a", "type": "cloze", "nodeId": "N1", "band": "B1_1", "prompt": "___", "answers": ["y"] } ] }""");
        var ex = Assert.Throws<InvalidDataException>(() => ContentLoader.Load(dir));
        Assert.Contains("Doppelte", ex.Message);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Rejects_exercise_on_unknown_node()
    {
        var dir = TempContent(Taxonomy, """{ "exercises": [ { "id": "a", "type": "cloze", "nodeId": "NOPE", "band": "B1_1", "prompt": "___", "answers": ["x"] } ] }""");
        Assert.Throws<InvalidDataException>(() => ContentLoader.Load(dir));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Exercise_round_trips_through_json()
    {
        var ex = new Exercise { Id = "r", Type = ExerciseType.Reading, NodeId = "N1", Band = CefrBand.B2_1, Prompt = "p", Text = "t", AudioOnly = true, Questions = [new ReadingQuestion("q", ["a", "b"], 1)], Tags = ["x"] };
        var back = ContentLoader.DeserializeExercise(ContentLoader.Serialize(ex));
        Assert.NotNull(back);
        Assert.True(back!.AudioOnly);
        Assert.Equal(1, back.Questions[0].CorrectIndex);
        Assert.Equal(new[] { "x" }, back.Tags);
    }
}

public class PlacementOrderTests(CatalogFixture fx) : IClassFixture<CatalogFixture>
{
    [Fact]
    public void Placement_interleaves_nodes_easy_pass_first()
    {
        var items = PlacementTest.Build(fx.Catalog);
        var half = items.Count / 2;
        // first half: one item per node, no node twice; second half likewise
        Assert.Equal(half, items.Take(half).Select(i => i.NodeId).Distinct().Count());
        Assert.Equal(items.Count - half, items.Skip(half).Select(i => i.NodeId).Distinct().Count());
        Assert.True(items.Take(half).All(i => i.Band <= CefrBand.B1_2));
    }
}

public class WeakAreaTests(CatalogFixture fx) : IClassFixture<CatalogFixture>
{
    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Fresh_errors_push_a_node_to_the_top_and_trend_is_computed()
    {
        var states = new Dictionary<string, SkillState>
        {
            ["GR.PASSIV"] = new() { NodeId = "GR.PASSIV", Theta = 0.2, Attempts = 8, Correct = 5 },
            ["GR.GENITIV"] = new() { NodeId = "GR.GENITIV", Theta = 0.2, Attempts = 8, Correct = 5 },
        };
        var errors = new List<ErrorEvent>
        {
            new("PASSIV_FORM", "GR.PASSIV", Now.AddDays(-1)),
            new("PASSIV_FORM", "GR.PASSIV", Now.AddDays(-2)),
            new("GENITIV", "GR.GENITIV", Now.AddDays(-10)),
            new("GENITIV", "GR.GENITIV", Now.AddDays(-11)),
            new("GENITIV", "GR.GENITIV", Now.AddDays(-12)),
        };
        var weak = LearnerAnalysis.WeakAreas(fx.Catalog, states, errors, Now);
        Assert.Equal("GR.PASSIV", weak[0].NodeId);
        Assert.Equal(Trend.Worsening, weak[0].Trend);
        Assert.Equal(Trend.Improving, weak.First(w => w.NodeId == "GR.GENITIV").Trend);
        Assert.Equal(2, weak[0].Errors7d);
    }

    [Fact]
    public void Top_error_codes_are_counted_within_the_window()
    {
        var errors = new List<ErrorEvent>
        {
            new("ART_FEHLT", "GR.ARTIKEL_GENUS", Now.AddDays(-1)),
            new("ART_FEHLT", "GR.ARTIKEL_GENUS", Now.AddDays(-3)),
            new("ART_FEHLT", "GR.ARTIKEL_GENUS", Now.AddDays(-40)),
            new("KOMMA", "GR.ORTHOGRAFIE", Now.AddDays(-2)),
        };
        var top = LearnerAnalysis.TopErrorCodes(fx.Catalog, errors, Now);
        Assert.Equal("ART_FEHLT", top[0].Code);
        Assert.Equal(2, top[0].Count);
        Assert.Equal("Artikel fehlt", top[0].Title);
    }
}
