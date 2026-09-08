using Lotse.Core.Engine;
using Lotse.Core.Model;

namespace Lotse.Core.Tests;

public class FreeTextAnalyzerTests
{
    private static readonly Exercise Formal = new()
    {
        Id = "t",
        Type = ExerciseType.FreeWrite,
        NodeId = "SC.FORMELL",
        Band = CefrBand.B2_1,
        Prompt = "Schreiben Sie eine formelle Beschwerde an die Hausverwaltung.",
        MinWords = 80,
        Rubric = ["Sie-Form", "Anrede und Grußformel"],
    };
    // No word target: the rule tests use one-liners and must not trip the length rule.
    private static readonly Exercise Casual = Formal with { Id = "c", Prompt = "Schreib deinem Freund, wie dein Wochenende war.", MinWords = null, Rubric = ["Perfekt"] };

    private static Lexicon Lex()
    {
        var vocab = new List<Exercise>
        {
            new() { Id = "v1", Type = ExerciseType.Vocab, NodeId = "WS.X", Band = CefrBand.B1_2, Prompt = "termin", Answers = ["der Termin"], Lemma = "Termin", Article = "der", Plural = "die Termine" },
            new() { Id = "v2", Type = ExerciseType.Vocab, NodeId = "WS.X", Band = CefrBand.B1_2, Prompt = "auto", Answers = ["das Auto"], Lemma = "Auto", Article = "das", Plural = "die Autos" },
            new() { Id = "v3", Type = ExerciseType.Vocab, NodeId = "WS.X", Band = CefrBand.B1_2, Prompt = "firma", Answers = ["die Firma"], Lemma = "Firma", Article = "die", Plural = "die Firmen" },
            new() { Id = "v4", Type = ExerciseType.Vocab, NodeId = "WS.X", Band = CefrBand.B1_2, Prompt = "geld", Answers = ["das Geld"], Lemma = "Geld", Article = "das", Plural = "nur Sg." },
            new() { Id = "v5", Type = ExerciseType.Vocab, NodeId = "WS.X", Band = CefrBand.B1_2, Prompt = "loesung", Answers = ["die Lösung"], Lemma = "Lösung", Article = "die", Plural = "die Lösungen" },
            // Countable by its card (die Ängste), yet "Angst haben" takes no article - the real bank has this card.
            new() { Id = "v6", Type = ExerciseType.Vocab, NodeId = "WS.X", Band = CefrBand.B1_2, Prompt = "strah", Answers = ["die Angst"], Lemma = "Angst", Article = "die", Plural = "die Ängste" },
        };
        var node = new SkillNode("WS.X", SkillArea.Wortschatz, "x", "x", CefrBand.B1_2, false, null, []);
        return Lexicon.FromCatalog(new ContentCatalog([node], [], vocab, []));
    }

    private static IReadOnlyList<string> Codes(string text, Exercise? task = null) => FreeTextAnalyzer.Analyze(text, task ?? Casual, Lex()).Findings.Select(f => f.Code).ToList();

    [Fact]
    public void Serbian_interference_in_the_perfect_is_caught_in_both_directions()
    {
        Assert.Contains("TEMPUS_HILFSVERB", Codes("Gestern habe ich nach Hause gegangen."));
        Assert.Contains("TEMPUS_HILFSVERB", Codes("Ich bin den ganzen Tag gearbeitet."));
        Assert.DoesNotContain("TEMPUS_HILFSVERB", Codes("Ich bin nach Hause gegangen und habe dann gearbeitet."));
    }

    /// <summary>
    /// A finding lowers a skill node, so a wrong finding is worse than none. These three sentences are correct
    /// German the first version marked as errors; they must stay silent.
    /// </summary>
    [Fact]
    public void Correct_german_the_rules_used_to_flag_stays_silent()
    {
        Assert.DoesNotContain("TEMPUS_HILFSVERB", Codes("Der Film hat mir gefallen."));            // gefallen takes haben
        Assert.DoesNotContain("TEMPUS_HILFSVERB", Codes("Ich habe das Auto in die Stadt gefahren.")); // transitive fahren takes haben
        Assert.DoesNotContain("TEMPUS_HILFSVERB", Codes("Die Arbeit ist beendet."));               // Zustandspassiv
        Assert.DoesNotContain("TEMPUS_HILFSVERB", Codes("Der Brief ist geschrieben und das Auto ist gekauft."));
        Assert.DoesNotContain("ART_FEHLT", Codes("Sie hat Angst vor der Prüfung."));                // bare-noun predicate
        // ...and the genuine mistakes next to them are still caught.
        Assert.Contains("TEMPUS_HILFSVERB", Codes("Ich bin gestern lange geschlafen."));
        Assert.Contains("ART_FEHLT", Codes("Ich habe Termin beim Arzt."));
    }

    [Fact]
    public void Verb_position_after_subordinator_and_after_fronted_adverbial()
    {
        var r = FreeTextAnalyzer.Analyze("Ich komme nicht, weil ich habe keine Zeit. Gestern ich habe lange gearbeitet.", Casual, Lex());
        Assert.Contains(r.Findings, f => f.Code == "WORTST_NEBENSATZ" && f.Correction.EndsWith("habe"));
        Assert.Contains(r.Findings, f => f.Code == "WORTST_V2" && f.Correction == "Gestern habe ich");
        Assert.Empty(Codes("Ich komme nicht, weil ich keine Zeit habe. Gestern habe ich lange gearbeitet."));
    }

