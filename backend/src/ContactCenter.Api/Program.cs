using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ContactCenter.Api.Admin;
using ContactCenter.Api.Agents;
using ContactCenter.Api.Ari;
using ContactCenter.Api.Auth;
using ContactCenter.Api.CallFlow;
using ContactCenter.Api.Data;
using ContactCenter.Api.Directory;
using ContactCenter.Api.Realtime;
using ContactCenter.Api.Tts;
using ContactCenter.Api.Turn;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Lokale secrets buiten git: appsettings.Local.json (gitignored, zie .example) wordt geladen als
// het bestaat — in élke omgeving, dus ook bij 'dotnet run' (default Production). In de container
// ontbreekt dit bestand en komen de secrets uit env-variabelen (docker-compose → infra/.env).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Dev-gemak: één parameter (--VmHost=x.x.x.x) wijst ARI, de database en
// Keycloak naar de VM; de rest komt uit appsettings.json.
var vmHost = builder.Configuration["VmHost"];
if (!string.IsNullOrWhiteSpace(vmHost))
{
    builder.Configuration["Ari:BaseUrl"] = $"http://{vmHost}:8088/ari/";
    builder.Configuration["Keycloak:BaseUrl"] = $"http://{vmHost}:8080";
    // coturn draait op de VM; vul de TURN-host in tenzij expliciet gezet (bv. in appsettings.Local.json).
    if (string.IsNullOrWhiteSpace(builder.Configuration["Turn:Host"]))
        builder.Configuration["Turn:Host"] = vmHost;
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

// TURN/STUN voor WebRTC-agents achter NAT (coturn). Leeg geheim = uit (terugval op host-kandidaten).
builder.Services.AddOptions<TurnOptions>().Bind(builder.Configuration.GetSection(TurnOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TurnCredentialService>();

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
builder.Services.AddSingleton<ITtsService, PiperTtsService>();
builder.Services.AddHostedService<AriEventListener>();

// Keycloak (OIDC) — valideert de JWT's van ZetaDesk/ZetaBeheer. Dev: http (geen TLS).
// Multi-tenant: elke klant heeft een eigen realm; de issuer in het token bepaalt de realm en
// daarmee de tenant. De backend haalt de JWKS per realm dynamisch op bij Keycloak:BaseUrl
// (in de container: localhost), terwijl de token-issuer het publieke IP kan zijn — de issuer
// wordt op realm-lidmaatschap gevalideerd (via de tenant-registry), niet op exacte host, dus
// de NAT-hairpin is vanzelf afgedekt.
var keycloakOptions = new KeycloakOptions
{
    BaseUrl = builder.Configuration["Keycloak:BaseUrl"] ?? "http://localhost:8080",
    AllowedClients = builder.Configuration.GetSection("Keycloak:AllowedClients").Get<string[]>()
        ?? ["zetadesk", "zetabeheer"],
};
builder.Services.AddSingleton(keycloakOptions);
builder.Services.AddSingleton<KeycloakRealmKeys>();
builder.Services.AddSingleton<ITenantAccessor, TenantAccessor>();
builder.Services.AddSingleton<ITenantRegistry, TenantRegistry>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<KeycloakRealmKeys, ITenantRegistry>((options, keys, registry) =>
    {
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            IssuerValidator = (issuer, _, _) =>
            {
                var realm = KeycloakRealmKeys.RealmFromIssuer(issuer);
                if (realm is not null && registry.TryGetByRealm(realm, out _))
                    return issuer;
                throw new SecurityTokenInvalidIssuerException($"Onbekende realm voor issuer '{issuer}'.");
            },
            ValidateIssuerSigningKey = true,
            IssuerSigningKeyResolver = (_, securityToken, _, _) =>
            {
                var realm = KeycloakRealmKeys.RealmFromIssuer(securityToken.Issuer);
                return realm is null ? [] : keys.SigningKeys(realm);
            },
            ValidateAudience = false, // Keycloak-tokens hebben standaard aud "account"
            NameClaimType = "preferred_username",
            RoleClaimType = ClaimTypes.Role,
        };
        options.Events = new JwtBearerEvents
        {
            // SignalR stuurt het token mee via de query-string bij de WebSocket-handshake.
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hub"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
            // H-3: audience-controle via azp. Keycloak's default-aud ("account") is nutteloos, dus
            // valideren we dat het token is uitgegeven aan één van onze eigen clients. Zonder dit zou
            // elk realm-token met de juiste rol (ook van een andere client) geaccepteerd worden.
            OnTokenValidated = context =>
            {
                var azp = context.Principal?.FindFirstValue("azp");
                if (azp is null || !keycloakOptions.AllowedClients.Contains(azp, StringComparer.Ordinal))
                    context.Fail($"Token niet uitgegeven aan een toegestane client (azp '{azp}').");
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

// M-6: toegestane CORS-origins uit config (env Cors__AllowedOrigins__0=...), default de lokale
// front-end-poorten. SignalR vereist AllowCredentials, dus geen wildcard-origin mogelijk.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:5174"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(corsOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials()));

// M-4: rate-limiting (defense-in-depth naast Keycloaks brute-force-detectie). Per gebruiker
// (of IP bij anonieme calls) een ruime vaste-venster-limiet; de SignalR-hub en /health blijven
// ongelimiteerd (langlopend/hoogfrequent-legitiem). 429 bij overschrijding.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
    {
        var path = http.Request.Path;
        if (path.StartsWithSegments("/hub") || path.StartsWithSegments("/health"))
            return RateLimitPartition.GetNoLimiter("unlimited");
        var key = http.User.Identity?.Name ?? http.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromSeconds(10),
        });
    });
});

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>(); // herleidt de tenant uit de token-issuer (realm)
app.UseAuthorization();
app.UseRateLimiter(); // M-4: na authenticatie, zodat per-gebruiker gepartitioneerd kan worden

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

agents.MapGet("", async (AgentStateService svc, ITenantAccessor tenant, CancellationToken ct)
    => Results.Ok(await svc.GetAllAsync(tenant.TenantId!.Value, ct)));

// Het SIP-wachtwoord voor de ingelogde agent (afgeleid van het token), voor de softphone-registratie.
agents.MapGet("/me/sip", async (HttpContext http, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
{
    var username = http.User.Identity?.Name;
    if (string.IsNullOrEmpty(username)) return Results.Unauthorized();
    await using var db = await factory.CreateDbContextAsync(ct);
    // tenant-gescoped via de query-filter (tenant-context staat na de middleware).
    var agent = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Name == username, ct);
    return agent is null
        ? Results.NotFound()
        : Results.Ok(new { username = agent.Name, password = agent.SipPassword });
});

// ICE-servers (STUN/TURN) voor de WebRTC-registratie van de ingelogde agent. Leeg wanneer TURN
// niet is geconfigureerd; de credentials zijn tijdelijk (per agent, uit het gedeelde geheim).
agents.MapGet("/me/ice", (HttpContext http, TurnCredentialService turn) =>
{
    var user = http.User.Identity?.Name;
    return string.IsNullOrEmpty(user)
        ? Results.Unauthorized()
        : Results.Ok(new { iceServers = turn.GetIceServers(user) });
});

agents.MapGet("/{name}", async (string name, AgentStateService svc, ITenantAccessor tenant, CancellationToken ct)
    => await svc.GetAsync(tenant.TenantId!.Value, name, ct) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());

agents.MapPost("/{name}/login", async (string name, AgentStateService svc, ITenantAccessor tenant, CancellationToken ct)
    => await svc.LoginAsync(tenant.TenantId!.Value, name, ct) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());

agents.MapPost("/{name}/logout", async (string name, AgentStateService svc, ITenantAccessor tenant, CancellationToken ct)
    => await svc.LogoutAsync(tenant.TenantId!.Value, name, ct) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());

agents.MapPost("/{name}/wrapup/finish", async (string name, AgentStateService svc, ITenantAccessor tenant, CancellationToken ct)
    => await svc.FinishWrapUpAsync(tenant.TenantId!.Value, name, ct) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());

agents.MapPost("/{name}/presence",
    async (string name, PresenceRequest req, AgentStateService svc, ITenantAccessor tenant, CancellationToken ct)
        => await svc.SetPresenceAsync(tenant.TenantId!.Value, name, req.Presence, ct) is { } snapshot
            ? Results.Ok(snapshot)
            : Results.NotFound());

// Handmatig een specifiek wachtend gesprek aannemen. 409 als het al weg is of de agent bezet is.
agents.MapPost("/{name}/calls/{callId}/pickup",
    async (string name, string callId, CallCoordinator calls, ITenantAccessor tenant, CancellationToken ct)
        => await calls.PickupAsync(tenant.TenantId!.Value, name, callId, ct) ? Results.Ok() : Results.Conflict());

agents.MapPost("/{name}/hold", async (string name, CallCoordinator calls, ITenantAccessor tenant, CancellationToken ct)
    => await calls.HoldAsync(tenant.TenantId!.Value, name, ct) ? Results.Ok(new { onHold = true }) : Results.NotFound());

agents.MapPost("/{name}/unhold", async (string name, CallCoordinator calls, ITenantAccessor tenant, CancellationToken ct)
    => await calls.UnholdAsync(tenant.TenantId!.Value, name, ct) ? Results.Ok(new { onHold = false }) : Results.NotFound());

agents.MapPost("/{name}/transfer/cold",
    async (string name, TransferRequest req, CallCoordinator calls, ITenantAccessor tenant, CancellationToken ct)
        => await calls.ColdTransferAsync(tenant.TenantId!.Value, name, req.Target, ct) ? Results.Ok() : Results.NotFound());

agents.MapPost("/{name}/transfer/agent",
    async (string name, AgentTransferRequest req, CallCoordinator calls, ITenantAccessor tenant, CancellationToken ct)
        => await calls.TransferToAgentAsync(tenant.TenantId!.Value, name, req.Agent, ct) ? Results.Ok() : Results.Conflict());

// Warm doorverbinden (overleg): starten met een collega, daarna voltooien of annuleren.
agents.MapPost("/{name}/transfer/warm",
    async (string name, AgentTransferRequest req, CallCoordinator calls, ITenantAccessor tenant, CancellationToken ct)
        => await calls.StartWarmTransferAsync(tenant.TenantId!.Value, name, req.Agent, ct) ? Results.Ok() : Results.Conflict());

agents.MapPost("/{name}/transfer/warm/complete",
    async (string name, CallCoordinator calls, ITenantAccessor tenant, CancellationToken ct)
        => await calls.CompleteWarmTransferAsync(tenant.TenantId!.Value, name, ct) ? Results.Ok() : Results.Conflict());

agents.MapPost("/{name}/transfer/warm/cancel",
    async (string name, CallCoordinator calls, ITenantAccessor tenant, CancellationToken ct)
        => await calls.CancelWarmTransferAsync(tenant.TenantId!.Value, name, ct) ? Results.Ok() : Results.Conflict());

// Zoeken naar doorverbind-bestemmingen (collega-agents + contacten).
app.MapGet("/api/directory/search",
    async (string? q, string? exclude, DirectoryService directory, CancellationToken ct)
        => Results.Ok(await directory.SearchAsync(q, exclude, ct))).RequireAuthorization();

// Wachtrij-overzicht: initiële stand; live updates lopen via de SignalR-hub.
app.MapGet("/api/queues", async (CallCoordinator calls, ITenantAccessor tenant, CancellationToken ct)
    => Results.Ok(await calls.GetWaitingViewAsync(tenant.TenantId!.Value, ct))).RequireAuthorization();

// Beheer-API (ZetaBeheer): CRUD op de configuratie — vereist de rol 'admin' (zie AdminApi).
app.MapAdminApi();

app.MapHub<ContactCenterHub>("/hub").RequireAuthorization();

// Agent-status live naar de eigen schermen pushen (i.p.v. pollen): koppel de statusmachine aan de notifier.
var agentState = app.Services.GetRequiredService<AgentStateService>();
var realtimeNotifier = app.Services.GetRequiredService<IRealtimeNotifier>();
agentState.AgentChanged = (tenantId, snapshot) => realtimeNotifier.AgentChangedAsync(tenantId, snapshot);

await DatabaseInitializer.InitializeAsync(app.Services);
await app.Services.GetRequiredService<ITenantRegistry>().ReloadAsync();

app.Run();

internal sealed record TransferRequest(string Target);
internal sealed record AgentTransferRequest(string Agent);
internal sealed record PresenceRequest(Presence Presence);
