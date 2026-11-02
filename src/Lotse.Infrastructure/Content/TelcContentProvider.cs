using System.Text.Json;
using Lotse.Core.Telc;
using Lotse.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lotse.Infrastructure.Content;

/// <summary>
/// The live telc content: the tasks shipped in <c>content/telc/</c> plus every approved draft from the database.
/// Singleton, like <see cref="ContentCatalogProvider"/>; swapping the reference is atomic, so a page never sees a
/// half-merged bank. The seed is validated at startup and must be valid; a draft that no longer validates (say, a
/// node was renamed since its approval) is skipped with a warning instead of taking the app down.
/// </summary>
public sealed class TelcContentProvider
{
    private readonly TelcContent _seed;
    private readonly ContentCatalogProvider _catalog;
    private readonly IDbContextFactory<LotseDbContext> _dbFactory;
    private readonly ILogger<TelcContentProvider> _logger;
    private volatile TelcContent _current;

    public TelcContentProvider(TelcContent seed, ContentCatalogProvider catalog, IDbContextFactory<LotseDbContext> dbFactory, ILogger<TelcContentProvider> logger)
    {
        _seed = _current = seed;
        _catalog = catalog;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public TelcContent Content => _current;

    public TelcContent Seed => _seed;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var approved = await db.TelcDrafts.AsNoTracking().Where(d => d.Status == TelcDraftStatus.Approved).OrderBy(d => d.ReviewedUtc).ToListAsync(ct);
        var merged = _seed;
        foreach (var draft in approved)
        {
            var single = TelcDraftCodec.TryRead(draft.Part, draft.Json, draft.Id);
            if (single is null || Validate(merged.With(single)).Count > 0)
            {
                _logger.LogWarning("Freigegebene telc-Aufgabe {Id} ist nicht mehr gültig, übersprungen.", draft.Id);
                continue;
            }
            merged = merged.With(single);
        }
        _current = merged;
    }

    /// <summary>The format rules against the current node and error catalogue.</summary>
    public IReadOnlyList<string> Validate(TelcContent content) => content.Validate(
        _catalog.Catalog.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal),
        _catalog.Catalog.ErrorTypes.Select(e => e.Code).ToHashSet(StringComparer.Ordinal));
}

/// <summary>Reads and writes one objective telc task as JSON in the shape of its content record.</summary>
public static class TelcDraftCodec
{
    /// <summary>A content holding just this task, with <paramref name="id"/> and the part's Teil number set; null when the JSON does not fit.</summary>
    public static TelcContent? TryRead(TelcPart part, string json, string id)
    {
        try
        {
            var e = TelcContent.Empty;
            return part switch
            {
                TelcPart.Lesen1 => e with { Lesen1 = [Read<TelcLesen1>(json) with { Id = id }] },
                TelcPart.Lesen2 => e with { Lesen2 = [Read<TelcLesen2>(json) with { Id = id }] },
                TelcPart.Lesen3 => e with { Lesen3 = [Read<TelcLesen3>(json) with { Id = id }] },
                TelcPart.Sprachbausteine1 or TelcPart.Sprachbausteine2 => e with
                {
                    Sprachbausteine = [Read<TelcSprachbausteine>(json) with { Id = id, Teil = part == TelcPart.Sprachbausteine1 ? 1 : 2 }],
                },
                TelcPart.Hoeren1 or TelcPart.Hoeren2 or TelcPart.Hoeren3 => e with
                {
                    Hoeren = [Read<TelcHoeren>(json) with { Id = id, Teil = part - TelcPart.Hoeren1 + 1 }],
                },
                _ => null,
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Model output is held to the records' own contract: a missing field or a null where the record has a
    /// non-nullable list fails here, as a <see cref="JsonException"/>, instead of as a crash in the validator later.
    /// </summary>
    private static readonly JsonSerializerOptions Strict = new(ContentLoader.Options)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>The tutor's JSON leaves out the id and the Teil number - the caller sets both.</summary>
    private static T Read<T>(string json) where T : class
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("leer");
        node["id"] = "x";
        node["teil"] = 0;
        return node.Deserialize<T>(Strict) ?? throw new InvalidDataException("leer");
    }

    /// <summary>One task of an objective part, serialized without its id - the example a generation prompt shows.</summary>
    public static string Example(TelcContent content, TelcPart part, string id)
    {
        object? task = part switch
        {
            TelcPart.Lesen1 => content.FindLesen1(id),
            TelcPart.Lesen2 => content.FindLesen2(id),
            TelcPart.Lesen3 => content.FindLesen3(id),
            TelcPart.Sprachbausteine1 or TelcPart.Sprachbausteine2 => content.FindSprachbausteine(id),
            TelcPart.Hoeren1 or TelcPart.Hoeren2 or TelcPart.Hoeren3 => content.FindHoeren(id),
            _ => null,
        };
        if (task is null) throw new KeyNotFoundException(id);
        var node = JsonSerializer.SerializeToNode(task, task.GetType(), ContentLoader.Options)!.AsObject();
        node.Remove("id");
        node.Remove("teil");
        node.Remove("statements"); // TelcHoeren's computed flattening, not part of the format
        return node.ToJsonString(new JsonSerializerOptions(ContentLoader.Options) { WriteIndented = false });
    }

    /// <summary>The task as stored: its record serialized, so an approved draft reads back exactly as reviewed.</summary>
    public static string Write(TelcContent single, TelcPart part)
    {
        var id = single.TaskIds(part).Single();
        var node = System.Text.Json.Nodes.JsonNode.Parse(Example(single, part, id))!;
        return node.ToJsonString(ContentLoader.Options);
    }
}
