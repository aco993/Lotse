using System.Reflection;
using Bunit;
using Lotse.Core.Engine;
using Lotse.Core.Model;
using Lotse.Core.Telc;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.Data;
using Lotse.Infrastructure.Services;
using Lotse.Web.Components.Telc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lotse.Web.Tests;

/// <summary>The telc email without a key: after "Fertig – prüfen" the rule-based findings are on the page, in the task's register.</summary>
public class TelcWritingTests : LotseComponentTest
{
    private sealed class NoDb : IDbContextFactory<LotseDbContext>
    {
        public LotseDbContext CreateDbContext() => throw new InvalidOperationException("Diese Komponente liest keine Datenbank.");
    }

    /// <summary>Content and the real analyzer; no tutor, no database.</summary>
    public class RulesOnlyTelc : DispatchProxy
    {
        public TelcContent Content { get; set; } = TelcContent.Empty;
        public Lexicon Lexicon { get; set; } = Lexicon.Empty;
        public List<bool?> FormalAsked { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_Content": return Content;
                case "get_TutorAvailable": return false;
                case nameof(ITelcService.PersonalizeAsync): return Task.FromResult((string)args![0]!);
                case nameof(ITelcService.QuickFindings):
                    var formal = (bool?)args![2];
                    FormalAsked.Add(formal);
                    var stand = new Exercise { Id = "t", Type = ExerciseType.FreeWrite, NodeId = "SP.VORTRAG", Band = CefrBand.B2_1, Prompt = "telc" };
                    return FreeTextAnalyzer.Analyze((string)args[0]!, stand, Lexicon, (bool)args[1]!, formal).Findings;
                default: throw new NotSupportedException($"{targetMethod?.Name} braucht diese Seite ohne Schlüssel nicht.");
            }
        }
    }

    private static readonly Lazy<(TelcContent Content, Lexicon Lexicon)> Loaded = new(() =>
    {
        var dir = ContentLoader.ResolveContentDirectory();
        var catalog = new ContentCatalogProvider(dir, new NoDb(), NullLogger<ContentCatalogProvider>.Instance).Catalog;
        return (TelcContentLoader.Load(dir, catalog), Lexicon.FromCatalog(catalog));
    });

    private readonly RulesOnlyTelc _telc;

    public TelcWritingTests()
    {
        var telc = DispatchProxy.Create<ITelcService, RulesOnlyTelc>();
        _telc = (RulesOnlyTelc)(object)telc;
        _telc.Content = Loaded.Value.Content;
        _telc.Lexicon = Loaded.Value.Lexicon;
        Services.AddSingleton(telc);
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public void The_finished_email_lists_what_the_rules_found_in_the_tasks_register()
    {
        var cut = Render<TelcWriting>(p => p.Add(c => c.TaskId, "sa.hotel"));
        Assert.Empty(cut.FindAll(".tc-findings"));

        cut.Find("textarea").Input("Betreff: Beschwerde\n\nHallo,\n\nich war in Ihre Hotel drei Nächte für meine Arbeit. Das Zimmer war laut und das Internet war kaputt. "
            + "Ich habe angerufen die Rezeption, aber niemand ist gekommen. Ihr habt mir ein ruhiges Zimmer versprochen.\n\nLG\nMilan");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Fertig – prüfen", StringComparison.Ordinal)).Click();

        var box = cut.Find(".tc-findings");
        Assert.Contains("Ihre Hotel", box.TextContent);
        Assert.Contains("habe angerufen die", box.TextContent);
        Assert.Contains("Ihr habt", box.TextContent);
        Assert.Contains("LG", box.TextContent);
        Assert.Equal([true], _telc.FormalAsked); // sa.hotel is a formal complaint
    }
}
