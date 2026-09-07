using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lotse.Core.Model;
using Lotse.Core.Tutor;
using Lotse.Infrastructure.Ai;
using Lotse.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lotse.Core.Tests;

/// <summary>Scripted HTTP server: each request is answered by the next scripted response; requests are recorded.</summary>
internal sealed class ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] script) : HttpMessageHandler
{
    private int _i;
    public List<JsonNode> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = await request.Content!.ReadAsStringAsync(ct);
        Requests.Add(JsonNode.Parse(body)!);
        var step = script[Math.Min(_i, script.Length - 1)];
        _i++;
        return step(request);
    }

    public static HttpResponseMessage Chat(string content, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } })) };

    public static HttpResponseMessage Error(HttpStatusCode status, string message)
        => new(status) { Content = new StringContent(JsonSerializer.Serialize(new { error = new { message } })) };
}

public class OpenAiCompatibleTutorTests
{
    private static TutorOptions Options => new() { Provider = TutorProvider.OpenAi, BaseUrl = "http://test/v1", Model = "m", TimeoutSeconds = 30 };
    private static readonly LearnerContext Ctx = new("Serbisch", [], [], [new ErrorType("KASUS_PRAEP", "GR.KASUS_PRAEPOSITIONEN", "t", "d", 2)]);
    private static readonly Exercise Task = new() { Id = "w", Type = ExerciseType.FreeWrite, NodeId = "SC.FORMELLE_NACHRICHT", Band = CefrBand.B2_1, Prompt = "Schreiben Sie.", MinWords = 40, Rubric = ["A", "B"] };

    private const string EvalJson = """{"overallScore":0.55,"estimatedLevel":"B1.2","rubric":[{"criterion":"A","score":3,"comment":"ok"}],"errors":[{"code":"KASUS_PRAEP","snippet":"mit der Auto","correction":"mit dem Auto","explanation":"Dativ"},{"code":"NOPE","snippet":"","correction":"","explanation":""}],"correctedText":"…","feedback":"Gut.","suggestedNodeIds":[],"upgrades":["Besser so."]}""";

    [Fact]
    public async Task Uses_json_schema_format_and_maps_the_evaluation()
    {
        var handler = new ScriptedHandler(_ => ScriptedHandler.Chat(EvalJson));
        var tutor = new OpenAiCompatibleTutor(Options, NullLogger<OpenAiCompatibleTutor>.Instance, handler);

        var eval = await tutor.EvaluateAsync(Task, "Ich fahre mit der Auto.", ProductionMode.Writing, Ctx);

        Assert.Equal("json_schema", handler.Requests[0]["response_format"]!["type"]!.GetValue<string>());
        Assert.Equal(0.55, eval.OverallScore, 3);
        Assert.Single(eval.Errors); // unknown code dropped
        Assert.Equal("KASUS_PRAEP", eval.Errors[0].Code);
        Assert.Equal("B1.2", eval.EstimatedLevel);
    }

