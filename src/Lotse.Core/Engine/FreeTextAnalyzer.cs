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
    private readonly Dictionary<string, (string Article, bool Countable, string? Plural)> _nouns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _umlautForms = new(StringComparer.OrdinalIgnoreCase);

    public static Lexicon FromCatalog(ContentCatalog catalog)
    {
        var lex = new Lexicon();
        foreach (var e in catalog.Exercises.Where(e => e.Type == ExerciseType.Vocab && e.Article is not null && !string.IsNullOrWhiteSpace(e.Lemma)))
        {
            if (e.Lemma!.Contains(' ')) continue;
            var plural = e.Plural is { } p && !p.StartsWith("nur", StringComparison.OrdinalIgnoreCase) ? p.Split(' ').Last() : null;
            lex._nouns[e.Lemma] = (e.Article!, !string.Equals(e.Plural, "nur Sg.", StringComparison.OrdinalIgnoreCase), plural);
            lex.AddUmlautForm(e.Lemma);
            if (plural is not null) lex.AddUmlautForm(plural);
        }
        foreach (var (article, lemma, plural) in CoreNouns.All)
        {
            if (!lex._nouns.TryAdd(lemma, (article, plural is not null, plural))) continue;
            lex.AddUmlautForm(lemma);
            if (plural is not null) lex.AddUmlautForm(plural);
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

    /// <summary>
    /// The gender of a singular noun for the agreement rules: the entry itself, else the head of a compound
    /// ("Kundenservice" → nothing, "Arbeitsplatz" → der Platz), else one of the suffixes that never lie (-ung, -heit,
    /// -keit, -schaft, -tion, -sion, -tät are feminine). <paramref name="pluralLooksSame"/> is true for nouns like
    /// "Zimmer", whose plural is spelled like the singular - "die Zimmer" is then a correct plural, not a gender slip.
    /// </summary>
    public bool TryGetGender(string noun, out string article, out bool pluralLooksSame)
    {
        if (_nouns.TryGetValue(noun, out var n)) { article = n.Article; pluralLooksSame = n.Plural == noun; return true; }
        // Heads of at least four letters after a first part of at least four: "Antwort" is not "Ant" + das Wort.
        for (var k = 4; k <= noun.Length - 4; k++)
        {
            var head = char.ToUpperInvariant(noun[k]) + noun[(k + 1)..];
            if (UnreliableHeads.Contains(head) || !_nouns.TryGetValue(head, out var h)) continue;
            article = h.Article; pluralLooksSame = h.Plural == head; return true;
        }
        if (noun.Length >= 6 && FeminineSuffixes.Any(s => noun.EndsWith(s, StringComparison.Ordinal)) && !MasculineUng.Any(s => noun.EndsWith(s, StringComparison.Ordinal)))
        {
            article = "die"; pluralLooksSame = false; return true;
        }
        article = ""; pluralLooksSame = false; return false;
    }

    /// <summary>True for the plural form of a noun the lexicon knows ("Monate", "Versuche").</summary>
    public bool IsPlural(string word) => _plurals.Value.Contains(word);

    private readonly Lazy<HashSet<string>> _plurals;

    private Lexicon() => _plurals = new(() => _nouns.Values.Where(n => n.Plural is not null).Select(n => n.Plural!).ToHashSet(StringComparer.Ordinal));

    private static readonly string[] FeminineSuffixes = ["ung", "heit", "keit", "schaft", "tion", "sion", "tät"];
    private static readonly string[] MasculineUng = ["sprung", "schwung"];
    // Heads that give the wrong gender inside a compound: die Erdkunde, die Reisende/der Reisende, der/das Teil.
    private static readonly HashSet<string> UnreliableHeads = new(StringComparer.Ordinal) { "Kunde", "Ende", "Teil" };

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
    private const string Finite = "bin|bist|ist|sind|seid|habe|hast|hat|haben|habt|hatte|hatten|war|waren|kann|kannst|können|könnt|muss|musst|müssen|müsst|will|willst|wollen|wollt|soll|sollst|sollen|darf|darfst|dürfen|werde|wirst|wird|werden|möchte|möchtest|möchten|würde|würdest|würden|könnte|könnten|müsste|müssten|sollte|sollten|gehe|gehst|geht|gehen|komme|kommst|kommt|kommen|mache|machst|macht|machen|arbeite|arbeitest|arbeitet|arbeiten|finde|findest|findet|finden|denke|denkst|denkt|denken|glaube|glaubst|glaubt|glauben|brauche|brauchst|braucht|brauchen|wohne|wohnst|wohnt|wohnen|lerne|lernst|lernt|lernen|sehe|siehst|sieht|sehen|weiß|weißt|wissen|funktioniert|funktionieren|gibt|gefällt|passt|kostet|dauert|fehlt|hilft|liegt|steht|bleibt|kenne|kennt|spreche|spricht|verstehe|versteht|zahle|zahlt|bekomme|bekommt|kaufe|kauft|suche|sucht";
    // One adverb may stand between subject and verb: "weil er nicht funktioniert gut".
    private const string MidAdverbs = "nicht|auch|schon|noch|jetzt|leider|immer|oft|gern|sehr|wirklich|bald|dann|nur";
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
        // Mass use without an article is correct for these too: "Ich habe Erfahrung", "braucht Hilfe", "hatte Verspätung".
        "Erfahrung", "Hilfe", "Arbeit", "Familie", "Schule", "Training", "Unterricht", "Verspätung", "Rückmeldung", "Unterstützung",
        "Feedback", "Bescheid", "Auskunft", "Ärger", "Stress", "Interesse", "Zugang", "Anspruch", "Kurs", "Dienst", "Schicht", "Frühstück",
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
    private static readonly Regex SubordinateOrderRe = new($@"\b(?<sub>{Subordinators})\s+(?<subj>{Pronouns})\s+(?:(?<adv>{MidAdverbs})\s+)?(?<verb>{Finite})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FrontedAdverbRe = new($@"(?:^|[.!?]\s+)(?<adv>Gestern|Heute|Morgen|Dann|Danach|Deshalb|Deswegen|Trotzdem|Leider|Zuerst|Jetzt|Später|Manchmal|Oft|Vielleicht|Natürlich|Außerdem|Zum Glück|Am Wochenende|Am Montag|Am Dienstag|Am Freitag|Im Sommer|Im Winter|Seit einem Jahr|Nächste Woche|Letzte Woche|Am Anfang|Zum Schluss)\s*,?\s+(?<subj>{Pronouns})\s+(?<verb>{Finite})\b", RegexOptions.Compiled);
    private static readonly Regex DativePrepRe = new(@"\b(?<prep>mit|nach|aus|bei|von|zu|seit|gegenüber)\s+(?<art>die|das|ein|eine|einen|meine|deine|seine|ihre|unsere)\s+(?<noun>\p{Lu}\p{L}+)", RegexOptions.Compiled);
    private static readonly Regex AccusativePrepRe = new(@"\b(?<prep>für|ohne|gegen|um|durch)\s+(?<art>dem|einem|einer|meinem|deinem|seinem|ihrem|unserem)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NDeclensionRe = new($@"\b(?<art>dem|den|des|einem|einen|eines|meinem|meinen|seinem|seinen|ihrem|ihren|unserem|unseren)\s+(?<noun>{string.Join('|', NDeclensionNouns)})(?=[\s.,!?;:]|$)", RegexOptions.Compiled);
    private static readonly Regex MissingArticleRe = new(@"\b(?<verb>habe|hast|hat|haben|brauche|brauchst|braucht|brauchen|suche|suchst|sucht|suchen|schreibe|schreibt|bekomme|bekommt|möchte|möchten|kaufe|kauft|hatte|hatten)\s+(?<noun>\p{Lu}\p{L}+)(?=[\s.,!?;:]|$)", RegexOptions.Compiled);
    private static readonly Regex LowercaseNounRe = new(@"\b(?<det>der|die|das|den|dem|des|ein|eine|einen|einem|einer|mein|meine|meinen|meinem|dein|deine|sein|seine|ihr|ihre|unser|unsere|kein|keine|keinen|dieser|diese|dieses|jeder|jede|jedes)\s+(?<word>\p{Ll}\p{L}+)(?=[\s.,!?;:]|$)", RegexOptions.Compiled);
    private static readonly Regex GreetingRe = new(@"\bMit\s+freundliche(?:n)?\s+Gr(?:ü|ue)(?:ß|ss)e(?:n)?\b", RegexOptions.Compiled);
    // One finding per informal form, quoted with the word before it, so the learner sees where it stands.
    private static readonly Regex InformalRe = new(@"(?:\S+[ \t]+)?\b(?:du|dich|dir|dein|deine|deinen|deinem|deiner|euch|euer|eure|euren|eurem|eurer)\b|\b[Ii]hr\s+(?:habt|seid|könnt|müsst|wisst|wollt|sollt|solltet|dürft|macht|wart|hattet|lasst)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex InformalOpeningRe = new(@"^(?:Hallo|Hi|Hey|Servus|Moin)\b[^\n]*", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex InformalClosingRe = new(@"^(?:LG|VG|Tschüss|Tschüs|Ciao|Bis bald|Bis dann)\b[^\n]*(?:\n[^\n]+)?", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private const string Determiners = "der|die|das|den|dem|des|ein|eine|einen|einem|einer|eines|kein|keine|keinen|keinem|keiner|keines|mein|meine|meinen|meinem|meiner|meines|dein|deine|deinen|deinem|deiner|deines|unser|unsere|unseren|unserem|unserer|unseres|seine|seinen|seinem|seiner|seines|ihre|ihren|ihrem|ihrer|ihres|diese|dieser|dieses|diesen|diesem|jede|jeder|jedes|jeden|jedem";
    // An inflected adjective - never a determiner itself: "das keine Frage", "ist das eine Investition".
    private const string Adjective = $@"(?!(?:{Determiners}|aber|oder|immer|wieder|sehr|wenige|viele|einige|andere|beide)\b)\p{{Ll}}+(?:e|en|er|es|em)";
    // Determiner, at most one adjective (or two joined by und/oder), noun. The noun must be capitalised, so a
    // speech transcript in lower case never reaches the agreement rules.
    private static readonly Regex DeterminerNounRe = new($@"\b(?<det>(?i:{Determiners}))\s+(?:(?<adj>{Adjective})\s+(?:(?:und|oder)\s+{Adjective}\s+)?)?(?<noun>\p{{Lu}}\p{{L}}+)(?=[\s.,!?;:)]|$)", RegexOptions.Compiled);
    // A subject pronoun and a verb that takes an accusative object, then an uninflected ein/kein/mein: with a
    // masculine noun that object needs "-en". The pronoun is the subject, so "ein Laptop" cannot be one.
    private const string AccusativeVerbs = "habe|hast|hat|haben|habt|hatte|hatten|brauche|brauchst|braucht|brauchen|suche|suchst|sucht|suchen|kaufe|kaufst|kauft|kaufen|bestelle|bestellst|bestellt|bestellen|bekomme|bekommst|bekommt|bekommen|möchte|möchtest|möchten|kenne|kennst|kennt|kennen|gibt";
    private static readonly Regex AccusativeObjectRe = new($@"\b(?:(?<subj>(?i:ich|du|er|sie|es|wir|ihr|man))\s+(?<verb>{AccusativeVerbs})|(?<verb>(?i:{AccusativeVerbs}))\s+(?<subj>ich|du|er|sie|es|wir|ihr|man))\s+(?:(?:auch|schon|noch|gestern|leider|nur|jetzt|bereits|heute|online|bei\s+\p{{L}}+)\s+){{0,3}}(?<det>ein|kein|mein|dein|unser)\s+(?:(?<adj>\p{{Ll}}+e[rs]?)\s+)?(?<noun>\p{{Lu}}\p{{L}}+)(?=[\s.,!?;:)]|$)", RegexOptions.Compiled);
    // Dative plural takes -n: "seit zwei Monate" → "seit zwei Monaten". Only the prepositions that never take the
    // accusative ("auf zwei Tage begrenzt" is correct).
    private static readonly Regex DativePluralRe = new(@"\b(?<prep>(?i:mit|nach|aus|bei|von|zu|seit|gegenüber))\s+(?<num>zwei|drei|vier|fünf|sechs|sieben|acht|neun|zehn|elf|zwölf|zwanzig|dreißig|vielen|einigen|mehreren|wenigen|\d+)\s+(?<noun>\p{Lu}\p{L}*e)(?=[\s.,!?;:)]|$)", RegexOptions.Compiled);
    // Genitive prepositions followed by a nominative/accusative form, which is wrong in every register ("wegen dem"
    // is colloquial; "wegen die" is not German). "während" and "statt" are left out: both also start clauses.
    private static readonly Regex GenitivePrepRe = new(@"\b(?<prep>(?i:trotz|wegen|innerhalb|außerhalb|aufgrund|infolge))\s+(?<det>die|das|ein|eine|einen|mehrere|viele|einige|wenige|meine|deine|seine|ihre|unsere|Ihre|keine)\s+", RegexOptions.Compiled);
    // The preposition governs both halves of "mit einem Bildschirm und eine Lüftung".
    private static readonly Regex CoordinatedDativeRe = new($@"\b(?<prep>(?i:mit|von|bei|aus|zu|nach))\s+(?:dem|einem|der|einer|meinem|meiner|deinem|seinem|seiner|ihrem|ihrer|Ihrem|Ihrer|unserem|unserer)\s+(?:{Adjective}\s+)?\p{{Lu}}\p{{L}}+\s+(?:und|oder)\s+(?<det2>die|das|ein|eine|einen|meine|deine|seine|ihre|Ihre|unsere|keine)\s+(?:(?<adj2>{Adjective})\s+)?(?<noun2>\p{{Lu}}\p{{L}}+)(?=\s*[.,!?;:]|\s*$|\s+(?:und|oder)\b)", RegexOptions.Compiled);
    // Modal + infinitive + object: the infinitive closes the clause ("Ich möchte die Nebenkosten wissen").
    private static readonly Regex ModalBracketRe = new($@"\b(?<mod>(?i:möchte|möchten|möchtest|will|willst|wollen|kann|kannst|können|muss|musst|müssen|soll|sollen|darf|dürfen|würde|würden))\s+(?:(?:gern|gerne|auch|bitte|noch|jetzt|ich|du|er|sie|wir|man)\s+)?(?<inf>wissen|haben|sehen|machen|kaufen|bekommen|besuchen|nehmen|buchen|bestellen|bezahlen|zahlen|lernen|finden|sprechen|fragen|kennen|lesen|schreiben|mieten|kündigen|besichtigen|bringen|zurückgeben|reparieren|umtauschen|erhalten|treffen)\s+(?<obj>(?i:{Determiners}))\s+", RegexOptions.Compiled);
    // Pronoun + auxiliary + participle + object or prepositional phrase: the participle closes the clause.
    private static readonly HashSet<string> BracketParticiples = new(StringComparer.OrdinalIgnoreCase)
    {
        "angerufen", "bestellt", "bezahlt", "geschickt", "erhalten", "versucht", "reserviert", "gebucht", "gewartet", "gekündigt",
    };
    private static readonly Regex PerfectBracketRe = new($@"\b(?<subj>(?i:ich|du|er|sie|es|wir|ihr|man))\s+(?<aux>bin|bist|ist|sind|seid|habe|hast|hat|haben|habt)\s+(?<part>ge\p{{L}}+|\p{{L}}+(?:t|en))\s+(?<next>(?i:{Determiners}|nach|mit|in|im|am|an|auf|bei|vor|seit|zum|zur|von|vom|aus))\s+", RegexOptions.Compiled);
    // Indirect question after a verb of knowing or asking: the verb goes to the end ("wissen, was der Inhalt ist").
    private static readonly Regex IndirectQuestionRe = new(@"\b(?<lead>wissen|fragen|frage mich|erfahren|mitteilen|sagen|erklären|weiß nicht|verstehe nicht)\s*,\s*(?<w>(?i:was|wer|wie viel|wie lange|wie oft|wie|wo|wann|warum|wieso|weshalb|wohin|woher|welche[rsmn]?))\s+(?<verb>ist|sind|kostet|kosten|gibt|hat|haben|kann|können|muss|müssen|soll|sollen|darf|dauert|beginnt|beginnen|fängt|findet|liegt|wird|werden)\s+(?=\p{L})", RegexOptions.Compiled);
    // "über das, weil ..." → "darüber": a preposition with "das"/"es" closing the clause points back with da(r)-.
    private static readonly Regex PronominalAdverbRe = new(@"\b(?<prep>(?i:über|auf|an|für|mit|von|um|nach|vor|zu|aus|bei|gegen))\s+(?<pro>das|es)(?=\s*[.!?;:]|\s*,(?!\s*(?:was|wie|wo\p{L}*|der|die|das)\b))", RegexOptions.Compiled);

    /// <param name="spoken">A speech transcript has no punctuation or capitalisation of its own - spelling, comma and greeting rules are skipped.</param>
    /// <param name="formal">Overrides what the task's wording says: a telc letter is formal even when the exercise carries no such word.</param>
    public static FreeTextReport Analyze(string text, Exercise task, Lexicon lexicon, bool spoken = false, bool? formal = null)
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
        var isFormal = formal ?? IsFormalTask(task);

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
            var rest = text[(m.Index + m.Length)..].Split(['.', ',', '!', '?', ';', '\n'], 2)[0].Trim();
            if (rest.Length == 0) continue; // "…, wenn ich kann." is fine
            // "weil wir arbeiten und lernen": the verb is last in its own clause, a second one follows.
            if (Regex.IsMatch(rest, @"^(?:und|oder|aber|sondern|denn|bzw)\b", RegexOptions.IgnoreCase)) continue;
            // "dass ich kommen kann", "wenn Sie nicht kommen können": the verb cluster closes the clause correctly.
            if (IsVerbCluster(rest)) continue;
            var adv = m.Groups["adv"].Success ? m.Groups["adv"].Value + " " : "";
            Add("WORTST_NEBENSATZ", $"{sub} {subj} {adv}{verb} {Shorten(rest)}", $"{sub} {subj} {adv}{Shorten(rest)} {verb}", $"Nach „{sub}“ steht das konjugierte Verb am Ende des Nebensatzes.");
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
            // The word before belongs to the conjunction ("ohne dass", "und ob", "erst wenn", "einfach weil"); the
            // comma goes in front of both, so it is not missing here.
            if (CommaJoinsBefore.Contains(before)) continue;
            // A line break is a heading or a list, not a missing comma.
            if (m.Value.Contains('\n')) continue;
            // "während der Kontrolle", "seit dem Umzug": the preposition, followed by its article.
            if (sub.Equals("während", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(text[(m.Index + m.Length)..], @"^(?:der|die|das|des|dem|den|ein|eine|einer|eines)\b", RegexOptions.IgnoreCase)) continue;
            Add("KOMMA", $"{before} {sub}", $"{before}, {sub}", $"Vor „{sub}“ beginnt ein Nebensatz – davor steht immer ein Komma.");
        }

        // --- case after prepositions, n-declension, articles (gender from the lexicon) -------------------
        foreach (Match m in DativePrepRe.Matches(text))
        {
            var prep = m.Groups["prep"].Value; var art = m.Groups["art"].Value; var noun = m.Groups["noun"].Value;
            // "seit" also starts a clause ("seit die Sauna geschlossen ist"), and "nach" follows its noun in
            // "meiner Meinung nach die Lösung" - neither is a preposition in front of this article.
            if (prep == "seit" && art != "einen" || IsPostposition(text, m.Index, prep)) continue;
            if (!lexicon.TryGetGender(noun, out var gender, out var pluralSame) || pluralSame && art is "die" or "meine" or "deine" or "seine" or "ihre" or "unsere") continue;
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

        if (!spoken) AgreementAndBracketRules(text, lexicon, Add);

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
        if (isFormal)
        {
            foreach (var form in InformalRe.Matches(text).Select(m => m.Value.Trim()).DistinctBy(v => v.ToLowerInvariant()).Take(3))
                Add("REG_FORMELL", form, "Sie / Ihnen / Ihr …", "Die Aufgabe ist formell: durchgehend „Sie“ – „du“, „ihr“ und „euch“ gehören in private Nachrichten.");
            if (!spoken && InformalOpeningRe.Match(text) is { Success: true } open)
                Add("REG_ANREDE_GRUSS", open.Value.Trim(), "Sehr geehrte Damen und Herren,", "Eine formelle Nachricht beginnt mit „Sehr geehrte Damen und Herren,“ oder „Sehr geehrte Frau …,“ – „Hallo“ ist privat.");
            if (!spoken && InformalClosingRe.Match(text) is { Success: true } close)
                Add("REG_ANREDE_GRUSS", close.Value.Trim(), "Mit freundlichen Grüßen", "Formeller Schluss: „Mit freundlichen Grüßen“ und der volle Name – Abkürzungen wie „LG“ sind privat.");
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

    /// <summary>
    /// Gender agreement, case after und, genitive prepositions, the dative plural, pronominal adverbs and the verb
    /// bracket. Written text only: the agreement rules need capitalised nouns, and a spoken "ich bin gekommen mit
    /// dem Zug" is everyday German that only writing marks.
    /// </summary>
    private static void AgreementAndBracketRules(string text, Lexicon lexicon, Action<string, string, string, string> add)
    {
        foreach (Match m in DeterminerNounRe.Matches(text))
        {
            var det = m.Groups["det"].Value; var noun = m.Groups["noun"].Value;
            var lower = det.ToLowerInvariant();
            // After a comma der/die/das/den/dem are usually relative pronouns: "das Hotel, das Frau Meier empfahl".
            if (lower is "der" or "die" or "das" or "den" or "dem" && PrecededByComma(text, m.Index)) continue;
            // "Ist das Absicht?", "War das Zufall?": a pronoun with a bare predicate noun, not an article.
            if (lower == "das" && Regex.IsMatch(text[..m.Index], @"\b(?:ist|war|wäre|sind|waren)\s*$", RegexOptions.IgnoreCase)) continue;
            if (!lexicon.TryGetGender(noun, out var gender, out var pluralSame)) continue;
            var form = DeterminerForm.Of(lower);
            if (form.Agrees(gender) || pluralSame && form.FitsPlural(lower)) continue;
            // A preposition in front decides the case: "in Ihre Hotel" → "in Ihrem Hotel" (wo?) or "in Ihr Hotel" (wohin?).
            var prev = Regex.Match(text[..m.Index], @"(\p{L}+)\s*$").Groups[1].Value.ToLowerInvariant();
            string? right = prev switch
            {
                "mit" or "nach" or "aus" or "bei" or "von" or "zu" or "seit" or "gegenüber" => form.For(gender, "dat"),
                "für" or "ohne" or "gegen" or "um" or "durch" => form.For(gender, "akk"),
                "in" or "an" or "auf" or "über" or "unter" or "vor" or "hinter" or "neben" or "zwischen" => null,
                _ => form.For(gender, form.GuessCase()),
            };
            var adj = m.Groups["adj"].Success ? " …" : "";
            add("ART_GENUS", $"{det} {(adj.Length > 0 ? m.Value[(det.Length + 1)..m.Value.LastIndexOf(noun, StringComparison.Ordinal)].Trim() + " " : "")}{noun}",
                right is null
                    ? $"{MatchCase(form.For(gender, "dat"), det)}{adj} {noun} (wo?) / {MatchCase(form.For(gender, "akk"), det)}{adj} {noun} (wohin?)"
                    : $"{MatchCase(right, det)}{adj} {noun}",
                $"„{noun}“ ist {GenderWord(gender)} ({gender} {noun}) – das Genus gehört zur Vokabel und stimmt oft nicht mit dem serbischen überein.");
        }

        foreach (Match m in AccusativeObjectRe.Matches(text))
        {
            var det = m.Groups["det"].Value; var noun = m.Groups["noun"].Value;
            if (!lexicon.TryGetGender(noun, out var gender, out _) || gender != "der") continue;
            var adj = m.Groups["adj"].Success ? m.Groups["adj"].Value : null;
            add("KASUS_OBJEKT", $"{det} {(adj is null ? "" : adj + " ")}{noun}", $"{det}en {(adj is null ? "" : AccusativeAdjective(adj) + " ")}{noun}",
                $"„{noun}“ ist maskulin und hier das Akkusativobjekt von „{m.Groups["verb"].Value}“: {det}en {noun}. Nur Maskulinum zeigt den Akkusativ am Artikel.");
        }

        foreach (Match m in DativePluralRe.Matches(text))
        {
            var prep = m.Groups["prep"].Value; var noun = m.Groups["noun"].Value;
            if (IsPostposition(text, m.Index, prep)) continue;
            // Only known plurals ("Monate", "Tage"), not units that never change ("16 Ampere"); and "bis zu neun
            // Monate" is an amount, not a dative.
            if (!lexicon.IsPlural(noun) || prep.Equals("zu", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(text[..m.Index], @"\bbis\s*$", RegexOptions.IgnoreCase)) continue;
            add("KASUS_PRAEP", $"{prep} {m.Groups["num"].Value} {noun}", $"{prep} {m.Groups["num"].Value} {noun}n",
                $"„{prep}“ verlangt den Dativ, und der Dativ Plural endet auf -n: {noun} → {noun}n.");
        }

        foreach (Match m in GenitivePrepRe.Matches(text))
        {
            var prep = m.Groups["prep"].Value; var det = m.Groups["det"].Value;
            var right = det switch
            {
                "die" => "der",
                "das" => "des",
                "ein" => "eines",
                "eine" => "einer",
                "einen" => "eines",
                "mehrere" => "mehrerer",
                "viele" => "vieler",
                "einige" => "einiger",
                "wenige" => "weniger",
                _ => det + "r", // meine → meiner, keine → keiner
            };
            add("GENITIV", $"{prep} {det}", $"{prep} {right}", $"„{prep}“ verlangt den Genitiv: {prep} {right} … (im Plural „der“/„-er“, Singular maskulin/neutral „des“/„-es“).");
        }

        foreach (Match m in CoordinatedDativeRe.Matches(text))
        {
            var prep = m.Groups["prep"].Value; var det = m.Groups["det2"].Value; var noun = m.Groups["noun2"].Value;
            if (IsPostposition(text, m.Index, prep) || !lexicon.TryGetGender(noun, out var gender, out var pluralSame) || pluralSame) continue;
            var right = DeterminerForm.Dative(det, gender);
            if (right is null) continue;
            var adj = m.Groups["adj2"].Success ? m.Groups["adj2"].Value + " " : "";
            add("KASUS_PRAEP", $"und {det} {adj}{noun}", $"und {right} {(adj.Length > 0 ? DativeAdjective(adj.Trim()) + " " : "")}{noun}",
                $"„{prep}“ gilt auch nach „und“: beide Teile stehen im Dativ.");
        }

        foreach (Match m in ModalBracketRe.Matches(text))
        {
            var mod = m.Groups["mod"].Value; var inf = m.Groups["inf"].Value; var obj = m.Groups["obj"].Value;
            add("WORTST_SATZKLAMMER", $"{mod} {inf} {obj}", $"{mod} {obj} … {inf}", $"Das Modalverb steht auf Position 2, der Infinitiv „{inf}“ am Ende: die Satzklammer umschließt das Objekt.");
        }

        foreach (Match m in PerfectBracketRe.Matches(text))
        {
            var aux = m.Groups["aux"].Value; var part = m.Groups["part"].Value;
            var isSein = aux is "bin" or "bist" or "ist" or "sind" or "seid";
            var known = isSein ? SeinParticiples.Contains(part) : HabenParticiples.Contains(part) || BracketParticiples.Contains(part);
            if (!known) continue;
            var subj = m.Groups["subj"].Value; var next = m.Groups["next"].Value;
            add("WORTST_SATZKLAMMER", $"{subj} {aux} {part} {next}", $"{subj} {aux} {next} … {part}", $"Im Perfekt steht das Partizip „{part}“ am Satzende – „{aux}“ und „{part}“ bilden die Klammer um den Rest.");
        }

        foreach (Match m in IndirectQuestionRe.Matches(text))
        {
            var w = m.Groups["w"].Value; var verb = m.Groups["verb"].Value;
            add("WORTST_NEBENSATZ", $"{m.Groups["lead"].Value}, {w} {verb}", $"{m.Groups["lead"].Value}, {w} … {verb}", $"Eine indirekte Frage ist ein Nebensatz: nach „{w}“ steht das Verb am Ende – „…, {w} … {verb}“.");
        }

        foreach (Match m in PronominalAdverbRe.Matches(text))
        {
            var prep = m.Groups["prep"].Value;
            var lowerPrep = prep.ToLowerInvariant();
            var da = "da" + ("aeiouäöü".Contains(lowerPrep[0]) ? "r" : "") + lowerPrep;
            add("PRAEPADVERB", $"{prep} {m.Groups["pro"].Value}", da, $"Auf eine Sache zurückverweisen: „{da}“ statt „{prep} {m.Groups["pro"].Value}“.");
        }
    }

    private static readonly HashSet<string> CommaJoinsBefore = new(StringComparer.OrdinalIgnoreCase)
    {
        "und", "oder", "sondern", "aber", "also", "ohne", "als", "einfach", "nur", "gerade", "erst", "besonders", "allem",
        "Beispiel", "selbst", "sogar", "außer", "anstatt", "statt", "je", "bzw", "nämlich", "insbesondere", "kurz", "lange",
    };

    private static readonly HashSet<string> FiniteSet = new(Finite.Split('|').Concat(["lassen", "worden", "sein", "wird", "werden", "wurde", "wurden"]), StringComparer.OrdinalIgnoreCase);

    private static bool IsVerbCluster(string rest)
    {
        var words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 3 || words.Any(w => char.IsUpper(w[0]))) return false;
        if (words.All(FiniteSet.Contains)) return true;
        return words.Length == 1 && (words[0].StartsWith("ge", StringComparison.Ordinal) || words[0].EndsWith("en", StringComparison.Ordinal));
    }

    private static bool PrecededByComma(string text, int index)
    {
        for (var i = index - 1; i >= 0; i--)
            if (!char.IsWhiteSpace(text[i])) return text[i] == ',';
        return false;
    }

    /// <summary>"nach" (and "gegenüber") after a noun is a postposition: "meiner Meinung nach die Lösung".</summary>
    private static bool IsPostposition(string text, int index, string prep)
    {
        if (!prep.Equals("nach", StringComparison.OrdinalIgnoreCase) && !prep.Equals("gegenüber", StringComparison.OrdinalIgnoreCase)) return false;
        var before = text[..index].TrimEnd();
        var lastSpace = before.LastIndexOfAny([' ', '\n', '\t']);
        var word = before[(lastSpace + 1)..];
        if (word.Length == 0 || !char.IsUpper(word[0])) return false;
        var start = before[..(lastSpace + 1)].TrimEnd();
        return start.Length > 0 && start[^1] is not ('.' or '!' or '?' or ':' or '\n');
    }

    private static string GenderWord(string article) => article switch { "der" => "maskulin", "die" => "feminin", _ => "neutral" };
    private static string MatchCase(string word, string model) => char.IsUpper(model[0]) ? char.ToUpperInvariant(word[0]) + word[1..] : word;
    private static string AccusativeAdjective(string adj) => adj.EndsWith("er", StringComparison.Ordinal) || adj.EndsWith("es", StringComparison.Ordinal) ? adj[..^2] + "en" : adj.EndsWith('e') ? adj + "n" : adj;
    private static string DativeAdjective(string adj) => adj.EndsWith("en", StringComparison.Ordinal) ? adj : adj.EndsWith('e') ? adj + "n" : adj.EndsWith("er", StringComparison.Ordinal) || adj.EndsWith("es", StringComparison.Ordinal) ? adj[..^2] + "en" : adj;

    /// <summary>The ending class of a determiner, which genders it can stand with in the singular, and the form a gender needs instead.</summary>
    private readonly record struct DeterminerForm(string Stem, string Ending)
    {
        public static DeterminerForm Of(string det) => det switch
        {
            "der" or "die" or "das" or "den" or "dem" or "des" => new("d", det[1..]),
            "ein" or "kein" or "mein" or "dein" or "unser" => new(det, ""),
            "dieser" or "jeder" => new(det[..^2], "er-dies"),
            _ when det.StartsWith("unser", StringComparison.Ordinal) => new("unser", det[5..]),
            _ => new(det[..(det.Length - EndingLength(det))], det[(det.Length - EndingLength(det))..]),
        };

        private static int EndingLength(string det) => det.EndsWith('e') ? 1 : 2;

        public bool Agrees(string gender) => (Stem, Ending, gender) switch
        {
            ("d", "er", "der" or "die") => true,
            ("d", "ie", "die") => true,
            ("d", "as", "das") => true,
            ("d", "en", "der") => true,
            ("d", "em" or "es", "der" or "das") => true,
            (_, "", "der" or "das") => true,
            (_, "er-dies", "der" or "die") => true,
            (not "d", "e", "die") => true,
            (not "d", "en", "der") => true,
            (not "d", "em" or "es", "der" or "das") => true,
            (not "d", "er", "die") => true,
            _ => false,
        };

        /// <summary>Forms that also stand with a plural noun, for nouns spelled the same in the plural ("die Zimmer").</summary>
        public bool FitsPlural(string det) => !det.StartsWith("ein", StringComparison.Ordinal) && !det.StartsWith("jed", StringComparison.Ordinal)
            && (Ending is "er" or "ie" or "en" or "e" or "er-dies");

        /// <summary>The case the learner probably meant, read off the ending they wrote.</summary>
        public string GuessCase() => (Stem, Ending) switch
        {
            (_, "em") => "dat",
            ("d", "er") => "nom",
            (_, "er") => "dat",
            (_, "es") => "gen",
            (_, "en") => "akk",
            _ => "nom",
        };

        /// <summary>This determiner in <paramref name="case"/> for the noun's real gender.</summary>
        public string For(string gender, string @case)
        {
            if (Stem == "d")
                return (@case, gender) switch
                {
                    ("dat", "die") => "der",
                    ("dat", _) => "dem",
                    ("gen", "die") => "der",
                    ("gen", _) => "des",
                    ("akk", "der") => "den",
                    (_, "der") => "der",
                    (_, "die") => "die",
                    _ => "das",
                };
            var dies = Ending == "er-dies" || Stem is "dies" or "jed";
            return (@case, gender) switch
            {
                ("dat", "die") => Stem + "er",
                ("dat", _) => Stem + "em",
                ("gen", "die") => Stem + "er",
                ("gen", _) => Stem + "es",
                ("akk", "der") => Stem + "en",
                (_, "der") => dies ? Stem + "er" : Stem,
                (_, "die") => Stem + "e",
                _ => dies ? Stem + "es" : Stem,
            };
        }

        /// <summary>The dative of a nominative/accusative determiner, or null when it already is one.</summary>
        public static string? Dative(string det, string gender)
        {
            var f = Of(det.ToLowerInvariant());
            if (f.Ending is "em" or "er") return null;
            if (f.Stem == "d") return gender == "die" ? "der" : "dem";
            return f.Stem + (gender == "die" ? "er" : "em");
        }
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
