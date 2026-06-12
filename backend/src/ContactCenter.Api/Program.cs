using System.Net.Http.Headers;
using System.Text;
using ContactCenter.Api.Ari;
using ContactCenter.Api.CallFlow;
using ContactCenter.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Dev-gemak: één parameter (--VmHost=x.x.x.x) wijst zowel ARI als de
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

var connectionString = builder.Configuration.GetConnectionString("ContactCenter");
if (!string.IsNullOrWhiteSpace(vmHost))
    connectionString = new NpgsqlConnectionStringBuilder(connectionString) { Host = vmHost }.ConnectionString;

builder.Services.AddDbContextFactory<CcDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.AddSingleton<QueueDecisionService>();
builder.Services.AddSingleton<InboundCallHandler>();
builder.Services.AddHostedService<AriEventListener>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

await DatabaseInitializer.InitializeAsync(app.Services);

app.Run();