    [Fact]
    public async Task Falls_back_to_json_object_when_schema_format_is_rejected_and_remembers_it()
    {
        var handler = new ScriptedHandler(
            _ => ScriptedHandler.Error(HttpStatusCode.BadRequest, "response_format json_schema not supported"),
            _ => ScriptedHandler.Chat("Hier:\n```json\n" + EvalJson + "\n```"),
            _ => ScriptedHandler.Chat(EvalJson));
        var tutor = new OpenAiCompatibleTutor(Options, NullLogger<OpenAiCompatibleTutor>.Instance, handler);

        var eval = await tutor.EvaluateAsync(Task, "x", ProductionMode.Writing, Ctx);
        Assert.Equal(0.55, eval.OverallScore, 3);
        Assert.Equal("json_object", handler.Requests[1]["response_format"]!["type"]!.GetValue<string>());

        await tutor.EvaluateAsync(Task, "y", ProductionMode.Writing, Ctx);
        Assert.Equal(3, handler.Requests.Count); // second call skips the schema attempt
        Assert.Equal("json_object", handler.Requests[2]["response_format"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Retries_transient_errors_then_succeeds()
    {
        var handler = new ScriptedHandler(
            _ => ScriptedHandler.Error(HttpStatusCode.TooManyRequests, "slow down"),
            _ => ScriptedHandler.Chat("bereit"));
        var tutor = new OpenAiCompatibleTutor(Options, NullLogger<OpenAiCompatibleTutor>.Instance, handler);
        var reply = await tutor.DiscussAsync("These", [new ChatTurn(true, "Hallo")], Ctx);
        Assert.Equal("bereit", reply);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Authentication_errors_surface_with_status_code()
    {
        var handler = new ScriptedHandler(_ => ScriptedHandler.Error(HttpStatusCode.Unauthorized, "invalid api key"));
        var tutor = new OpenAiCompatibleTutor(Options, NullLogger<OpenAiCompatibleTutor>.Instance, handler);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => tutor.DiscussAsync("t", [new ChatTurn(true, "x")], Ctx));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Contains("401", TutorRegistry.Friendly(ex));
    }

    [Fact]
    public async Task Discussion_sends_system_prompt_and_history_in_order()
    {
        var handler = new ScriptedHandler(_ => ScriptedHandler.Chat("Ich sehe das anders.\n\n✎ Sprachlich gut."));
        var tutor = new OpenAiCompatibleTutor(Options, NullLogger<OpenAiCompatibleTutor>.Instance, handler);
        await tutor.DiscussAsync("Homeoffice", [new ChatTurn(true, "Ich bin dafür."), new ChatTurn(false, "Warum?"), new ChatTurn(true, "Weil …")], Ctx);
        var msgs = handler.Requests[0]["messages"]!.AsArray();
        Assert.Equal(["system", "user", "assistant", "user"], msgs.Select(m => m!["role"]!.GetValue<string>()));
        Assert.Contains("Homeoffice", msgs[0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Generated_drills_are_validated_before_they_are_returned()
    {
        var gen = """{"exercises":[{"type":"cloze","band":"B2_1","prompt":"mit ___ (der Bus)","instruction":"","answers":["dem Bus"],"options":[],"correctIndex":-1,"explanation":"Dativ","serbianNote":""},{"type":"cloze","band":"B2_1","prompt":"kaputt","instruction":"","answers":[],"options":[],"correctIndex":-1,"explanation":"","serbianNote":""}]}""";
        var handler = new ScriptedHandler(_ => ScriptedHandler.Chat(gen));
        var tutor = new OpenAiCompatibleTutor(Options, NullLogger<OpenAiCompatibleTutor>.Instance, handler);
        var node = new SkillNode("GR.KASUS_PRAEPOSITIONEN", SkillArea.Grammatik, "Präp", "d", CefrBand.B1_2, true, null, []);
        var made = await tutor.GenerateExercisesAsync(node, CefrBand.B2_1, 2, ExerciseContext.Beruf, Ctx, []);
        Assert.Single(made);
        Assert.Equal(ExerciseSource.Generated, made[0].Source);
        Assert.StartsWith("gen.gr.kasus_praepositionen.", made[0].Id);
    }
}

public sealed class TutorRegistryTests : IAsyncLifetime
{
    private sealed class TestDbFactory(string path) : IDbContextFactory<LotseDbContext>
    {
        public LotseDbContext CreateDbContext() => new(new DbContextOptionsBuilder<LotseDbContext>().UseSqlite($"Data Source={path}").Options);
    }

    private const string UserId = "test-user";

    private string _path = "";
    private TestDbFactory _factory = default!;

    public async ValueTask InitializeAsync()
    {
        _path = Path.Combine(Path.GetTempPath(), $"lotse-reg-{Guid.NewGuid():N}.db");
        _factory = new TestDbFactory(_path);
        await using var db = _factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        // Settings has a real FK to AspNetUsers – seed the one identity FakeCurrentUserAccessor claims.
        db.Users.Add(new ApplicationUser { Id = UserId, UserName = UserId });
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using (var db = _factory.CreateDbContext()) await db.Database.EnsureDeletedAsync();
        try { File.Delete(_path); } catch (IOException) { }
    }

    // No environment fallback: the machine running the tests may well have real keys set.
    private TutorRegistry NewRegistry(IDataProtectionProvider dp) => new(_factory, dp, NullLoggerFactory.Instance, new FakeCurrentUserAccessor(UserId), env: _ => null);

    [Fact]
    public void Default_is_groq_without_key_and_explains_what_is_missing()
    {
        var reg = NewRegistry(new EphemeralDataProtectionProvider());
        Assert.Equal("groq", reg.Settings.PresetId);
        Assert.False(reg.IsAvailable);
        Assert.Contains("Schlüssel fehlt", reg.Description);
        Assert.Contains("console.groq.com", reg.Description);
    }

    [Fact]
    public async Task Saved_settings_are_persisted_with_an_encrypted_key_and_restored_on_the_next_start()
    {
        var dp = new EphemeralDataProtectionProvider();
        var reg = NewRegistry(dp);
        await reg.SaveAsync(new TutorSettings("groq", null, "llama-3.3-70b-versatile", "gsk_secret_123"));
        Assert.True(reg.IsAvailable);
        Assert.Contains("Groq", reg.Description);

        await using (var db = _factory.CreateDbContext())
        {
            var row = await db.Settings.SingleAsync();
            Assert.DoesNotContain("gsk_secret_123", row.Value); // never stored in clear text
        }

        var second = NewRegistry(dp);
        Assert.False(second.IsAvailable); // before InitializeAsync: defaults
        await second.InitializeAsync();
        Assert.True(second.IsAvailable);
        Assert.Equal("gsk_secret_123", second.Settings.ApiKey);
    }

    [Fact]
    public async Task Key_encrypted_with_another_key_ring_is_ignored_gracefully()
    {
        var reg = NewRegistry(new EphemeralDataProtectionProvider());
        await reg.SaveAsync(new TutorSettings("groq", null, "m", "gsk_x"));
        var other = NewRegistry(new EphemeralDataProtectionProvider());
        await other.InitializeAsync(); // cannot unprotect → key null, but no exception
        Assert.Equal("groq", other.Settings.PresetId);
        Assert.Null(other.Settings.ApiKey);
    }

    [Fact]
    public async Task Local_presets_need_no_key()
    {
        var reg = NewRegistry(new EphemeralDataProtectionProvider());
        await reg.SaveAsync(new TutorSettings("ollama", null, "qwen2.5:7b", null));
        Assert.True(reg.IsAvailable);
        Assert.Contains("Ollama", reg.Description);
    }

    [Fact]
    public async Task Timeout_and_effort_survive_a_save_and_reload()
    {
        var dp = new EphemeralDataProtectionProvider();
        var reg = NewRegistry(dp);
        await reg.SaveAsync(new TutorSettings("ollama", null, "qwen2.5-lotse", null, "high", 420));

        var second = NewRegistry(dp);
        await second.InitializeAsync();

        Assert.Equal(420, second.Settings.TimeoutSeconds);
        Assert.Equal("high", second.Settings.Effort);
    }

    [Fact]
    public void Local_presets_start_with_a_larger_time_budget_than_hosted_ones()
    {
        // A hosted 70B answers a full evaluation in seconds; a local 7B needs one to three minutes for the same
        // ~800 tokens, so one flat budget cannot serve both.
        Assert.Equal(300, TutorSettings.ForPreset("ollama").TimeoutSeconds);
        Assert.Equal(300, TutorSettings.ForPreset("lmstudio").TimeoutSeconds);
        Assert.Equal(120, TutorSettings.ForPreset("groq").TimeoutSeconds);
        Assert.Equal(120, TutorSettings.ForPreset("anthropic").TimeoutSeconds);
    }

    [Fact]
    public void Switching_preset_takes_that_provider_model_and_budget_along()
    {
        var fresh = TutorSettings.ForPreset("ollama");
        Assert.Equal("ollama", fresh.PresetId);
        Assert.Equal("qwen2.5:7b", fresh.Model);
        Assert.Equal(300, fresh.TimeoutSeconds);
        Assert.Null(fresh.ApiKey);
        Assert.Null(fresh.BaseUrl); // falls back to the preset's own URL
    }

    [Fact]
    public async Task Probe_reports_unreachable_servers_in_plain_words()
    {
        var reg = NewRegistry(new EphemeralDataProtectionProvider());
        var probe = await reg.ProbeAsync(new TutorSettings("custom", "http://127.0.0.1:9/v1", "m", null, TimeoutSeconds: 20));
        Assert.False(probe.Ok);
        Assert.False(string.IsNullOrWhiteSpace(probe.Message));
    }
}
