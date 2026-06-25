using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using ContactCenter.Api.Admin;
using ContactCenter.Api.Agents;
using ContactCenter.Api.Ari;
using ContactCenter.Api.CallFlow;
using ContactCenter.Api.Data;
using ContactCenter.Api.Directory;
using ContactCenter.Api.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Dev-gemak: één parameter (--VmHost=x.x.x.x) wijst ARI en de
// database naar de VM; de rest komt uit appsettings.json.
var vmHost = builder.Configuration["VmHost"];
if (!string.IsNullOrWhiteSpace(vmHost))
    builder.Configuration["Ari:BaseUrl"] = $"http://{vmHost}:8088/ari/";

builder.Services.AddOptions<AriOptions>()
    .Bind(builder.Configuration.GetSection(AriOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "Ari:BaseUrl is verplicht")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Username), "Ari:Username is verplicht")
    .ValidateOnStart();

builder.Services.AddHttpClient<AriHttpClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<AriOptions>>().Value;
    http.BaseAddress = new Uri(opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/");
    http.Timeout = TimeSpan.FromSeconds(10);
    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opts.Username}:{opts.Password}"));
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
});
builder.Services.AddSingleton<IAriClient>(sp => sp.GetRequiredService<AriHttpClient>());

var connectionString = builder.Configuration.GetConnectionString("ContactCenter");
if (!string.IsNullOrWhiteSpace(vmHost))
    connectionString = new NpgsqlConnectionStringBuilder(connectionString) { Host = vmHost }.ConnectionString;

builder.Services.AddDbContextFactory<CcDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();
builder.Services.AddSingleton<AgentStateService>();
builder.Services.AddSingleton<CallCoordinator>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CallCoordinator>());
builder.Services.AddSingleton<QueueDecisionService>();
builder.Services.AddSingleton<DirectoryService>();
builder.Services.AddSingleton<InboundCallHandler>();
builder.Services.AddHostedService<AriEventListener>();

// dev: front-end draait op een andere localhost-poort; SignalR vereist AllowCredentials,
// dus reflecteer de origin (niet AllowAnyOrigin). Aanscherpen met Keycloak.
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials()));

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Agent-API: nog zonder authenticatie (Keycloak volgt in een latere fase)
app.MapGet("/api/agents", async (AgentStateService agents, CancellationToken ct)
    => Results.Ok(await agents.GetAllAsync(ct)));

app.MapGet("/api/agents/{name}", async (string name, AgentStateService agents, CancellationToken ct)
    => await agents.GetAsync(name, ct) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());

app.MapPost("/api/agents/{name}/login", async (string name, AgentStateService agents, CancellationToken ct)
    => await agents.LoginAsync(name, ct) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());

app.MapPost("/api/agents/{name}/logout", async (string name, AgentStateService agents, CancellationToken ct)
    => await agents.LogoutAsync(name, ct) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());

app.MapPost("/api/agents/{name}/wrapup/finish", async (string name, AgentStateService agents, CancellationToken ct)
    => await agents.FinishWrapUpAsync(name, ct) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());

app.MapPost("/api/agents/{name}/presence",
    async (string name, PresenceRequest req, AgentStateService agents, CancellationToken ct)
        => await agents.SetPresenceAsync(name, req.Presence, ct) is { } snapshot
            ? Results.Ok(snapshot)
            : Results.NotFound());

// Handmatig een specifiek wachtend gesprek aannemen. 409 als het al weg is of de agent bezet is.
app.MapPost("/api/agents/{name}/calls/{callId}/pickup",
    async (string name, string callId, CallCoordinator calls, CancellationToken ct)
        => await calls.PickupAsync(name, callId, ct) ? Results.Ok() : Results.Conflict());

app.MapPost("/api/agents/{name}/hold", async (string name, CallCoordinator calls, CancellationToken ct)
    => await calls.HoldAsync(name, ct) ? Results.Ok(new { onHold = true }) : Results.NotFound());

app.MapPost("/api/agents/{name}/unhold", async (string name, CallCoordinator calls, CancellationToken ct)
    => await calls.UnholdAsync(name, ct) ? Results.Ok(new { onHold = false }) : Results.NotFound());

app.MapPost("/api/agents/{name}/transfer/cold",
    async (string name, TransferRequest req, CallCoordinator calls, CancellationToken ct)
        => await calls.ColdTransferAsync(name, req.Target, ct) ? Results.Ok() : Results.NotFound());

app.MapPost("/api/agents/{name}/transfer/agent",
    async (string name, AgentTransferRequest req, CallCoordinator calls, CancellationToken ct)
        => await calls.TransferToAgentAsync(name, req.Agent, ct) ? Results.Ok() : Results.Conflict());

// Warm doorverbinden (overleg): starten met een collega, daarna voltooien of annuleren.
app.MapPost("/api/agents/{name}/transfer/warm",
    async (string name, AgentTransferRequest req, CallCoordinator calls, CancellationToken ct)
        => await calls.StartWarmTransferAsync(name, req.Agent, ct) ? Results.Ok() : Results.Conflict());

app.MapPost("/api/agents/{name}/transfer/warm/complete",
    async (string name, CallCoordinator calls, CancellationToken ct)
        => await calls.CompleteWarmTransferAsync(name, ct) ? Results.Ok() : Results.Conflict());

app.MapPost("/api/agents/{name}/transfer/warm/cancel",
    async (string name, CallCoordinator calls, CancellationToken ct)
        => await calls.CancelWarmTransferAsync(name, ct) ? Results.Ok() : Results.Conflict());

// Zoeken naar doorverbind-bestemmingen (collega-agents + contacten).
app.MapGet("/api/directory/search",
    async (string? q, string? exclude, DirectoryService directory, CancellationToken ct)
        => Results.Ok(await directory.SearchAsync(q, exclude, ct)));

// Wachtrij-overzicht: initiële stand; live updates lopen via de SignalR-hub.
app.MapGet("/api/queues", async (CallCoordinator calls, CancellationToken ct)
    => Results.Ok(await calls.GetWaitingViewAsync(ct)));

// Beheer-API (ZetaBeheer): CRUD op de configuratie. Nog onbeveiligd — Keycloak volgt.
app.MapAdminApi();

app.MapHub<ContactCenterHub>("/hub");

await DatabaseInitializer.InitializeAsync(app.Services);

app.Run();

internal sealed record TransferRequest(string Target);
internal sealed record AgentTransferRequest(string Agent);
internal sealed record PresenceRequest(Presence Presence);
