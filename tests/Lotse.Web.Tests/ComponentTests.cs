using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Infrastructure.Services;
using Lotse.Web.Components.Exercises;
using Lotse.Web.Components.Shared;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Lotse.Web.Tests;

/// <summary>Shared bUnit setup: MudBlazor services, loose JS interop (no browser), a fake application service.</summary>
public abstract class LotseComponentTest : BunitContext
{
    protected readonly FakeLearningService Learning = new();

    protected LotseComponentTest()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./js/speech.js").Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        // No Piper in the test host, same as on a machine where install-piper.ps1 was never run: SpeechService
        // then goes straight to the browser voices, which loose JS interop answers.
        Services.AddSingleton<ITextToSpeech>(new NoTextToSpeech());
        Services.AddScoped<SpeechService>();
        Services.AddSingleton<ILearningService>(Learning);
    }

    protected static Exercise Cloze(string id = "c1", params string[] answers) => new()
    {
        Id = id,
        Type = ExerciseType.Cloze,
        NodeId = "GR.KASUS_PRAEPOSITIONEN",
        Band = CefrBand.B1_2,
        Prompt = "Ich fahre mit ___ Bus.",
        Instruction = "Artikel",
        Answers = answers.Length == 0 ? ["dem"] : answers,
        Explanation = "mit + Dativ",
        SerbianNote = "sa + instrumental → mit + Dativ",
    };

    protected static Exercise Choice(string id = "m1") => new()
    {
        Id = id,
        Type = ExerciseType.MultipleChoice,
        NodeId = "GR.ARTIKEL_GENUS",
        Band = CefrBand.B1_1,
        Prompt = "___ Problem",
        Options = ["Der", "Die", "Das"],
        CorrectIndex = 2,
        Explanation = "das Problem",
    };
}

public class MasteryBarTests : LotseComponentTest
{
    [Fact]
    public void Width_follows_value_and_opacity_follows_confidence()
    {
        var cut = Render<MasteryBar>(p => p.Add(x => x.Value, 0.75).Add(x => x.Confidence, 0.5));
        var bar = cut.Find(".heat > span");
        Assert.Contains("width:75%", bar.GetAttribute("style"));
        Assert.Contains("opacity:0.68", bar.GetAttribute("style")); // 0.35 + 0.65 * 0.5
        Assert.Contains("75 %", cut.Find(".heat").GetAttribute("title"));
    }

    [Fact]
    public void Colour_encodes_the_pass_threshold()
    {
        var weak = Render<MasteryBar>(p => p.Add(x => x.Value, 0.2)).Find(".heat > span").GetAttribute("style");
        var strong = Render<MasteryBar>(p => p.Add(x => x.Value, 0.9)).Find(".heat > span").GetAttribute("style");
        Assert.Contains("error", weak);
        Assert.Contains("tertiary", strong);
    }
}

public class FeedbackPanelTests : LotseComponentTest
{
    [Fact]
    public void Wrong_answer_shows_solution_explanation_and_serbian_note()
    {
        var ex = Cloze();
        var result = new AnswerResult(AnswerChecker.Check(ex, "den"), ex, null, 0.3, ex.SerbianNote, false);
        var cut = Render<FeedbackPanel>(p => p.Add(x => x.Result, result));
        Assert.Contains("Leider nicht", cut.Markup);
        Assert.Contains("Lösung:", cut.Markup);
        Assert.Contains("dem", cut.Markup);
        Assert.Contains("mit + Dativ", cut.Markup);
        Assert.Contains("Serbisch ↔ Deutsch", cut.Markup);
        Assert.Contains("ex-feedback-wrong", cut.Markup);
    }

    [Fact]
    public void Correct_answer_has_no_solution_line_and_no_serbian_note()
    {
        var ex = Cloze();
        var result = new AnswerResult(AnswerChecker.Check(ex, "dem"), ex, null, 0.6, null, false);
        var cut = Render<FeedbackPanel>(p => p.Add(x => x.Result, result));
        Assert.Contains("Richtig", cut.Markup);
        Assert.DoesNotContain("Lösung:", cut.Markup);
        Assert.DoesNotContain("Serbisch ↔ Deutsch", cut.Markup);
    }
}

