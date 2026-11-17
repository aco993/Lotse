using Lotse.Core.Engine;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.Data;
using Lotse.Web.Components.Exercises;
using Lotse.Web.Components.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lotse.Web.Tests;

public class ReadAloudPracticeTests : LotseComponentTest
{
    private sealed class NoDb : IDbContextFactory<LotseDbContext>
    {
        public LotseDbContext CreateDbContext() => throw new InvalidOperationException("Diese Komponente liest keine Datenbank.");
    }

    public ReadAloudPracticeTests()
    {
        Services.AddSingleton(new ContentCatalogProvider(ContentLoader.ResolveContentDirectory(), new NoDb(), NullLogger<ContentCatalogProvider>.Instance));
        var speech = JSInterop.SetupModule("./js/speech.js");
        speech.Setup<bool>("sttSupported").SetResult(true);
        speech.Setup<bool>("startRecognition", _ => true).SetResult(true);
    }

    [Fact]
    public void Each_word_is_marked_by_what_the_recogniser_wrote()
    {
        var cut = Render<ReadAloudPractice>();
        var sentence = cut.Find(".ra-sentence").TextContent.Trim();
        var words = ReadAloud.Tokens(sentence);

        cut.FindAll("button").First(b => b.TextContent.Contains("Vorlesen")).Click();
        cut.WaitForAssertion(() => Assert.Contains("Fertig", cut.Markup));

        // The recogniser drops the last word and writes the first one differently.
        var heard = string.Join(" ", ["xyz", .. words.Skip(1).SkipLast(1)]);
        var service = Services.GetRequiredService<SpeechService>();
        cut.InvokeAsync(() => { service.OnTranscript(heard, true); service.OnRecognitionEnded(); });

        cut.WaitForAssertion(() =>
        {
            var marked = cut.FindAll(".ra-sentence .ra-word");
            Assert.Equal(words.Count, marked.Count);
            Assert.Contains("ra-misheard", marked[0].ClassName);
            Assert.Contains("xyz", marked[0].GetAttribute("title"));
            Assert.Contains("ra-missing", marked[^1].ClassName);
            Assert.All(marked.Skip(1).SkipLast(1), w => Assert.Contains("ra-heard", w.ClassName));
            Assert.Contains("Noch einmal", cut.Markup);
            Assert.Contains("Heute 1 Satz gelesen", cut.Markup);
        });
    }

    [Fact]
    public void Without_speech_recognition_the_learner_is_told_which_browsers_can()
    {
        JSInterop.SetupModule("./js/speech.js").Setup<bool>("sttSupported").SetResult(false);
        var cut = Render<ReadAloudPractice>();
        cut.WaitForAssertion(() => Assert.Contains("Chrome oder Edge", cut.Markup));
    }
}
