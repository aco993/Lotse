using System.Text.RegularExpressions;
using Lotse.Core.Model;

namespace Lotse.Core.Engine;

/// <summary>One mistake found in free text, tagged with a code from the error catalogue.</summary>
public sealed record TextFinding(string Code, string Snippet, string Correction, string Explanation);

/// <summary>What the analyzer could say about a text without a language model.</summary>
public sealed record FreeTextReport(
    IReadOnlyList<TextFinding> Findings,
    /// <summary>Observations worth showing but not certain enough to count as errors.</summary>
    IReadOnlyList<string> Hints,
    int WordCount,
    int ConnectorCount)
{
    public static readonly FreeTextReport Empty = new([], [], 0, 0);
}

/// <summary>Nouns the analyzer knows: gender and countability, harvested from the vocabulary bank.</summary>
public sealed class Lexicon
{
    private readonly Dictionary<string, (string Article, bool Countable)> _nouns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _umlautForms = new(StringComparer.OrdinalIgnoreCase);

    public static Lexicon FromCatalog(ContentCatalog catalog)
    {
        var lex = new Lexicon();
        foreach (var e in catalog.Exercises.Where(e => e.Type == ExerciseType.Vocab && e.Article is not null && !string.IsNullOrWhiteSpace(e.Lemma)))
        {
            if (e.Lemma!.Contains(' ')) continue;
            lex._nouns[e.Lemma] = (e.Article!, !string.Equals(e.Plural, "nur Sg.", StringComparison.OrdinalIgnoreCase));
            lex.AddUmlautForm(e.Lemma);
            if (e.Plural is { } plural && !plural.StartsWith("nur", StringComparison.OrdinalIgnoreCase))
                lex.AddUmlautForm(plural.Split(' ').Last());
        }
        foreach (var w in CommonUmlautWords) lex.AddUmlautForm(w);
        return lex;
    }

    public static Lexicon Empty { get; } = new();

    public bool TryGetNoun(string word, out string article, out bool countable)
    {
        if (_nouns.TryGetValue(word, out var n)) { article = n.Article; countable = n.Countable; return true; }
        article = ""; countable = false; return false;
    }

    /// <summary>The correctly spelled form for a word written with ae/oe/ue/ss (e.g. "fuer" → "für"), if known.</summary>
    public string? UmlautCorrection(string word) => _umlautForms.GetValueOrDefault(word);

    private void AddUmlautForm(string word)
    {
        var plain = word.Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("Ä", "Ae").Replace("Ö", "Oe").Replace("Ü", "Ue").Replace("ß", "ss");
        if (plain != word) _umlautForms.TryAdd(plain, word);
    }

    private static readonly string[] CommonUmlautWords =
    [
        "für", "über", "während", "später", "früher", "können", "müssen", "möchte", "möchten", "würde", "würden", "wäre", "wären",
        "hätte", "hätten", "könnte", "könnten", "müsste", "müssten", "schön", "natürlich", "zurück", "Grüße", "Grüßen", "größer",
        "größte", "wünsche", "wünschen", "Wünsche", "Küche", "Bücher", "Stück", "Glück", "glücklich", "erklären", "Erklärung",
        "Gespräch", "Vorschläge", "Lösung", "Lösungen", "ändern", "Änderung", "möglich", "Möglichkeit", "persönlich", "regelmäßig",
        "gemütlich", "Nähe", "Verspätung", "Anfänger", "Bestätigung", "gehört", "Straße", "Straßen", "groß", "große", "großen",
        "außerdem", "heißt", "weiß", "schließlich", "Maßnahme", "Maßnahmen", "Spaß", "Fuß", "dass",
    ];
}

