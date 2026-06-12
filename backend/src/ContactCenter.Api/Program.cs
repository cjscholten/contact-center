using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using ContactCenter.Api.Agents;
using ContactCenter.Api.Ami;
using ContactCenter.Api.Ari;
using ContactCenter.Api.CallFlow;
using ContactCenter.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Dev-gemak: één parameter (--VmHost=x.x.x.x) wijst ARI, AMI en de
// database naar de VM; de rest komt uit appsettings.json.
var vmHost = builder.Configuration["VmHost"];
if (!string.IsNullOrWhiteSpace(vmHost))
{
    builder.Configuration["Ari:BaseUrl"] = $"http://{vmHost}:8088/ari/";
    builder.Configuration["Ami:Host"] = vmHost;
}

builder.Services.AddOptions<AriOptions>()
    .Bind(builder.Configuration.GetSection(AriOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "Ari:BaseUrl is verplicht")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Username), "Ari:Username is verplicht")
    .ValidateOnStart();

builder.Services.AddOptions<AmiOptions>()
    .Bind(builder.Configuration.GetSection(AmiOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Username), "Ami:Username is verplicht")
    .ValidateOnStart();

builder.Services.AddHttpClient<AriHttpClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<AriOptions>>().Value;
    http.BaseAddress = new Uri(opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/");
    http.Timeout = TimeSpan.FromSeconds(10);
    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opts.Username}:{opts.Password}"));
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
});

var connectionString = builder.Configuration.GetConnectionString("ContactCenter");
if (!string.IsNullOrWhiteSpace(vmHost))
    connectionString = new NpgsqlConnectionStringBuilder(connectionString) { Host = vmHost }.ConnectionString;

builder.Services.AddDbContextFactory<CcDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.AddSingleton<IQueueMemberControl, AmiClient>();
builder.Services.AddSingleton<AgentStateService>();
builder.Services.AddSingleton<QueueDecisionService>();
builder.Services.AddSingleton<InboundCallHandler>();
builder.Services.AddHostedService<AriEventListener>();

// dev: agent-pagina draait op een andere localhost-poort; aanscherpen met Keycloak
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

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

await DatabaseInitializer.InitializeAsync(app.Services);

app.Run();
