using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using ContactCenter.Api.Admin;
using ContactCenter.Api.Agents;
using ContactCenter.Api.Ari;
using ContactCenter.Api.Auth;
using ContactCenter.Api.CallFlow;
using ContactCenter.Api.Data;
using ContactCenter.Api.Directory;
using ContactCenter.Api.Realtime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Dev-gemak: één parameter (--VmHost=x.x.x.x) wijst ARI, de database en
// Keycloak naar de VM; de rest komt uit appsettings.json.
var vmHost = builder.Configuration["VmHost"];
if (!string.IsNullOrWhiteSpace(vmHost))
{
    builder.Configuration["Ari:BaseUrl"] = $"http://{vmHost}:8088/ari/";
    builder.Configuration["Keycloak:Authority"] = $"http://{vmHost}:8080/realms/contactcenter";
}

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

// Keycloak (OIDC) — valideert de JWT's van ZetaDesk/ZetaBeheer. Dev: http (geen TLS).
// Authority = waar de backend de metadata/JWKS ophaalt (in de container: localhost).
// ValidIssuer (optioneel) = de issuer in het token (in de container: het publieke IP,
// want de browser haalt het token daar). We accepteren beide, zodat metadata-ophalen lokaal
// kan terwijl het token-issuer publiek is — geen NAT-hairpin nodig.
var keycloakAuthority = builder.Configuration["Keycloak:Authority"];
var keycloakValidIssuer = builder.Configuration["Keycloak:ValidIssuer"];
var validIssuers = string.IsNullOrWhiteSpace(keycloakValidIssuer)
    ? new[] { keycloakAuthority! }
    : new[] { keycloakAuthority!, keycloakValidIssuer };
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = keycloakAuthority;
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = validIssuers,
            ValidateAudience = false, // Keycloak-tokens hebben standaard aud "account"
            NameClaimType = "preferred_username",
            RoleClaimType = ClaimTypes.Role,
        };
        // SignalR stuurt het token mee via de query-string bij de WebSocket-handshake.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hub"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddSingleton<IClaimsTransformation, KeycloakRolesTransformation>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("agent", p => p.RequireRole("agent"));
    options.AddPolicy("admin", p => p.RequireRole("admin"));
});

