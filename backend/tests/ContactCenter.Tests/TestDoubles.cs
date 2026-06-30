using ContactCenter.Api.Ari;
using ContactCenter.Api.Auth;
using ContactCenter.Api.CallFlow;
using ContactCenter.Api.Data;
using ContactCenter.Api.Realtime;
using ContactCenter.Api.Tts;
using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Tests;

/// <summary>Vangt de wachtrij-pushes (per tenant) op zodat tests ze kunnen inspecteren.</summary>
public sealed class FakeRealtimeNotifier : IRealtimeNotifier
{
    public List<(int TenantId, IReadOnlyList<WaitingCallView> Waiting)> QueuePushes { get; } = [];

    public Task QueuesChangedAsync(int tenantId, IReadOnlyList<WaitingCallView> waiting, CancellationToken ct = default)
    {
        QueuePushes.Add((tenantId, waiting));
        return Task.CompletedTask;
    }
}

/// <summary>Tenant-accessor voor tests; standaard tenant 0 (gelijk aan de default-TenantId van geseede data).</summary>
public sealed class TestTenantAccessor : ITenantAccessor
{
    public int? TenantId { get; set; } = 0;
}

/// <summary>In-memory DbContext-factory met seed-helpers voor de tests.</summary>
public sealed class TestDbContextFactory : IDbContextFactory<CcDbContext>
{
    private readonly DbContextOptions<CcDbContext> _options =
        new DbContextOptionsBuilder<CcDbContext>()
            .UseInMemoryDatabase($"cc-test-{Guid.NewGuid()}")
            .Options;

    /// <summary>De tenant-context die de gemaakte DbContexts gebruiken (default 0).</summary>
    public TestTenantAccessor Tenant { get; } = new();

    public CcDbContext CreateDbContext() => new(_options, Tenant);

    public void Seed(int wrapUpSeconds, params (string name, string[] queues)[] agents)
    {
        using var db = CreateDbContext();
        var queueNames = agents.SelectMany(a => a.queues).Distinct();
        var queues = queueNames.ToDictionary(n => n, n => new QueueConfig { Name = n, DisplayName = n });
        db.Queues.AddRange(queues.Values);
        db.SaveChanges();

        foreach (var (name, qs) in agents)
        {
            db.Agents.Add(new Agent
            {
                Name = name,
                DisplayName = name,
                Endpoint = $"PJSIP/{name}",
                QueueAssignments = [.. qs.Select(q => new AgentQueueAssignment { QueueConfigId = queues[q].Id })],
            });
        }
        db.Settings.Add(new GlobalSettings { WrapUpSeconds = wrapUpSeconds });
        db.SaveChanges();
    }
}

/// <summary>Registreert alle ARI-aanroepen zodat tests de gespreksflow kunnen controleren.</summary>
public sealed class FakeAriClient : IAriClient
{
    public List<(string Endpoint, string AppArgs, string CallerId, string ChannelId)> Originates = [];
    public Dictionary<string, string> BridgeTypes = new(); // bridgeId → type
    public List<(string Bridge, string Channel)> Added = [];
    public List<(string Bridge, string Channel)> Removed = [];
    public List<string> DestroyedBridges = [];
    public List<string> Hangups = [];
    public List<string> MohStarted = [];
    public List<(string Bridge, string Class)> MohStartedWithClass = [];
    public List<(string Channel, string Context, string Extension)> Continued = [];
    public List<(string Channel, string Media)> Plays = [];

    private int _bridgeSeq;
    private int _channelSeq;

    public Task AnswerAsync(string channelId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string> PlayAsync(string channelId, string media, CancellationToken ct = default)
    {
        Plays.Add((channelId, media));
        return Task.FromResult($"playback-{Plays.Count}");
    }
    public Task ContinueInDialplanAsync(string channelId, string context, string extension, int priority,
        CancellationToken ct = default)
    {
        Continued.Add((channelId, context, extension));
        return Task.CompletedTask;
    }

    public Task HangupAsync(string channelId, CancellationToken ct = default)
    {
        Hangups.Add(channelId);
        return Task.CompletedTask;
    }

    public Task<string> CreateBridgeAsync(string type, CancellationToken ct = default)
    {
        var id = $"bridge-{++_bridgeSeq}";
        BridgeTypes[id] = type;
        return Task.FromResult(id);
    }

    public Task DestroyBridgeAsync(string bridgeId, CancellationToken ct = default)
    {
        DestroyedBridges.Add(bridgeId);
        return Task.CompletedTask;
    }

    public Task AddToBridgeAsync(string bridgeId, string channelId, CancellationToken ct = default)
    {
        Added.Add((bridgeId, channelId));
        return Task.CompletedTask;
    }

    public Task RemoveFromBridgeAsync(string bridgeId, string channelId, CancellationToken ct = default)
    {
        Removed.Add((bridgeId, channelId));
        return Task.CompletedTask;
    }

    public Task StartBridgeMohAsync(string bridgeId, string mohClass = "default", CancellationToken ct = default)
    {
        MohStarted.Add(bridgeId);
        MohStartedWithClass.Add((bridgeId, mohClass));
        return Task.CompletedTask;
    }

    public Task<string> OriginateToStasisAsync(string endpoint, string appArgs, string callerId,
        int timeoutSeconds = 30, CancellationToken ct = default)
    {
        var channelId = $"chan-{++_channelSeq}";
        Originates.Add((endpoint, appArgs, callerId, channelId));
        return Task.FromResult(channelId);
    }
}

/// <summary>Vervangt Piper in de tests: registreert synthese-aanroepen en kan succes/fout simuleren.</summary>
public sealed class FakeTtsService : ITtsService
{
    public bool Enabled { get; set; } = true;
    public bool Succeed { get; set; } = true;
    public List<(string Text, string Voice, string Output)> Calls { get; } = [];
    private readonly HashSet<string> _existing = [];

    public bool IsEnabled => Enabled;
    public IReadOnlyList<string> AvailableVoices => ["nl_NL-pim-medium", "nl_NL-ronnie-medium"];
    public string DefaultVoice => "nl_NL-pim-medium";
    public bool OutputExists(string outputName) => _existing.Contains(outputName);

    public Task<bool> SynthesizeAsync(string text, string voice, string outputName, CancellationToken ct = default)
    {
        Calls.Add((text, voice, outputName));
        var ok = Enabled && Succeed;
        if (ok) _existing.Add(outputName);
        return Task.FromResult(ok);
    }
}
