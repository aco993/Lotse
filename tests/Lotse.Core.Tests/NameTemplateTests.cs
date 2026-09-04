using Lotse.Core.Model;

namespace Lotse.Core.Tests;

/// <summary>The content carries tokens; these are the rules by which they become a person.</summary>
public class NameTemplateTests
{
    [Theory]
    [InlineData("Sehr geehrter Herr {Nachname},", "Sehr geehrter Herr Petrović,")]
    [InlineData("Hallo {Vorname}!", "Hallo Marko!")]
    [InlineData("Mit freundlichen Grüßen, {Name}", "Mit freundlichen Grüßen, Marko Petrović")]
    [InlineData("{Vorname} {Nachname} = {Name}", "Marko Petrović = Marko Petrović")]
    [InlineData("Ohne Platzhalter", "Ohne Platzhalter")]
    public void Tokens_become_the_learners_name(string template, string expected)
        => Assert.Equal(expected, NameTemplate.Render(template, "Marko", "Petrović"));

    [Fact]
    public void Empty_fields_fall_back_to_the_authors_own_name()
        => Assert.Equal("Aleksandar Micić", NameTemplate.Render("{Name}", "", "   "));

    [Fact]
    public void One_filled_field_still_falls_back_for_the_other()
        => Assert.Equal("Marko Micić", NameTemplate.Render("{Name}", "Marko", ""));

    [Fact]
    public void Rendering_an_exercise_leaves_the_answers_alone()
    {
        var e = new Exercise
        {
            Id = "x",
            Type = ExerciseType.Cloze,
            NodeId = "GR.PASSIV",
            Band = CefrBand.B2_1,
            Prompt = "Hallo {Vorname}, ___ du das?",
            Instruction = "Für {Name}",
            Options = ["{Vorname}", "andere"],
            ModelAnswer = "Grüße, {Name}",
            Text = "Sehr geehrter Herr {Nachname}",
            Answers = ["machst"],
        };

        var rendered = NameTemplate.Render(e, "Marko", "Petrović");

        Assert.Equal("Hallo Marko, ___ du das?", rendered.Prompt);
        Assert.Equal("Für Marko Petrović", rendered.Instruction);
        Assert.Equal("Marko", rendered.Options[0]);
        Assert.Equal("Grüße, Marko Petrović", rendered.ModelAnswer);
        Assert.Equal("Sehr geehrter Herr Petrović", rendered.Text);
        // The checker compares against this verbatim; substituting here would be a bug, not a feature.
        Assert.Equal("machst", rendered.Answers[0]);
    }

    [Fact]
    public void Rendering_a_lesson_reaches_the_story_lines_and_their_options()
    {
        var lesson = new Lesson(
            "L01", 1, "Titel", "Untertitel", ExerciseContext.Beruf, CefrBand.B2_1, "team",
            "Intro für {Vorname}.",
            [new DialogueLine("Herr Krüger", "Guten Tag, Herr {Nachname}.", ["Ich bin {Name}.", "Andere"], 0, ["Gut, {Vorname}.", "Nein"])],
            new LessonExplanation("T", "GR.PASSIV", "Text", []),
            [], null, "Merksatz für {Vorname}.", 15, []);

        var r = NameTemplate.Render(lesson, "Marko", "Petrović");

        Assert.Equal("Intro für Marko.", r.Intro);
        Assert.Equal("Guten Tag, Herr Petrović.", r.Story[0].Text);
        Assert.Equal("Ich bin Marko Petrović.", r.Story[0].Options![0]);
        Assert.Equal("Gut, Marko.", r.Story[0].Feedback![0]);
        Assert.Equal("Merksatz für Marko.", r.Merksatz);
    }
}
