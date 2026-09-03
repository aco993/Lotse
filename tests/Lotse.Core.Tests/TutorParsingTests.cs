using Lotse.Infrastructure.Ai;

namespace Lotse.Core.Tests;

public class TutorParsingTests
{
    [Fact]
    public void Extracts_object_from_code_fence_and_prose()
    {
        var text = "Hier ist die Bewertung:\n```json\n{\"overallScore\": 0.6, \"errors\": []}\n```\nViel Erfolg!";
        Assert.Equal("{\"overallScore\": 0.6, \"errors\": []}", TutorBase.ExtractJsonObject(text));
    }

    [Fact]
    public void Plain_object_is_returned_unchanged()
        => Assert.Equal("{\"a\":1}", TutorBase.ExtractJsonObject("{\"a\":1}"));

    [Fact]
    public void Text_without_object_throws()
        => Assert.Throws<InvalidOperationException>(() => TutorBase.ExtractJsonObject("keine Ahnung"));

    [Fact]
    public void Options_resolve_availability_per_provider()
    {
        var anthropic = new TutorOptions { Provider = TutorProvider.Anthropic, ApiKey = "sk-test" };
        Assert.True(anthropic.IsConfigured);
        var local = new TutorOptions { Provider = TutorProvider.OpenAi, BaseUrl = "http://localhost:11434/v1", Model = "llama3.2" };
        Assert.True(local.IsConfigured); // no key needed for a local server
        var missing = new TutorOptions { Provider = TutorProvider.OpenAi, Model = "x" };
        Assert.False(missing.IsConfigured);
    }
}
