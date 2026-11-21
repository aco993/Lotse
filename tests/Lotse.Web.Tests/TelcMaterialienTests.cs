using System.Reflection;
using Bunit;
using Lotse.Core.Telc;
using Lotse.Infrastructure.Services;
using Lotse.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lotse.Web.Tests;

/// <summary>The page of official material: links leave the app safely, and a paper result is converted before it is stored.</summary>
public class TelcMaterialienTests : LotseComponentTest
{
    /// <summary>An <see cref="ITelcService"/> that only records what the page stores.</summary>
    public class RecordingTelc : DispatchProxy
    {
        public List<(string Material, DateOnly Date, IReadOnlyDictionary<TelcPart, double> Points)> Stored { get; } = [];
        public Guid SimulationId { get; } = Guid.NewGuid();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(ITelcService.RecordMockExamAsync))
                throw new NotSupportedException($"{targetMethod?.Name} braucht diese Seite nicht.");
            var points = (IReadOnlyDictionary<TelcPart, double>)args![2]!;
            Stored.Add(((string)args[0]!, (DateOnly)args[1]!, points));
            var tasks = points.Keys.ToDictionary(p => p, _ => TelcMaterials.ExternalTaskId((string)args[0]!));
            return Task.FromResult(new TelcSimulationView(SimulationId, DateTime.UtcNow, DateTime.UtcNow, tasks, new Dictionary<TelcPart, TelcAttemptRow>()));
        }
    }

    private readonly RecordingTelc _telc;

    public TelcMaterialienTests()
    {
        var telc = DispatchProxy.Create<ITelcService, RecordingTelc>();
        _telc = (RecordingTelc)(object)telc;
        Services.AddSingleton(telc);
        Services.AddSingleton(TimeProvider.System);
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public void Every_material_is_listed_and_its_links_open_apart_from_the_app()
    {
        var cut = Render<TelcMaterialien>();

        foreach (var m in TelcMaterials.All) Assert.Contains(m.Title, cut.Markup);
        var links = cut.FindAll(".tc-material-links a");
        Assert.Equal(TelcMaterials.All.Sum(m => m.Links.Count), links.Count);
        Assert.All(links, a =>
        {
            Assert.Equal("_blank", a.GetAttribute("target"));
            Assert.Contains("noopener", a.GetAttribute("rel"));
        });
    }

    [Fact]
    public void Right_answers_become_telc_points_and_empty_parts_stay_out()
    {
        var cut = Render<TelcMaterialien>();

        cut.Find("#mock-Lesen3").Change("8");           // 8 of 10 · 2.5
        cut.Find("#mock-Sprachbausteine2").Change("9"); // 9 of 10 · 1.5
        cut.Find("#mock-Schreiben").Change("36");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Eintragen").Click();

        var stored = Assert.Single(_telc.Stored);
        Assert.Equal("telc-b2-ut1", stored.Material);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), stored.Date);
        Assert.Equal(3, stored.Points.Count);
        Assert.Equal(20, stored.Points[TelcPart.Lesen3]);
        Assert.Equal(13.5, stored.Points[TelcPart.Sprachbausteine2]);
        Assert.Equal(36, stored.Points[TelcPart.Schreiben]);
        Assert.EndsWith($"telc/simulation/{_telc.SimulationId}", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void Nothing_entered_means_nothing_to_store()
    {
        var cut = Render<TelcMaterialien>();
        Assert.True(cut.FindAll("button").Single(b => b.TextContent.Trim() == "Eintragen").HasAttribute("disabled"));
    }
}