// dev: de front-ends draaien op vaste localhost-poorten; SignalR vereist AllowCredentials.
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173", "http://localhost:5174")
        .AllowAnyMethod().AllowAnyHeader().AllowCredentials()));

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Agent-API: vereist een geldig token met de rol 'agent'; een agent mag bovendien
// alleen handelingen op zijn eigen {name} uitvoeren (controle via endpoint-filter).
var agents = app.MapGroup("/api/agents").RequireAuthorization("agent");
agents.AddEndpointFilter(async (ctx, next) =>
{
    if (ctx.HttpContext.Request.RouteValues.TryGetValue("name", out var nameObj)
        && nameObj is string routeName
        && !string.Equals(routeName, ctx.HttpContext.User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Forbid();
    }
    return await next(ctx);
});

agents.MapGet("", async (AgentStateService svc, CancellationToken ct)
    => Results.Ok(await svc.GetAllAsync(ct)));

// Het SIP-wachtwoord voor de ingelogde agent (afgeleid van het token), voor de softphone-registratie.
agents.MapGet("/me/sip", async (HttpContext http, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
{
    var username = http.User.Identity?.Name;
    if (string.IsNullOrEmpty(username)) return Results.Unauthorized();
    await using var db = await factory.CreateDbContextAsync(ct);
    var agent = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Name == username, ct);
    return agent is null
        ? Results.NotFound()
        : Results.Ok(new { username = agent.Name, password = agent.SipPassword });
});

agents.MapGet("/{name}", async (string name, AgentStateService svc, CancellationToken ct)
    => await svc.GetAsync(name, ct) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());

agents.MapPost("/{name}/login", async (string name, AgentStateService svc, CancellationToken ct)
    => await svc.LoginAsync(name, ct) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());

agents.MapPost("/{name}/logout", async (string name, AgentStateService svc, CancellationToken ct)
    => await svc.LogoutAsync(name, ct) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());

agents.MapPost("/{name}/wrapup/finish", async (string name, AgentStateService svc, CancellationToken ct)
    => await svc.FinishWrapUpAsync(name, ct) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());

agents.MapPost("/{name}/presence",
    async (string name, PresenceRequest req, AgentStateService svc, CancellationToken ct)
        => await svc.SetPresenceAsync(name, req.Presence, ct) is { } snapshot
            ? Results.Ok(snapshot)
            : Results.NotFound());

// Handmatig een specifiek wachtend gesprek aannemen. 409 als het al weg is of de agent bezet is.
agents.MapPost("/{name}/calls/{callId}/pickup",
    async (string name, string callId, CallCoordinator calls, CancellationToken ct)
        => await calls.PickupAsync(name, callId, ct) ? Results.Ok() : Results.Conflict());

agents.MapPost("/{name}/hold", async (string name, CallCoordinator calls, CancellationToken ct)
    => await calls.HoldAsync(name, ct) ? Results.Ok(new { onHold = true }) : Results.NotFound());

agents.MapPost("/{name}/unhold", async (string name, CallCoordinator calls, CancellationToken ct)
    => await calls.UnholdAsync(name, ct) ? Results.Ok(new { onHold = false }) : Results.NotFound());

agents.MapPost("/{name}/transfer/cold",
    async (string name, TransferRequest req, CallCoordinator calls, CancellationToken ct)
        => await calls.ColdTransferAsync(name, req.Target, ct) ? Results.Ok() : Results.NotFound());

agents.MapPost("/{name}/transfer/agent",
    async (string name, AgentTransferRequest req, CallCoordinator calls, CancellationToken ct)
        => await calls.TransferToAgentAsync(name, req.Agent, ct) ? Results.Ok() : Results.Conflict());

// Warm doorverbinden (overleg): starten met een collega, daarna voltooien of annuleren.
agents.MapPost("/{name}/transfer/warm",
    async (string name, AgentTransferRequest req, CallCoordinator calls, CancellationToken ct)
        => await calls.StartWarmTransferAsync(name, req.Agent, ct) ? Results.Ok() : Results.Conflict());

agents.MapPost("/{name}/transfer/warm/complete",
    async (string name, CallCoordinator calls, CancellationToken ct)
        => await calls.CompleteWarmTransferAsync(name, ct) ? Results.Ok() : Results.Conflict());

agents.MapPost("/{name}/transfer/warm/cancel",
    async (string name, CallCoordinator calls, CancellationToken ct)
        => await calls.CancelWarmTransferAsync(name, ct) ? Results.Ok() : Results.Conflict());

// Zoeken naar doorverbind-bestemmingen (collega-agents + contacten).
app.MapGet("/api/directory/search",
    async (string? q, string? exclude, DirectoryService directory, CancellationToken ct)
        => Results.Ok(await directory.SearchAsync(q, exclude, ct))).RequireAuthorization();

// Wachtrij-overzicht: initiële stand; live updates lopen via de SignalR-hub.
app.MapGet("/api/queues", async (CallCoordinator calls, CancellationToken ct)
    => Results.Ok(await calls.GetWaitingViewAsync(ct))).RequireAuthorization();

// Beheer-API (ZetaBeheer): CRUD op de configuratie — vereist de rol 'admin' (zie AdminApi).
app.MapAdminApi();

app.MapHub<ContactCenterHub>("/hub").RequireAuthorization();

await DatabaseInitializer.InitializeAsync(app.Services);

app.Run();

internal sealed record TransferRequest(string Target);
internal sealed record AgentTransferRequest(string Agent);
internal sealed record PresenceRequest(Presence Presence);
