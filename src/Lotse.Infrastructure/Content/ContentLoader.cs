using System.Text.Json;
using System.Text.Json.Serialization;
using Lotse.Core.Model;

namespace Lotse.Infrastructure.Content;

/// <summary>Reads the JSON content files (taxonomy + exercise banks) into a validated <see cref="ContentCatalog"/>.</summary>
public static class ContentLoader
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        WriteIndented = true,
    };

    private sealed record TaxonomyFile(List<SkillNode> Nodes, List<ErrorType> ErrorTypes);
    private sealed record ExerciseFile(List<Exercise> Exercises);

    public static ContentCatalog Load(string contentDirectory)
    {
        if (!Directory.Exists(contentDirectory))
            throw new DirectoryNotFoundException($"Content-Verzeichnis nicht gefunden: {contentDirectory}");

        var taxonomyPath = Path.Combine(contentDirectory, "taxonomy.json");
        var taxonomy = JsonSerializer.Deserialize<TaxonomyFile>(File.ReadAllText(taxonomyPath), Options)
                       ?? throw new InvalidDataException("taxonomy.json ist leer.");

        var exercises = new List<Exercise>();
        var exerciseDir = Path.Combine(contentDirectory, "exercises");
        if (Directory.Exists(exerciseDir))
        {
            foreach (var file in Directory.EnumerateFiles(exerciseDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var parsed = JsonSerializer.Deserialize<ExerciseFile>(File.ReadAllText(file), Options)
                             ?? throw new InvalidDataException($"{file} ist leer.");
                exercises.AddRange(parsed.Exercises);
            }
        }

        var duplicates = exercises.GroupBy(e => e.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new InvalidDataException($"Doppelte Übungs-IDs: {string.Join(", ", duplicates)}");

        var catalog = new ContentCatalog(taxonomy.Nodes, taxonomy.ErrorTypes, exercises);
        var problems = catalog.Validate();
        if (problems.Count > 0)
            throw new InvalidDataException("Ungültiger Content:\n" + string.Join("\n", problems));
        return catalog;
    }

    /// <summary>Finds the content folder next to the running app, or walking up from the working directory (dev + tests).</summary>
    public static string ResolveContentDirectory(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return Path.GetFullPath(configured);

        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "content"),
            Path.Combine(Directory.GetCurrentDirectory(), "content"),
        };
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "content"));

        return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, "taxonomy.json")))
               ?? throw new DirectoryNotFoundException("Kein content/-Ordner mit taxonomy.json gefunden.");
    }

    public static string Serialize(Exercise exercise) => JsonSerializer.Serialize(exercise, Options);
    public static Exercise? DeserializeExercise(string json) => JsonSerializer.Deserialize<Exercise>(json, Options);
}