    [Fact]
    public void Connector_adverb_followed_by_subject_is_a_connector_placement_error()
        => Assert.Contains("KONNEKTOR_STELLUNG", Codes("Ich war krank. Deshalb ich bin zu Hause geblieben."));

    [Fact]
    public void Missing_comma_before_subordinate_clause()
    {
        Assert.Contains("KOMMA", Codes("Ich glaube dass wir das schaffen."));
        Assert.DoesNotContain("KOMMA", Codes("Ich glaube, dass wir das schaffen."));
        Assert.DoesNotContain("KOMMA", Codes("Wenn ich Zeit habe, komme ich."));
    }

    [Fact]
    public void Spelling_umlaut_digraphs_and_sharp_s()
    {
        var r = FreeTextAnalyzer.Analyze("Vielen Dank fuer die Loesung, das ist eine grosse Hilfe. Ich weiss es.", Casual, Lex());
        Assert.Contains(r.Findings, f => f.Code == "ORTH_UMLAUT" && f.Snippet == "fuer" && f.Correction == "für");
        Assert.Contains(r.Findings, f => f.Code == "ORTH_UMLAUT" && f.Snippet == "Loesung" && f.Correction == "Lösung");
        Assert.Contains(r.Findings, f => f.Code == "ORTH_SZ" && f.Snippet == "grosse" && f.Correction == "große");
        Assert.Contains(r.Findings, f => f.Code == "ORTH_SZ" && f.Snippet == "weiss");
    }

    [Fact]
    public void Case_after_prepositions_uses_the_lexicon_gender()
    {
        var r = FreeTextAnalyzer.Analyze("Ich fahre mit das Auto zu die Firma, für dem Termin.", Casual, Lex());
        Assert.Contains(r.Findings, f => f.Code == "KASUS_PRAEP" && f.Correction == "mit dem Auto");
        Assert.Contains(r.Findings, f => f.Code == "KASUS_PRAEP" && f.Correction == "zu der Firma");
        Assert.Contains(r.Findings, f => f.Code == "KASUS_PRAEP" && f.Snippet == "für dem");
        Assert.Empty(Codes("Ich fahre mit dem Auto zur Firma, für den Termin."));
    }

    [Fact]
    public void Missing_article_and_lowercase_noun_only_for_known_countable_nouns()
    {
        var r = FreeTextAnalyzer.Analyze("Ich habe Termin morgen und ich brauche Geld. Das ist die lösung.", Casual, Lex());
        Assert.Contains(r.Findings, f => f.Code == "ART_FEHLT" && f.Correction == "habe einen Termin");
        Assert.DoesNotContain(r.Findings, f => f.Code == "ART_FEHLT" && f.Snippet.Contains("Geld")); // uncountable
        Assert.Contains(r.Findings, f => f.Code == "ORTH_GROSSSCHREIBUNG" && f.Correction == "die Lösung");
    }

    [Fact]
    public void N_declension_and_verb_preposition()
    {
        var r = FreeTextAnalyzer.Analyze("Ich habe mit dem Kollege gesprochen und warte für seine Antwort. Ich interessiere mich über das Projekt.", Casual, Lex());
        Assert.Contains(r.Findings, f => f.Code == "N_DEKLINATION" && f.Correction == "dem Kollegen");
        Assert.Contains(r.Findings, f => f.Code == "VERB_PRAEP" && f.Snippet.StartsWith("warte"));
        Assert.Contains(r.Findings, f => f.Code == "VERB_PRAEP" && f.Snippet.StartsWith("interessiere"));
    }

    [Fact]
    public void Formal_task_flags_register_and_the_greeting_formula()
    {
        var text = "Hallo, ich schreibe dir wegen dem Lärm. Mit freundliche Grüße, Marko";
        var r = FreeTextAnalyzer.Analyze(text, Formal, Lex());
        Assert.Contains(r.Findings, f => f.Code == "REG_FORMELL");
        Assert.Contains(r.Findings, f => f.Code == "REG_ANREDE_GRUSS" && f.Correction == "Mit freundlichen Grüßen");
        Assert.Contains(r.Findings, f => f.Code == "TXT_LAENGE");
        Assert.DoesNotContain("REG_FORMELL", Codes("Hallo Jonas, kommst du am Samstag? Ich freue mich auf dich.", Casual));
    }

    [Fact]
    public void A_clean_b2_text_produces_no_findings()
    {
        var text = "Sehr geehrte Damen und Herren, ich wende mich an Sie, weil die Heizung in meiner Wohnung seit zwei Wochen nicht funktioniert. "
                 + "Obwohl ich das Problem bereits telefonisch gemeldet habe, ist bisher nichts passiert. Deshalb bitte ich Sie, den Schaden bis Ende der Woche beheben zu lassen. "
                 + "Außerdem möchte ich wissen, ob ich die Miete für diesen Zeitraum mindern kann. Für eine kurze Rückmeldung wäre ich Ihnen dankbar. "
                 + "Mit freundlichen Grüßen Ana Petrović";
        var r = FreeTextAnalyzer.Analyze(text, Formal, Lex());
        Assert.Empty(r.Findings);
        Assert.True(r.ConnectorCount >= 3);
    }

    [Fact]
    public void False_friends_are_hints_not_errors()
    {
        var r = FreeTextAnalyzer.Analyze("Ich habe mich auf den Konkurs beworben.", Casual, Lex());
        Assert.DoesNotContain(r.Findings, f => f.Code == "WS_FALSCHER_FREUND");
        Assert.Contains(r.Hints, h => h.Contains("Konkurs"));
    }
}
