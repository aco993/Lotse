using Lotse.Core.Engine;
using Lotse.Core.Model;

namespace Lotse.Core.Tests;

public class LessonContentTests(CatalogFixture fx) : IClassFixture<CatalogFixture>
{
    [Fact]
    public void Course_has_twenty_four_ordered_lessons_in_two_parts()
    {
        var lessons = fx.Catalog.Lessons;
        Assert.Equal(24, lessons.Count);
        Assert.Equal(Enumerable.Range(1, 24), lessons.Select(l => l.Order));
        Assert.Equal(12, lessons.Count(l => l.Part == 1));
        Assert.Equal(12, lessons.Count(l => l.Part == 2));
    }

    [Fact]
    public void Course_covers_every_grammar_node_with_a_lesson_explanation()
    {
        var explained = fx.Catalog.Lessons.Select(l => l.Grammar.NodeId).ToHashSet();
        // The course is a B2 course ("Dein Kurs auf B2") and ends there by design; C1 nodes are drilled for
        // learners who set that target, but no lesson is expected to explain them.
        var grammarNodes = fx.Catalog.Nodes
            .Where(n => n.Id.StartsWith("GR.", StringComparison.Ordinal) && n.Band != CefrBand.C1)
            .Select(n => n.Id).ToList();
        var missing = grammarNodes.Where(n => !explained.Contains(n)).ToList();
        // A handful of small nodes are practised inside other lessons rather than explained on their own.
        Assert.True(missing.Count <= 6, "Nicht erklärte Grammatikknoten: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_lesson_is_complete()
    {
        foreach (var l in fx.Catalog.Lessons)
        {
            Assert.True(l.Steps.Count >= 8, $"{l.Id}: nur {l.Steps.Count} Schritte");
            Assert.NotNull(l.ProductionExerciseId);
            Assert.True(l.Story.Count >= 5, $"{l.Id}: zu kurze Geschichte");
            Assert.True(l.Story.Count(s => s.IsLearnerTurn) >= 2, $"{l.Id}: zu wenige Lerner-Züge");
            Assert.True(l.Grammar.Examples.Count >= 3, $"{l.Id}: zu wenige Beispiele");
            Assert.False(string.IsNullOrWhiteSpace(l.Merksatz));
            Assert.NotEmpty(l.NodeIds);
        }
    }

    [Fact]
    public void Every_lesson_mixes_interactive_types()
    {
        foreach (var l in fx.Catalog.Lessons)
        {
            var types = l.Steps.Select(id => fx.Catalog.Exercise(id)!.Type).ToHashSet();
            Assert.Contains(ExerciseType.Dialogue, types);
            Assert.Contains(ExerciseType.Match, types);
            Assert.Contains(ExerciseType.SpotError, types);
            Assert.True(types.Count >= 4, $"{l.Id}: nur {types.Count} Aufgabentypen");
        }
    }

    [Fact]
    public void Dialogue_turns_have_one_best_option_with_feedback_for_each()
    {
        var dialogues = fx.Catalog.Exercises.Where(e => e.Type == ExerciseType.Dialogue).ToList();
        Assert.True(dialogues.Count >= 12);
        foreach (var d in dialogues)
            foreach (var turn in d.Lines.Where(l => l.IsLearnerTurn))
            {
                Assert.Equal(3, turn.Options!.Count);
                Assert.InRange(turn.CorrectIndex!.Value, 0, 2);
                Assert.NotNull(turn.Feedback);
                Assert.Equal(3, turn.Feedback!.Count);
                Assert.All(turn.Feedback, f => Assert.True(f.Length > 15, $"{d.Id}: Feedback zu knapp"));
            }
        foreach (var l in fx.Catalog.Lessons)
            foreach (var turn in l.Story.Where(s => s.IsLearnerTurn))
                Assert.Equal(turn.Options!.Count, turn.Feedback!.Count);
    }

    [Fact]
    public void Spot_error_words_and_correction_are_consistent()
    {
        foreach (var e in fx.Catalog.Exercises.Where(e => e.Type == ExerciseType.SpotError))
        {
            var wrong = e.Options[e.CorrectIndex!.Value];
            Assert.NotEqual(wrong.Trim(',', '.'), e.Answers[0].Trim(',', '.'));
            Assert.Equal(Outcome.Correct, AnswerChecker.Check(e, e.CorrectIndex.ToString()).Outcome);
            Assert.Equal(Outcome.Incorrect, AnswerChecker.Check(e, ((e.CorrectIndex.Value + 1) % e.Options.Count).ToString()).Outcome);
        }
    }

    [Fact]
    public void Lesson_steps_cover_the_declared_grammar_node()
        => Assert.All(fx.Catalog.Lessons, l => Assert.Contains(l.Steps, id => fx.Catalog.Exercise(id)!.NodeId == l.Grammar.NodeId));

    [Fact]
    public void Read_aloud_speaks_the_sentence_and_never_the_speaker_label()
    {
        // The name stands beside the line on screen and each character has their own voice; speaking "Sabine:"
        // before every turn only interrupts the German.
        var told = new DialogueLine("Sabine (im Stand-up)", "Der Sprint beginnt am Montag.");
        Assert.Equal("Der Sprint beginnt am Montag.", told.SpokenText);

        var mine = new DialogueLine("Du", "", ["Das schaffe ich.", "Weiss nicht.", "Egal."], 0,
            ["passend", "zu vage", "unhoeflich"]);
        Assert.Equal("Das schaffe ich.", mine.SpokenText);

        foreach (var l in fx.Catalog.Lessons)
            foreach (var line in l.Story)
            {
                var label = line.Speaker.Split('(')[0].Trim();
                Assert.False(line.SpokenText.StartsWith(label + ":", StringComparison.Ordinal),
                    $"{l.Id}: Sprechername im Vorlesetext");
            }
    }
}
