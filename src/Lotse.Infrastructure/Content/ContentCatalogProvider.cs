using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lotse.Infrastructure.Content;

/// <summary>
/// Holds the live catalog: seed content from disk plus AI-generated exercises from the database.
/// Singleton; swapping the catalog reference is atomic, readers never see a half-built catalog.
/// </summary>
public sealed class ContentCatalogProvider
{
    private readonly ContentCatalog _seed;
    private readonly IDbContextFactory<LotseDbContext> _dbFactory;
    private readonly ILogger<ContentCatalogProvider> _logger;
    private volatile ContentCatalog _current;

    public ContentCatalogProvider(string contentDirectory, IDbContextFactory<LotseDbContext> dbFactory, ILogger<ContentCatalogProvider> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _seed = ContentLoader.Load(contentDirectory);
        _current = _seed;
        _logger.LogInformation("Content geladen: {Nodes} Knoten, {Errors} Fehlercodes, {Exercises} Übungen aus {Dir}", _seed.Nodes.Count, _seed.ErrorTypes.Count, _seed.Exercises.Count, contentDirectory);
    }

    public ContentCatalog Catalog => _current;

    private Lexicon? _lexicon;
    private ContentCatalog? _lexiconFor;

    /// <summary>Nouns (gender, countability) and umlaut spellings known from the vocabulary bank, for the rule-based text analyzer. Rebuilt lazily whenever the catalog changes.</summary>
    public Lexicon Lexicon
    {
        get
        {
            var current = _current;
            if (!ReferenceEquals(_lexiconFor, current) || _lexicon is null)
            {
                _lexicon = Lexicon.FromCatalog(current);
                _lexiconFor = current;
            }
            return _lexicon;
        }
    }

    public int SeedExerciseCount => _seed.Exercises.Count;
    public int GeneratedExerciseCount => _current.Exercises.Count - _seed.Exercises.Count;

    /// <summary>Loads generated exercises from the database and merges them. Called once at startup and after each generation.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.GeneratedExercises.AsNoTracking().ToListAsync(ct);
        var extra = new List<Exercise>();
        foreach (var row in rows)
        {
            try
            {
                var ex = ContentLoader.DeserializeExercise(row.Json);
                // Same key balancing as the seed files get in ContentLoader - a model's habit is as fixed as an author's.
                if (ex is not null && ExerciseValidator.Validate(ex, _seed.Nodes.Select(n => n.Id).ToHashSet()).Count == 0) extra.Add(KeyShuffle.Apply(ex));
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Generierte Übung {Id} unlesbar, übersprungen.", row.Id);
            }
        }
        _current = _seed.With(extra);
    }
}
