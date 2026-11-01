using System.Text.Json;
using Lotse.Core.Model;
using Lotse.Core.Telc;

namespace Lotse.Infrastructure.Content;

/// <summary>Reads <c>content/telc/*.json</c> into one validated <see cref="TelcContent"/>.</summary>
public static class TelcContentLoader
{
    private sealed record TelcFile(
        List<TelcLesen1>? Lesen1,
        List<TelcLesen2>? Lesen2,
        List<TelcLesen3>? Lesen3,
        List<TelcSprachbausteine>? Sprachbausteine,
        List<TelcHoeren>? Hoeren,
        List<TelcWritingPair>? Schreiben,
        List<TelcErfahrung>? Erfahrungen,
        List<TelcDiskussion>? Diskussionen,
        List<TelcPlanung>? Planungen);

    public static TelcContent Load(string contentDirectory, ContentCatalog catalog)
    {
        var dir = Path.Combine(contentDirectory, "telc");
        if (!Directory.Exists(dir)) return TelcContent.Empty;

        var files = Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => JsonSerializer.Deserialize<TelcFile>(File.ReadAllText(f), ContentLoader.Options) ?? throw new InvalidDataException($"{f} ist leer."))
            .ToList();

        var content = new TelcContent(
            files.SelectMany(f => f.Lesen1 ?? []).ToList(),
            files.SelectMany(f => f.Lesen2 ?? []).ToList(),
            files.SelectMany(f => f.Lesen3 ?? []).ToList(),
            files.SelectMany(f => f.Sprachbausteine ?? []).ToList(),
            files.SelectMany(f => f.Hoeren ?? []).ToList(),
            files.SelectMany(f => f.Schreiben ?? []).ToList(),
            files.SelectMany(f => f.Erfahrungen ?? []).ToList(),
            files.SelectMany(f => f.Diskussionen ?? []).ToList(),
            files.SelectMany(f => f.Planungen ?? []).ToList());

        var problems = content.Validate(
            catalog.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal),
            catalog.ErrorTypes.Select(e => e.Code).ToHashSet(StringComparer.Ordinal));
        if (problems.Count > 0)
            throw new InvalidDataException("Ungültiger telc-Content:\n" + string.Join("\n", problems));
        return content;
    }
}