public class ClosedExerciseTests : LotseComponentTest
{
    [Fact]
    public void Multiple_choice_renders_numbered_options_and_submits_the_chosen_index()
    {
        var ex = Choice();
        Learning.Exercises[ex.Id] = ex;
        var cut = Render<ClosedExercise>(p => p.Add(x => x.Exercise, ex));

        var options = cut.FindAll("button").Where(b => b.TextContent.Contains("Das") || b.TextContent.Contains("Der") || b.TextContent.Contains("Die")).ToList();
        Assert.Equal(3, options.Count);
        Assert.StartsWith("1", options[0].TextContent.Trim());

        options[2].Click();
        cut.FindAll("button").First(b => b.TextContent.Contains("Prüfen")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Richtig", cut.Markup));
        Assert.Single(Learning.Answers);
        Assert.Equal("2", Learning.Answers[0].Answer);
        Assert.Contains("Weiter", cut.Markup);
    }

    [Fact]
    public void Check_button_is_disabled_until_something_is_typed()
    {
        var ex = Cloze();
        Learning.Exercises[ex.Id] = ex;
        var cut = Render<ClosedExercise>(p => p.Add(x => x.Exercise, ex));
        var check = cut.FindAll("button").First(b => b.TextContent.Contains("Prüfen"));
        Assert.True(check.HasAttribute("disabled"));
    }

    [Fact]
    public void Dont_know_submits_an_empty_answer_and_shows_the_solution()
    {
        var ex = Cloze();
        Learning.Exercises[ex.Id] = ex;
        var cut = Render<ClosedExercise>(p => p.Add(x => x.Exercise, ex));
        cut.FindAll("button").First(b => b.TextContent.Contains("Weiß ich nicht")).Click();
        cut.WaitForAssertion(() => Assert.Contains("Lösung:", cut.Markup));
        Assert.Equal("", Learning.Answers[0].Answer);
    }

    [Fact]
    public void Weiter_raises_OnDone_with_the_result()
    {
        var ex = Choice();
        Learning.Exercises[ex.Id] = ex;
        AnswerResult? done = null;
        var cut = Render<ClosedExercise>(p => p.Add(x => x.Exercise, ex).Add(x => x.OnDone, r => done = r));
        cut.FindAll("button").First(b => b.TextContent.Contains("Das")).Click();
        cut.FindAll("button").First(b => b.TextContent.Contains("Prüfen")).Click();
        cut.WaitForAssertion(() => cut.FindAll("button").First(b => b.TextContent.Contains("Weiter")).Click());
        Assert.NotNull(done);
        Assert.Equal(Outcome.Correct, done!.Check.Outcome);
    }
}

public class DialogueExerciseTests : LotseComponentTest
{
    private static Exercise Dialogue() => new()
    {
        Id = "d1",
        Type = ExerciseType.Dialogue,
        NodeId = "SP.ALLTAG_BERUF",
        Band = CefrBand.B1_2,
        Prompt = "Kaffeeküche",
        Lines =
        [
            new DialogueLine("Jonas", "Hi! Neu hier?"),
            new DialogueLine("Du", "…", ["Ja, seit heute.", "Ja. Neu.", "Ich neu."], 0, ["Gut.", "Knapp.", "Fehler."]),
            new DialogueLine("Jonas", "Willkommen!"),
            new DialogueLine("Du", "…", ["Danke dir!", "Danke Ihnen!", "Danke."], 0, ["Passend.", "Zu formell.", "Kurz."]),
        ],
    };

    [Fact]
    public void Reveals_lines_up_to_the_first_turn_and_shows_feedback_after_a_choice()
    {
        var ex = Dialogue();
        Learning.Exercises[ex.Id] = ex;
        var cut = Render<DialogueExercise>(p => p.Add(x => x.Exercise, ex));

        Assert.Contains("Hi! Neu hier?", cut.Markup);
        Assert.DoesNotContain("Willkommen!", cut.Markup);

        cut.FindAll("button.dlg-option")[1].Click(); // the "Knapp." option
        Assert.Contains("Knapp.", cut.Markup);
        Assert.Contains("Besser:", cut.Markup);
        Assert.Contains("Willkommen!", cut.Markup); // story continued to the next turn
    }

    [Fact]
    public void Submits_one_choice_per_turn_in_order()
    {
        var ex = Dialogue();
        Learning.Exercises[ex.Id] = ex;
        var cut = Render<DialogueExercise>(p => p.Add(x => x.Exercise, ex));
        cut.FindAll("button.dlg-option")[0].Click();
        cut.FindAll("button.dlg-option")[2].Click();
        cut.FindAll("button").First(b => b.TextContent.Contains("auswerten")).Click();
        cut.WaitForAssertion(() => Assert.Contains("1 von 2", cut.Markup));
        Assert.Equal([0, 2], Learning.DialogueChoices[0]);
    }
}

public class ExerciseRunnerTests : LotseComponentTest
{
    [Theory]
    [InlineData(StepKind.Review, "Wiederholung")]
    [InlineData(StepKind.Recheck, "Wiedervorlage")]
    [InlineData(StepKind.Focus, "Schwerpunkt")]
    public void Step_kind_is_labelled_for_the_learner(StepKind kind, string label)
    {
        var ex = Cloze();
        Learning.Exercises[ex.Id] = ex;
        var cut = Render<ExerciseRunner>(p => p.Add(x => x.Exercise, ex).Add(x => x.Kind, kind).Add(x => x.Reason, "weil").Add(x => x.NodeTitle, "Präpositionen").Add(x => x.Index, 2).Add(x => x.Total, 9));
        Assert.Contains(label, cut.Markup);
        Assert.Contains("3 / 9", cut.Markup);
        Assert.Contains("weil", cut.Markup);
    }
}
