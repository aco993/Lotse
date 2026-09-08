using System.Text.RegularExpressions;
using Lotse.Core.Exam;
using Lotse.Core.Model;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.Services;

namespace Lotse.Core.Tests;

/// <summary>The exam's procedural rules and the speaker split behind the multi-voice listening texts.</summary>
public class ExamTimingTests
{
    private static Exercise Reading(string prompt, bool audio, ExerciseContext ctx = ExerciseContext.Pruefung) => new()
    {
        Id = "r",
        Type = ExerciseType.Reading,
        NodeId = audio ? "HV.HOEREN_ALLTAG" : "LS.LESEN",
        Band = CefrBand.B2_1,
        Prompt = prompt,
        Text = "Ein Text.",
        AudioOnly = audio,
        Context = ctx,
        Questions = [new ReadingQuestion("F?", ["a", "b"], 0)],
    };

    [Fact]
    public void Inline_dashes_and_separate_lines_both_split_into_turns_without_the_labels()
    {
        // The inline form of the content files ...
        var inline = SpeakerTurns.Parse("Lena: Hast du schon gehört? – Markus: Ja, leider. – Lena: Und was meinst du?");
        Assert.Equal(["Lena", "Markus", "Lena"], inline.Select(t => t.Speaker));
        Assert.Equal("Ja, leider.", inline[1].Text);
        Assert.DoesNotContain(inline, t => t.Text.Contains(':'));

        // ... and the one-turn-per-line form the generator is told to use, with a title in the name.
        var lines = SpeakerTurns.Parse("Moderator: Frau Dr. Keller, ist das ein Trend?\nDr. Keller: Nein, das ist mehr als ein Trend.\nModerator: Woran sehen Sie das?");
        Assert.Equal(["Moderator", "Dr. Keller", "Moderator"], lines.Select(t => t.Speaker));
        Assert.True(SpeakerTurns.IsDialogue("A: eins\nB: zwei\nA: drei"));
    }

    [Fact]
    public void A_monologue_and_an_announcement_full_of_colons_stay_one_unlabelled_turn()
    {
        foreach (var text in new[]
        {
            "Information zu Ihrer Reise: Der Regionalexpress fällt aus. Reisende nach München: bitte Gleis 8. Wir bitten um Verständnis.",
            "Sprechzeiten: Montag: 8 bis 12 Uhr. Dienstag: 15 bis 18 Uhr. Mittwoch: geschlossen. Termine: nur telefonisch.",
            "Meine Damen und Herren, wenn wir über Fachkräftemangel sprechen, denken viele zuerst an Ingenieure.",
        })
        {
            var turns = SpeakerTurns.Parse(text);
            var only = Assert.Single(turns);
            Assert.Null(only.Speaker);
            Assert.Equal(text, only.Text);
            Assert.False(SpeakerTurns.IsDialogue(text));
        }
        Assert.Empty(SpeakerTurns.Parse(null));
    }

    [Fact]
    public void Strangers_get_alternating_voices_while_known_names_keep_theirs()
    {
        var voices = SpeakerVoices.ForScript(["Moderator", "Dr. Keller", "Moderator", "Frau Berg", "Dr. Keller"]);
        Assert.Equal(SpeechVoice.Male, voices["Moderator"]);     // listed role
        Assert.Equal(SpeechVoice.Female, voices["Dr. Keller"]);  // stranger after a male voice
        Assert.Equal(SpeechVoice.Female, voices["Frau Berg"]);   // courtesy title wins over the alternation

        var pair = SpeakerVoices.ForScript(["Lena", "Markus"]);
        Assert.Equal(SpeechVoice.Female, pair["Lena"]);
        Assert.Equal(SpeechVoice.Male, pair["Markus"]);

        var strangers = SpeakerVoices.ForScript(["Anton", "Bert", "Anton"]);
        Assert.NotEqual(strangers["Anton"], strangers["Bert"]);
        Assert.Null(SpeakerVoices.Known("Anton"));
        Assert.Equal(SpeechVoice.Male, SpeakerVoices.For("Anton"));
    }

