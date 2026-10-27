using System.Text.Json;
using Lotse.Core.Model;
using Lotse.Core.Training;

namespace Lotse.Infrastructure.Content;

/// <summary>Reads <c>content/training/*.json</c> into one validated <see cref="TrainingContent"/>.</summary>
public static class TrainingContentLoader
{
    private sealed record TrainingFile(List<TrainingTopic>? Topics, List<TrainingPoint>? Points, List<TrainingTask>? Tasks, TrainingDiagnostic? Diagnostic);

    public static TrainingContent Load(string contentDirectory, ContentCatalog catalog)
    {
        var topics = new List<TrainingTopic>();
        var points = new List<TrainingPoint>();
        var tasks = new List<TrainingTask>();
        TrainingDiagnostic? diagnostic = null;

        var dir = Path.Combine(contentDirectory, "training");
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var parsed = JsonSerializer.Deserialize<TrainingFile>(File.ReadAllText(file), ContentLoader.Options)
                             ?? throw new InvalidDataException($"{file} ist leer.");
                topics.AddRange(parsed.Topics ?? []);
                points.AddRange(parsed.Points ?? []);
                tasks.AddRange(parsed.Tasks ?? []);
                diagnostic ??= parsed.Diagnostic;
            }
        }

        if (topics.Count == 0 || diagnostic is null)
            throw new InvalidDataException($"Kein B2-Training-Inhalt (Themen und Diagnose) in {dir}.");

        var content = new TrainingContent(topics, points, tasks, diagnostic);
        var problems = content.Validate(catalog);
        if (problems.Count > 0)
            throw new InvalidDataException("Ungültiger Training-Content:\n" + string.Join("\n", problems));
        return content;
    }
}
