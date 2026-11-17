using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Infrastructure.Content;

namespace Lotse.Core.Tests;

/// <summary>Reading aloud: the transcript lined up with the sentence, word by word, without punishing spelling the recogniser cannot hear.</summary>
public sealed class ReadAloudTests
{
    private static readonly ContentCatalog Catalog = ContentLoader.Load(ContentLoader.ResolveContentDirectory());

    [Fact]
    public void A_perfect_reading_hears_every_word_regardless_of_case_punctuation_and_eszett()
    {
        var r = ReadAloud.Compare("Ich bin der Meinung, dass wir mehr Zeit brauchen.", "ich bin der meinung das wir mehr zeit brauchen");
        Assert.All(r.Words, w => Assert.Equal(WordHeard.Heard, w.Status));
        Assert.Equal(1.0, r.Score);
        Assert.Empty(r.Extra);
    }

    [Fact]
    public void A_wrong_ending_is_misheard_in_place_and_says_what_was_heard()
    {
        var r = ReadAloud.Compare("Ich fahre mit dem Bus.", "ich fahre mit den bus");
        var w = Assert.Single(r.ToPractise);
        Assert.Equal(("dem", WordHeard.Misheard, "den"), (w.Word, w.Status, w.HeardAs));
        Assert.Equal(0.8, r.Score, 6);
    }

    [Fact]
    public void A_swallowed_word_is_missing_and_an_added_one_is_extra_without_lowering_the_score()
    {
        var r = ReadAloud.Compare("Das sehe ich ganz anders.", "das sehe ich ähm anders");
        Assert.Equal(WordHeard.Misheard, r.Words.Single(w => w.Word == "ganz").Status);

        var skipped = ReadAloud.Compare("Das sehe ich ganz anders.", "das sehe ich anders");
        Assert.Equal(WordHeard.Missing, skipped.Words.Single(w => w.Word == "ganz").Status);
        Assert.Equal(0.8, skipped.Score, 6);

        var added = ReadAloud.Compare("Das sehe ich anders.", "also das sehe ich anders");
        Assert.Equal(1.0, added.Score);
        Assert.Equal(["also"], added.Extra);
    }

    [Fact]
    public void Umlauts_written_out_count_as_the_same_word()
    {
        var r = ReadAloud.Compare("Wir müssen das Problem lösen.", "wir muessen das problem loesen");
        Assert.Equal(1.0, r.Score);
    }

    [Fact]
    public void Nothing_heard_means_every_word_missing()
    {
        var r = ReadAloud.Compare("Meiner Meinung nach stimmt das.", "");
        Assert.All(r.Words, w => Assert.Equal(WordHeard.Missing, w.Status));
        Assert.Equal(0.0, r.Score);
    }

    [Fact]
    public void The_sentences_are_spoken_redemittel_filled_in_and_free_of_cloze_hints()
    {
        var sentences = ReadAloud.Sentences(Catalog);
        Assert.True(sentences.Count >= 20, $"nur {sentences.Count} Sätze");
        Assert.All(sentences, s =>
        {
            Assert.Contains(s.NodeId, ReadAloud.SpokenNodes);
            Assert.DoesNotContain("___", s.Sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("(", s.Sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("…", s.Sentence, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(sentences, s => s.NodeId == "RM.FORMELLE_EMAIL");
    }
}
