namespace Lotse.Core.Model;

/// <summary>In-memory view of all learning content: taxonomy, error codes and the exercise bank (seed + generated).</summary>
public sealed class ContentCatalog
{
    private readonly Dictionary<string, SkillNode> _nodes;
    private readonly Dictionary<string, ErrorType> _errors;
    private readonly Dictionary<string, Exercise> _exercises;
    private readonly ILookup<string, Exercise> _byNode;

    public ContentCatalog(IEnumerable<SkillNode> nodes, IEnumerable<ErrorType> errors, IEnumerable<Exercise> exercises)
    {
        _nodes = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        _errors = errors.ToDictionary(e => e.Code, StringComparer.Ordinal);
        _exercises = exercises.ToDictionary(e => e.Id, StringComparer.Ordinal);
        _byNode = _exercises.Values.ToLookup(e => e.NodeId, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<SkillNode> Nodes => _nodes.Values;
    public IReadOnlyCollection<ErrorType> ErrorTypes => _errors.Values;
    public IReadOnlyCollection<Exercise> Exercises => _exercises.Values;

    public SkillNode? Node(string id) => _nodes.GetValueOrDefault(id);
    public ErrorType? Error(string code) => _errors.GetValueOrDefault(code);
    public Exercise? Exercise(string id) => _exercises.GetValueOrDefault(id);
    public IEnumerable<Exercise> ForNode(string nodeId) => _byNode[nodeId];

    public string NodeTitle(string id) => Node(id)?.Title ?? id;

    /// <summary>Returns a catalog that also contains <paramref name="extra"/> exercises (e.g. AI-generated ones loaded from the database).</summary>
    public ContentCatalog With(IEnumerable<Exercise> extra)
        => new(_nodes.Values, _errors.Values, _exercises.Values.Concat(extra.Where(e => !_exercises.ContainsKey(e.Id))));

    /// <summary>Structural validation of the whole catalog. Returns human-readable problems; empty = valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        var nodeIds = _nodes.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var n in _nodes.Values)
            foreach (var p in n.Prerequisites)
                if (!nodeIds.Contains(p)) problems.Add($"Knoten {n.Id}: unbekannte Voraussetzung '{p}'");

        foreach (var e in _errors.Values)
            if (!nodeIds.Contains(e.NodeId)) problems.Add($"Fehlercode {e.Code}: unbekannter Knoten '{e.NodeId}'");

        foreach (var ex in _exercises.Values)
            problems.AddRange(ExerciseValidator.Validate(ex, nodeIds));

        return problems;
    }
}