/// <summary>
/// Rule-based feedback on free writing, for learners without a tutor key. Every rule is chosen for precision over
/// recall: a false "error" costs trust, a missed one costs little, because the self-check and the exam rubric
/// still exist. The rules are the ones a Serbian speaker's texts actually trip over - auxiliary choice in the
/// perfect, verb position after subordinators and fronted adverbials, the missing comma before dass/weil,
/// umlaut and ß spelling, formal register, n-declension, case after prepositions (gender from the vocabulary bank)
/// - and each finding carries the catalogue code, so it feeds the learner model exactly like an AI-tagged error.
/// </summary>
public static class FreeTextAnalyzer
{
    private const string Pronouns = "ich|du|er|sie|es|wir|ihr|man";
    private const string Finite = "bin|bist|ist|sind|seid|habe|hast|hat|haben|habt|hatte|hatten|war|waren|kann|kannst|können|könnt|muss|musst|müssen|müsst|will|willst|wollen|wollt|soll|sollst|sollen|darf|darfst|dürfen|werde|wirst|wird|werden|möchte|möchtest|möchten|würde|würdest|würden|könnte|könnten|müsste|müssten|sollte|sollten|gehe|gehst|geht|gehen|komme|kommst|kommt|kommen|mache|machst|macht|machen|arbeite|arbeitest|arbeitet|arbeiten|finde|findest|findet|finden|denke|denkst|denkt|denken|glaube|glaubst|glaubt|glauben|brauche|brauchst|braucht|brauchen|wohne|wohnst|wohnt|wohnen|lerne|lernst|lernt|lernen|sehe|siehst|sieht|sehen|weiß|weißt|wissen";
    private const string Subordinators = "dass|weil|obwohl|ob|damit|während|nachdem|bevor|sodass|falls|sobald|solange|wenn";
    private static readonly string[] Connectors =
    [
        "weil", "deshalb", "deswegen", "daher", "obwohl", "trotzdem", "außerdem", "zudem", "einerseits", "andererseits", "zwar",
        "allerdings", "jedoch", "dennoch", "sodass", "damit", "während", "nachdem", "bevor", "meiner meinung nach", "ich bin der ansicht",
        "ich finde", "zum beispiel", "beispielsweise", "im gegensatz", "sowohl", "nicht nur", "zusammenfassend", "abschließend", "erstens", "zweitens", "schließlich",
    ];
    // Precision over recall, as everywhere in this file: only participles whose auxiliary is unambiguous.
    // "gefahren", "geflogen" and "gefallen" are out - "hat das Auto gefahren", "hat die Maschine geflogen" and
    // "hat mir gefallen" are all correct with haben, and the first version marked "Der Film hat mir gefallen"
    // wrong, which is textbook B1 German.
    private static readonly HashSet<string> SeinParticiples = new(StringComparer.OrdinalIgnoreCase)
    {
        "gegangen", "gekommen", "geblieben", "gestiegen", "gelaufen", "aufgestanden", "passiert", "gestorben",
        "eingeschlafen", "umgezogen", "gewesen", "geworden", "angekommen", "eingestiegen", "ausgestiegen", "gewachsen", "gelungen", "gereist", "gewandert",
    };
    private static readonly HashSet<string> HabenParticiples = new(StringComparer.OrdinalIgnoreCase)
    {
        "gearbeitet", "gemacht", "gehabt", "gesagt", "gesehen", "gekauft", "gelernt", "geschrieben", "gelesen", "gegessen", "getrunken",
        "gespielt", "gehört", "gefunden", "genommen", "gegeben", "bekommen", "verstanden", "besucht", "getroffen", "angefangen", "beendet", "geschlafen", "gewohnt", "gefragt", "geantwortet",
    };
    // "sein" + one of these is the Zustandspassiv, not a wrong auxiliary: "Die Arbeit ist beendet", "Das Auto
    // ist gekauft", "Der Brief ist geschrieben". Only intransitive haben-verbs can be flagged after "sein".
    private static readonly HashSet<string> NoStatePassive = new(StringComparer.OrdinalIgnoreCase)
    {
        "gearbeitet", "gehabt", "geschlafen", "gewohnt", "geantwortet",
    };
    // Bare-noun predicates: "Angst haben", "Hunger haben", "Zeit haben" take no article even though the noun is
    // countable elsewhere ("die Ängste"). The vocabulary card for Angst lists a plural, so the countability rule
    // alone flagged the card's own example sentence.
    private static readonly HashSet<string> BareNounPredicates = new(StringComparer.OrdinalIgnoreCase)
    {
        "Angst", "Hunger", "Durst", "Zeit", "Lust", "Recht", "Glück", "Pech", "Urlaub", "Feierabend", "Geburtstag", "Fieber",
        "Besuch", "Spaß", "Ahnung", "Mut", "Geduld", "Erfolg", "Kontakt", "Platz", "Bedarf", "Vorrang", "Schuld", "Sinn",
    };
    private static readonly Dictionary<string, string> SzSpellings = new(StringComparer.Ordinal)
    {
        ["daß"] = "dass",
        ["Strasse"] = "Straße",
        ["gross"] = "groß",
        ["grosse"] = "große",
        ["grossen"] = "großen",
        ["Fuss"] = "Fuß",
        ["heisst"] = "heißt",
        ["weiss"] = "weiß",
        ["schliesslich"] = "schließlich",
        ["Massnahme"] = "Maßnahme",
        ["Massnahmen"] = "Maßnahmen",
        ["ausserdem"] = "außerdem",
        ["Spass"] = "Spaß",
        ["Grüsse"] = "Grüße",
        ["Grüssen"] = "Grüßen",
        ["regelmässig"] = "regelmäßig",
        ["draussen"] = "draußen",
    };
    private static readonly Dictionary<string, (string Wrong, string Right)> VerbPrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["warten"] = ("für", "auf"),
        ["warte"] = ("für", "auf"),
        ["wartet"] = ("für", "auf"),
        ["gewartet"] = ("für", "auf"),
        ["interessieren"] = ("über", "für"),
        ["interessiere"] = ("über", "für"),
        ["interessiert"] = ("über", "für"),
        ["freue"] = ("für", "über/auf"),
        ["freuen"] = ("für", "über/auf"),
        ["freut"] = ("für", "über/auf"),
        ["teilnehmen"] = ("bei", "an"),
        ["teilgenommen"] = ("bei", "an"),
        ["bewerben"] = ("für", "um"),
        ["beworben"] = ("für", "um"),
        ["bewerbe"] = ("für", "um"),
        ["kümmern"] = ("für", "um"),
        ["kümmere"] = ("für", "um"),
        ["kümmert"] = ("für", "um"),
        ["beschweren"] = ("auf", "über"),
        ["beschwere"] = ("auf", "über"),
        ["antworten"] = ("an", "auf"),
        ["antworte"] = ("an", "auf"),
        ["geantwortet"] = ("an", "auf"),
        ["fragen"] = ("für", "nach"),
        ["frage"] = ("für", "nach"),
        ["gefragt"] = ("für", "nach"),
        ["denke"] = ("auf", "an"),
        ["denken"] = ("auf", "an"),
        ["gedacht"] = ("auf", "an"),
        ["abhängt"] = ("auf", "von"),
        ["abhängen"] = ("auf", "von"),
    };
    private static readonly string[] NDeclensionNouns = ["Kollege", "Kunde", "Student", "Praktikant", "Präsident", "Nachbar", "Herr", "Mensch", "Junge", "Experte", "Patient", "Name", "Gedanke", "Assistent", "Zeuge", "Bauer", "Held"];
    private static readonly string[] FalseFriends = ["Konkurs", "Magazin", "Pension", "Mappe", "Ambulanz", "Akademiker", "Kompromiss", "Familiär"];

    private static readonly Regex WordRe = new(@"\p{L}[\p{L}\-]*", RegexOptions.Compiled);
    private static readonly Regex MissingCommaRe = new($@"(?<=[\p{{L}}\d])\s+(?<sub>{Subordinators})\s+(?={Pronouns}|der|die|das|es|man|ein|eine|sie|wir)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SubordinateOrderRe = new($@"\b(?<sub>{Subordinators})\s+(?<subj>{Pronouns})\s+(?<verb>{Finite})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FrontedAdverbRe = new($@"(?:^|[.!?]\s+)(?<adv>Gestern|Heute|Morgen|Dann|Danach|Deshalb|Deswegen|Trotzdem|Leider|Zuerst|Jetzt|Später|Manchmal|Oft|Vielleicht|Natürlich|Außerdem|Zum Glück|Am Wochenende|Am Montag|Am Dienstag|Am Freitag|Im Sommer|Im Winter|Seit einem Jahr|Nächste Woche|Letzte Woche|Am Anfang|Zum Schluss)\s*,?\s+(?<subj>{Pronouns})\s+(?<verb>{Finite})\b", RegexOptions.Compiled);
    private static readonly Regex DativePrepRe = new(@"\b(?<prep>mit|nach|aus|bei|von|zu|seit|gegenüber)\s+(?<art>die|das|ein|eine|einen|meine|deine|seine|ihre|unsere)\s+(?<noun>\p{Lu}\p{L}+)", RegexOptions.Compiled);
    private static readonly Regex AccusativePrepRe = new(@"\b(?<prep>für|ohne|gegen|um|durch)\s+(?<art>dem|einem|einer|meinem|deinem|seinem|ihrem|unserem)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NDeclensionRe = new($@"\b(?<art>dem|den|des|einem|einen|eines|meinem|meinen|seinem|seinen|ihrem|ihren|unserem|unseren)\s+(?<noun>{string.Join('|', NDeclensionNouns)})(?=[\s.,!?;:]|$)", RegexOptions.Compiled);
    private static readonly Regex MissingArticleRe = new(@"\b(?<verb>habe|hast|hat|haben|brauche|brauchst|braucht|brauchen|suche|suchst|sucht|suchen|schreibe|schreibt|bekomme|bekommt|möchte|möchten|kaufe|kauft|hatte|hatten)\s+(?<noun>\p{Lu}\p{L}+)(?=[\s.,!?;:]|$)", RegexOptions.Compiled);
    private static readonly Regex LowercaseNounRe = new(@"\b(?<det>der|die|das|den|dem|des|ein|eine|einen|einem|einer|mein|meine|meinen|meinem|dein|deine|sein|seine|ihr|ihre|unser|unsere|kein|keine|keinen|dieser|diese|dieses|jeder|jede|jedes)\s+(?<word>\p{Ll}\p{L}+)(?=[\s.,!?;:]|$)", RegexOptions.Compiled);
    private static readonly Regex GreetingRe = new(@"\bMit\s+freundliche(?:n)?\s+Gr(?:ü|ue)(?:ß|ss)e(?:n)?\b", RegexOptions.Compiled);
    private static readonly Regex InformalRe = new(@"\b(du|dich|dir|dein|deine|deinen|deinem|hallo|hi|tschüss|lg)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <param name="spoken">A speech transcript has no punctuation or capitalisation of its own - spelling, comma and greeting rules are skipped.</param>
    public static FreeTextReport Analyze(string text, Exercise task, Lexicon lexicon, bool spoken = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return FreeTextReport.Empty;
        var findings = new List<TextFinding>();
        var hints = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string code, string snippet, string correction, string explanation)
        {
            if (seen.Add(code + "|" + snippet.ToLowerInvariant())) findings.Add(new TextFinding(code, snippet.Trim(), correction, explanation));
        }

        var words = WordRe.Matches(text).Select(m => m.Value).ToList();
        var wordCount = AnswerChecker.CountWords(text);
        var lower = text.ToLowerInvariant();
        var formal = IsFormalTask(task);

        // --- spelling: umlaut digraphs and ß -----------------------------------------------------------
        foreach (var w in spoken ? [] : words.Distinct(StringComparer.Ordinal))
        {
            if (SzSpellings.TryGetValue(w, out var sz)) Add("ORTH_SZ", w, sz, "ß nach langem Vokal/Diphthong, ss nach kurzem: „dass“, „Straße“, „groß“.");
            else if (lexicon.UmlautCorrection(w) is { } um && um != w && !w.Equals(um, StringComparison.Ordinal))
                Add("ORTH_UMLAUT", w, um, "ae/oe/ue/ss sind Notlösungen ohne deutsche Tastatur – im Text gehören ä/ö/ü/ß hin.");
        }

        // --- perfect: haben/sein -----------------------------------------------------------------------
        // From each auxiliary, walk forward to the first participle of the same clause (stop at punctuation or a
        // conjunction) - the participle decides which auxiliary was right.
        var tokens = text.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var aux = tokens[i].Trim(',', '.', '!', '?', ';', ':');
            var auxIsSein = aux is "bin" or "bist" or "ist" or "sind" or "seid";
            var auxIsHaben = aux is "habe" or "hast" or "hat" or "haben" or "habt";
            if (!auxIsSein && !auxIsHaben || tokens[i].EndsWith(',') || tokens[i].EndsWith('.')) continue;
            for (var j = i + 1; j < Math.Min(tokens.Length, i + 9); j++)
            {
                var word = tokens[j].Trim(',', '.', '!', '?', ';', ':', '"', '“', '”');
                if (word is "und" or "oder" or "aber" or "dass" or "weil" or "denn") break;
                var isSeinPart = SeinParticiples.Contains(word);
                var isHabenPart = HabenParticiples.Contains(word);
                if (isSeinPart || isHabenPart)
                {
                    var phrase = string.Join(' ', tokens[i..(j + 1)]).TrimEnd('.', ',', '!', '?', ';', ':');
                    if (auxIsHaben && isSeinPart)
                        Add("TEMPUS_HILFSVERB", phrase, phrase.Replace(aux, SeinFor(aux)), $"„{word}“ bildet das Perfekt mit „sein“ (Bewegung/Zustandswechsel). Serbisch nutzt immer „biti“ – im Deutschen entscheidet das Verb.");
                    else if (auxIsSein && isHabenPart && NoStatePassive.Contains(word))
                        Add("TEMPUS_HILFSVERB", phrase, phrase.Replace(aux, HabenFor(aux)), $"„{word}“ bildet das Perfekt mit „haben“ – „sein“ nur bei Bewegung, Zustandswechsel und sein/bleiben/werden.");
                    break;
                }
                if (tokens[j].EndsWith(',') || tokens[j].EndsWith('.') || tokens[j].EndsWith('!') || tokens[j].EndsWith('?') || tokens[j].EndsWith(';')) break;
            }
        }

        // --- word order -------------------------------------------------------------------------------
        foreach (Match m in SubordinateOrderRe.Matches(text))
        {
            var sub = m.Groups["sub"].Value; var subj = m.Groups["subj"].Value; var verb = m.Groups["verb"].Value;
            var rest = text[(m.Index + m.Length)..].Split(['.', ',', '!', '?', ';'], 2)[0].Trim();
            if (rest.Length == 0) continue; // "…, wenn ich kann." is fine
            Add("WORTST_NEBENSATZ", $"{sub} {subj} {verb} {Shorten(rest)}", $"{sub} {subj} {Shorten(rest)} {verb}", $"Nach „{sub}“ steht das konjugierte Verb am Ende des Nebensatzes.");
        }
        foreach (Match m in FrontedAdverbRe.Matches(text))
        {
            var adv = m.Groups["adv"].Value; var subj = m.Groups["subj"].Value; var verb = m.Groups["verb"].Value;
            var code = adv is "Deshalb" or "Deswegen" or "Trotzdem" or "Außerdem" or "Dann" or "Danach" ? "KONNEKTOR_STELLUNG" : "WORTST_V2";
            Add(code, $"{adv} {subj} {verb}", $"{adv} {verb} {subj}", $"Steht „{adv}“ am Satzanfang, folgt sofort das Verb (Position 2), dann das Subjekt.");
        }

        // --- comma before subordinate clause ----------------------------------------------------------
        foreach (Match m in spoken ? [] : MissingCommaRe.Matches(text).Cast<Match>())
        {
            var sub = m.Groups["sub"].Value;
            var before = text[..m.Index].Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
            if (before.Equals("so", StringComparison.OrdinalIgnoreCase) || before.Equals("auch", StringComparison.OrdinalIgnoreCase) && sub.Equals("wenn", StringComparison.OrdinalIgnoreCase)) continue;
            Add("KOMMA", $"{before} {sub}", $"{before}, {sub}", $"Vor „{sub}“ beginnt ein Nebensatz – davor steht immer ein Komma.");
        }

        // --- case after prepositions, n-declension, articles (gender from the lexicon) -------------------
        foreach (Match m in DativePrepRe.Matches(text))
        {
            var prep = m.Groups["prep"].Value; var art = m.Groups["art"].Value; var noun = m.Groups["noun"].Value;
            if (!lexicon.TryGetNoun(noun, out var gender, out _)) continue;
            var right = DativeArticle(art, gender);
            if (right is null || right == art) continue;
            Add("KASUS_PRAEP", $"{prep} {art} {noun}", $"{prep} {right} {noun}", $"„{prep}“ verlangt immer den Dativ: {gender} {noun} → {prep} {right} {noun}.");
        }
        foreach (Match m in AccusativePrepRe.Matches(text))
        {
            var prep = m.Groups["prep"].Value; var art = m.Groups["art"].Value;
            var right = art.ToLowerInvariant() switch { "dem" => "den/das", "einem" => "einen/ein", "einer" => "eine", _ => art.Replace("em", "en") };
            Add("KASUS_PRAEP", $"{prep} {art}", $"{prep} {right}", $"„{prep}“ verlangt immer den Akkusativ – „{art}“ ist Dativ.");
        }
        foreach (Match m in NDeclensionRe.Matches(text))
        {
            var art = m.Groups["art"].Value; var noun = m.Groups["noun"].Value;
            var ending = noun.EndsWith('e') ? "n" : "en";
            Add("N_DEKLINATION", $"{art} {noun}", $"{art} {noun}{ending}", $"„{noun}“ gehört zur n-Deklination: außer im Nominativ Singular immer „{noun}{ending}“.");
        }
        foreach (Match m in MissingArticleRe.Matches(text))
        {
            var noun = m.Groups["noun"].Value;
            if (BareNounPredicates.Contains(noun)) continue;
            if (!lexicon.TryGetNoun(noun, out var gender, out var countable) || !countable) continue;
            var verb = m.Groups["verb"].Value;
            var art = gender switch { "der" => "einen", "das" => "ein", _ => "eine" };
            Add("ART_FEHLT", $"{verb} {noun}", $"{verb} {art} {noun}", "Serbisch kommt ohne Artikel aus – im Deutschen braucht ein zählbares Nomen im Singular fast immer einen.");
        }
        foreach (Match m in spoken ? [] : LowercaseNounRe.Matches(text).Cast<Match>())
        {
            var word = m.Groups["word"].Value;
            var cap = char.ToUpperInvariant(word[0]) + word[1..];
            if (!lexicon.TryGetNoun(cap, out _, out _)) continue;
            Add("ORTH_GROSSSCHREIBUNG", $"{m.Groups["det"].Value} {word}", $"{m.Groups["det"].Value} {cap}", "Nomen werden im Deutschen großgeschrieben – nach einem Artikel steht fast immer eines.");
        }

        // --- verb + preposition --------------------------------------------------------------------------
        for (var i = 0; i < words.Count - 1; i++)
        {
            if (!VerbPrepositions.TryGetValue(words[i], out var vp)) continue;
            for (var j = i + 1; j <= Math.Min(i + 3, words.Count - 1); j++)
            {
                if (words[j].Equals(vp.Wrong, StringComparison.OrdinalIgnoreCase))
                {
                    Add("VERB_PRAEP", $"{words[i]} … {words[j]}", $"{words[i]} … {vp.Right}", $"Feste Verbindung: „{words[i]}“ + „{vp.Right}“ – die Präposition folgt dem Verb, nicht der serbischen Logik.");
                    break;
                }
                if (words[j].Equals(vp.Right.Split('/')[0], StringComparison.OrdinalIgnoreCase)) break;
            }
        }

        // --- register and greeting (formal tasks) ---------------------------------------------------------
        if (!spoken && GreetingRe.Match(text) is { Success: true } g && g.Value != "Mit freundlichen Grüßen")
            Add("REG_ANREDE_GRUSS", g.Value, "Mit freundlichen Grüßen", "Feste Formel, Dativ Plural: „Mit freundlichen Grüßen“ – ohne Komma danach.");
        if (formal)
        {
            var informal = InformalRe.Matches(text).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
            if (informal.Count > 0)
                Add("REG_FORMELL", string.Join(", ", informal), "Sie / Ihnen / Ihr …", "Die Aufgabe ist formell: durchgehend „Sie“, keine Umgangssprache, formelle Anrede und Grußformel.");
            if (!spoken && !lower.Contains("sehr geehrte") && !lower.Contains("liebe") && !lower.Contains("guten tag"))
                hints.Add("Formelle Nachricht ohne Anrede – „Sehr geehrte Damen und Herren,“ oder „Sehr geehrte Frau …,“ gehört an den Anfang.");
        }

        // --- length and coherence ---------------------------------------------------------------------------
        if (task.MinWords is { } min && wordCount < min * 0.8)
            Add("TXT_LAENGE", $"{wordCount} Wörter", $"ca. {min} Wörter", "Deutlich unter der geforderten Länge – in der Prüfung kostet das Punkte bei „Erfüllung“.");
        var connectorCount = Connectors.Count(c => lower.Contains(c));
        if (wordCount >= 80 && connectorCount <= 1)
            Add("TXT_KOHAERENZ", "wenige Konnektoren", "weil, deshalb, obwohl, außerdem, einerseits … andererseits", "Die Sätze stehen nebeneinander statt verbunden – B2 erwartet Konnektoren und Nebensätze.");

        // --- advisory only: false friends ------------------------------------------------------------------
        foreach (var ff in FalseFriends.Where(f => words.Contains(f, StringComparer.Ordinal)))
            hints.Add(ff switch
            {
                "Konkurs" => "„Konkurs“ heißt Insolvenz – für eine Ausschreibung: „Ausschreibung“, „Stellenanzeige“; für einen Wettbewerb: „Wettbewerb“.",
                "Magazin" => "„Magazin“ ist eine Zeitschrift oder ein Lager – ein Geschäft ist „das Geschäft/der Laden“.",
                "Pension" => "„Pension“ ist die Beamtenrente oder eine kleine Unterkunft – die normale Altersrente heißt „Rente“.",
                "Mappe" => "„Mappe“ ist eine Sammelmappe für Papiere – eine Landkarte heißt „Karte“.",
                "Ambulanz" => "„Ambulanz“ ist die Notaufnahme/Ambulanz eines Krankenhauses – die Praxis heißt „Arztpraxis“.",
                "Akademiker" => "„Akademiker“ ist jeder mit Hochschulabschluss – ein Mitglied der Akademie heißt „Akademiemitglied“.",
                _ => $"Prüfe „{ff}“ – ein falscher Freund aus dem Serbischen?",
            });

        return new FreeTextReport(findings, hints, wordCount, connectorCount);
    }

    private static bool IsFormalTask(Exercise task)
    {
        var all = (task.Prompt + " " + string.Join(' ', task.Rubric) + " " + string.Join(' ', task.Tags)).ToLowerInvariant();
        return all.Contains("formell") || all.Contains("sehr geehrte") || all.Contains("beschwerde") || all.Contains("kündigung") || all.Contains("bewerbung") || all.Contains("an die verwaltung") || all.Contains("behörde");
    }

    private static string SeinFor(string aux) => aux switch { "habe" => "bin", "hast" => "bist", "hat" => "ist", "haben" => "sind", "habt" => "seid", _ => aux };
    private static string HabenFor(string aux) => aux switch { "bin" => "habe", "bist" => "hast", "ist" => "hat", "sind" => "haben", "seid" => "habt", _ => aux };

    private static string? DativeArticle(string article, string gender) => (article.ToLowerInvariant(), gender) switch
    {
        ("die", "die") => "der",
        ("die", "der") => "dem",
        ("die", "das") => "dem",
        ("das", _) => "dem",
        ("ein", _) => "einem",
        ("eine", _) => "einer",
        ("einen", _) => "einem",
        ("meine", "die") => "meiner",
        ("meine", _) => "meinem",
        ("deine", "die") => "deiner",
        ("deine", _) => "deinem",
        ("seine", "die") => "seiner",
        ("seine", _) => "seinem",
        ("ihre", "die") => "ihrer",
        ("ihre", _) => "ihrem",
        ("unsere", "die") => "unserer",
        ("unsere", _) => "unserem",
        _ => null,
    };

    private static string Shorten(string s) => s.Length <= 40 ? s : s[..37] + "…";
}
