using Lotse.Core.Model;

namespace Lotse.Core.Tests;

/// <summary>
/// The helper language is the one thing in the app that is NOT German. These tests pin the two rules that make it
/// safe: the second column of a grammar table never goes blank, and a contrastive note never appears in a language
/// the learner did not choose.
/// </summary>
public class HelperLanguageTests
{
    [Fact]
    public void Example_row_uses_english_when_the_learner_chose_it()
        => Assert.Equal("I'll call you tomorrow.",
            new ExampleRow("Ich rufe dich morgen an.", "Zvaću te sutra.", null, "I'll call you tomorrow.").Helper(HelperLanguage.English));

    [Fact]
    public void Example_row_falls_back_rather_than_leaving_the_column_empty()
        => Assert.Equal("Zvaću te sutra.",
            new ExampleRow("Ich rufe dich morgen an.", "Zvaću te sutra.").Helper(HelperLanguage.English));

    [Fact]
    public void Example_row_stays_serbian_for_a_serbian_learner()
        => Assert.Equal("Zvaću te sutra.",
            new ExampleRow("Ich rufe dich morgen an.", "Zvaću te sutra.", null, "I'll call you tomorrow.").Helper(HelperLanguage.Serbian));

    private static Exercise Vocab() => new()
    {
        Id = "v1",
        Type = ExerciseType.Vocab,
        NodeId = "WS.ARBEIT_KARRIERE",
        Band = CefrBand.B2_1,
        Prompt = "propratno pismo",
        PromptEn = "cover letter",
        Answers = ["das Anschreiben"],
        SerbianNote = "srpska napomena",
        EnglishNote = "english note",
    };

    [Fact]
    public void English_learner_is_asked_in_english()
        => Assert.Equal("cover letter", NameTemplate.Render(Vocab(), new LearnerView("", "", HelperLanguage.English)).Prompt);

    [Fact]
    public void Serbian_learner_is_asked_in_serbian()
        => Assert.Equal("propratno pismo", NameTemplate.Render(Vocab(), new LearnerView("", "", HelperLanguage.Serbian)).Prompt);

    [Fact]
    public void A_vocab_card_without_an_english_prompt_still_has_a_question()
    {
        var withoutEnglish = Vocab() with { PromptEn = null };
        Assert.Equal("propratno pismo", NameTemplate.Render(withoutEnglish, new LearnerView("", "", HelperLanguage.English)).Prompt);
    }

    [Theory]
    [InlineData(HelperLanguage.Serbian, "srpska napomena")]
    [InlineData(HelperLanguage.English, "english note")]
    public void The_note_follows_the_chosen_language(HelperLanguage lang, string expected)
        => Assert.Equal(expected, Vocab().NoteFor(lang));

    [Fact]
    public void A_missing_note_stays_missing_instead_of_switching_language()
    {
        // Showing a Serbian note to someone who chose English is worse than showing none - the note is an extra.
        var onlySerbian = Vocab() with { EnglishNote = null };
        Assert.Null(onlySerbian.NoteFor(HelperLanguage.English));
        Assert.Equal("srpska napomena", onlySerbian.NoteFor(HelperLanguage.Serbian));
    }

    [Fact]
    public void Node_note_follows_the_chosen_language()
    {
        var node = new SkillNode("GR.X", SkillArea.Grammatik, "T", "D", CefrBand.B2_1, true, "srpski", [], 1.0, null, "english");
        Assert.Equal("srpski", node.InterferenceNoteFor(HelperLanguage.Serbian));
        Assert.Equal("english", node.InterferenceNoteFor(HelperLanguage.English));
    }

    [Fact]
    public void The_default_view_is_the_authors_own()
        => Assert.Equal(HelperLanguage.Serbian, LearnerView.Default.HelperLanguage);
}

/// <summary>
/// Content-side guards. English is only worth offering if it is complete, so a Serbian string that never got an
/// English counterpart has to fail the build rather than surface as a Serbian sentence in an English session.
/// </summary>
public class HelperLanguageContentTests(CatalogFixture fx) : IClassFixture<CatalogFixture>
{
    [Fact]
    public void Every_node_with_a_serbian_interference_note_has_an_english_one()
    {
        var missing = fx.Catalog.Nodes.Where(n => n.InterferenceNote is not null && n.InterferenceNoteEn is null).Select(n => n.Id).ToList();
        Assert.True(missing.Count == 0, "Knoten ohne englische Interferenznotiz: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_exercise_with_a_serbian_note_has_an_english_one()
    {
        var missing = fx.Catalog.Exercises.Where(e => e.SerbianNote is not null && e.EnglishNote is null).Select(e => e.Id).ToList();
        Assert.True(missing.Count == 0, "Übungen ohne englische Notiz: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_vocab_and_translation_item_can_be_asked_in_english()
    {
        // For these two types the prompt IS the helper language: without an English one the card has no question,
        // and NameTemplate would quietly fall back to Serbian in an English session.
        var missing = fx.Catalog.Exercises
            .Where(e => e.Type is ExerciseType.Vocab or ExerciseType.Translate && string.IsNullOrWhiteSpace(e.PromptEn))
            .Select(e => e.Id).ToList();
        Assert.True(missing.Count == 0, $"{missing.Count} Vokabel-/Übersetzungsaufgaben ohne promptEn: " + string.Join(", ", missing.Take(20)));
    }

    [Fact]
    public void No_other_exercise_type_carries_an_english_prompt()
    {
        // A German prompt with an English twin would mean the item was mis-tagged - the swap only applies to the
        // two types whose prompt is the bridge language.
        var stray = fx.Catalog.Exercises
            .Where(e => e.Type is not (ExerciseType.Vocab or ExerciseType.Translate) && e.PromptEn is not null)
            .Select(e => e.Id).ToList();
        Assert.True(stray.Count == 0, "promptEn an einem Aufgabentyp mit deutschem Prompt: " + string.Join(", ", stray));
    }

    [Fact]
    public void Every_explanation_that_talks_about_serbian_has_an_english_counterpart()
    {
        // An explanation that names the learner's first language is only right for that learner. The ones that
        // explain German without any contrast are fine as they are and need no second version.
        var missing = fx.Catalog.Lessons
            .Where(l => (l.Grammar.Text.Contains("erbisch", StringComparison.Ordinal) || l.Grammar.Text.Contains("erbische", StringComparison.Ordinal))
                        && string.IsNullOrWhiteSpace(l.Grammar.TextEn))
            .Select(l => l.Id).ToList();
        Assert.True(missing.Count == 0, "Erklärtexte mit Serbisch-Bezug ohne englische Fassung: " + string.Join(", ", missing));
    }

    [Fact]
    public void No_english_explanation_still_talks_about_serbian()
    {
        var leftovers = fx.Catalog.Lessons
            .Where(l => l.Grammar.TextEn is { } t && t.Contains("erbisch", StringComparison.Ordinal))
            .Select(l => l.Id).ToList();
        Assert.True(leftovers.Count == 0, "Englische Erklärtexte, die noch über Serbisch sprechen: " + string.Join(", ", leftovers));
    }

    [Fact]
    public void Every_grammar_example_has_both_columns()
    {
        var missing = fx.Catalog.Lessons
            .SelectMany(l => l.Grammar.Examples.Select(e => (l.Id, e)))
            .Where(t => string.IsNullOrWhiteSpace(t.e.English))
            .Select(t => t.Id + ": " + t.e.German).ToList();
        Assert.True(missing.Count == 0, "Beispielzeilen ohne Englisch: " + string.Join(" | ", missing));
    }
}