    [Fact]
    public void Listening_plays_follow_the_exam_in_the_exam_context_and_stay_at_two_elsewhere()
    {
        Assert.Equal(1, ExamTiming.MaxPlays(Reading("Hören Teil 1: Sie hören eine Ansage.", audio: true)));
        Assert.Equal(1, ExamTiming.MaxPlays(Reading("Hören Teil 3: Wer sagt was?", audio: true)));
        Assert.Equal(2, ExamTiming.MaxPlays(Reading("Hören Teil 2: Interview", audio: true)));
        Assert.Equal(2, ExamTiming.MaxPlays(Reading("Hören Teil 4: Radiobeitrag", audio: true)));
        Assert.Equal(2, ExamTiming.MaxPlays(Reading("Hören: Sprachnachricht", audio: true)));
        Assert.Equal(2, ExamTiming.MaxPlays(Reading("Hören Teil 1: Hören Sie zweimal.", audio: true, ExerciseContext.Alltag)));
        Assert.Equal(int.MaxValue, ExamTiming.MaxPlays(Reading("Lesen Teil 3", audio: false)));
        Assert.Equal(3, ExamTiming.Part(Reading("Hören Teil 3: Wer sagt was?", audio: true)));
        Assert.Null(ExamTiming.Part(Reading("Hören: Sprachnachricht", audio: true)));
    }

    [Fact]
    public void Time_limits_exist_only_for_exam_format_reading_writing_and_speaking()
    {
        Assert.Equal(18 * 60, ExamTiming.TimeLimitSeconds(Reading("Lesen Teil 1: Vier Personen", audio: false)));
        Assert.Equal(12 * 60, ExamTiming.TimeLimitSeconds(Reading("Lesen Teil 3: Zeitungsartikel", audio: false)));
        Assert.Equal(6 * 60, ExamTiming.TimeLimitSeconds(Reading("Lesen Teil 5: Hausordnung", audio: false)));
        Assert.Null(ExamTiming.TimeLimitSeconds(Reading("Hören Teil 2: Interview", audio: true)));          // the audio sets the pace
        Assert.Null(ExamTiming.TimeLimitSeconds(Reading("Lesen Teil 3", audio: false, ExerciseContext.Beruf)));

        var write = new Exercise { Id = "w", Type = ExerciseType.FreeWrite, NodeId = "SC.FORUM", Band = CefrBand.B2_1, Prompt = "Schreiben Teil 1", MinWords = 150, Context = ExerciseContext.Pruefung };
        Assert.Equal(50 * 60, ExamTiming.TimeLimitSeconds(write));
        Assert.Equal(25 * 60, ExamTiming.TimeLimitSeconds(write with { MinWords = 100 }));
        Assert.Null(ExamTiming.TimeLimitSeconds(write with { Context = ExerciseContext.Alltag }));

        var talk = new Exercise { Id = "s", Type = ExerciseType.Speak, NodeId = "SP.VORTRAG", Band = CefrBand.B2_1, Prompt = "Sprechen Teil 1", TargetSeconds = 240, Context = ExerciseContext.Pruefung };
        Assert.Equal(240, ExamTiming.TimeLimitSeconds(talk));
        Assert.Equal(4 * 60, ExamTiming.TimeLimitSeconds(talk with { TargetSeconds = null }));
    }

    /// <summary>
    /// Every listening text announced as a conversation must split into speakers, or it would be read in one voice
    /// with the names spoken aloud - the exact thing this feature exists to prevent. Guards the content convention
    /// (label, colon, space; turns joined by " – " or a line break) for every text added later.
    /// </summary>
    [Fact]
    public void Every_listening_text_announced_as_a_conversation_splits_into_speakers()
    {
        var catalog = ContentLoader.Load(ContentLoader.ResolveContentDirectory());
        var conversations = catalog.Exercises
            .Where(e => e is { Type: ExerciseType.Reading, AudioOnly: true } && Regex.IsMatch(e.Prompt, @"Gespräch|Diskussion|Interview|Unterhaltung"))
            .ToList();
        Assert.NotEmpty(conversations);
        foreach (var ex in conversations)
        {
            var turns = SpeakerTurns.Parse(ex.Text);
            Assert.True(turns.Count(t => t.Speaker is not null) >= 3, $"{ex.Id}: als Gespräch angekündigt, aber nicht in Sprecher zerlegbar");
            Assert.True(SpeakerVoices.ForScript(turns.Select(t => t.Speaker)).Values.Distinct().Count() == 2, $"{ex.Id}: beide Sprecher bekämen dieselbe Stimme");
        }
    }
}
