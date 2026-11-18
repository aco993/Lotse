using System.Reflection;
using Lotse.Core.Telc;
using Lotse.Infrastructure.Content;
using Lotse.Infrastructure.Data;
using Lotse.Infrastructure.Services;
using Lotse.Web.Components.Telc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lotse.Web.Tests;

/// <summary>
/// Every shipped task of every objective telc part renders. The format validator checks the content; this checks the
/// page, which once threw on every Leseverstehen Teil 3 task (a loop variable read after the loop had ended) while
/// all content tests stayed green.
/// </summary>
public class TelcObjectiveTests : LotseComponentTest
{
    private sealed class NoDb : IDbContextFactory<LotseDbContext>
    {
        public LotseDbContext CreateDbContext() => throw new InvalidOperationException("Diese Komponente liest keine Datenbank.");
    }

    /// <summary>An <see cref="ITelcService"/> that only has content: rendering a task needs nothing else.</summary>
    public class ContentOnlyTelc : DispatchProxy
    {
        public TelcContent Content { get; set; } = TelcContent.Empty;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_Content" => Content,
            "get_TutorAvailable" => false,
            _ => throw new NotSupportedException($"{targetMethod?.Name} wird beim Anzeigen nicht gebraucht."),
        };
    }

    private static readonly Lazy<TelcContent> Content = new(() =>
    {
        var dir = ContentLoader.ResolveContentDirectory();
        var catalog = new ContentCatalogProvider(dir, new NoDb(), NullLogger<ContentCatalogProvider>.Instance).Catalog;
        return TelcContentLoader.Load(dir, catalog);
    });

    public TelcObjectiveTests()
    {
        var telc = DispatchProxy.Create<ITelcService, ContentOnlyTelc>();
        ((ContentOnlyTelc)(object)telc).Content = Content.Value;
        Services.AddSingleton(telc);
    }

    public static TheoryData<TelcPart, string> Tasks()
    {
        var data = new TheoryData<TelcPart, string>();
        foreach (var part in TelcContent.GeneratableParts)
            foreach (var id in Content.Value.TaskIds(part))
                data.Add(part, id);
        return data;
    }

    [Theory]
    [MemberData(nameof(Tasks))]
    public void Every_objective_task_renders(TelcPart part, string taskId)
    {
        var cut = Render<TelcObjective>(p => p.Add(c => c.Part, part).Add(c => c.TaskId, taskId));

        Assert.DoesNotContain("Das hat gerade nicht geklappt", cut.Markup);
        Assert.Equal(TelcBlueprint.Info(part).Title, cut.Find("h1").TextContent); // the page has no other heading
        Assert.Equal(Content.Value.Key(part, taskId)!.Count, cut.FindAll("[role=radiogroup]").Count);
    }
}
