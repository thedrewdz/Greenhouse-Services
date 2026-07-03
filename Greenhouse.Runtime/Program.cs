using Greenhouse.Bluetooth;
using Greenhouse.Core.Configuration;
using Greenhouse.Core.Setup;
using Greenhouse.Mqtt;
using Greenhouse.Network;
using Greenhouse.Storage;
using Greenhouse.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Loopback-only binding — the UI runs on the same device; never expose an external interface.
// Port matches the http profile in Properties/launchSettings.json; do not bind 0.0.0.0.
builder.WebHost.UseUrls("http://127.0.0.1:5150");

builder.Services.AddControllers();

// OpenAPI — the daemon publishes the contract the WebUI client is generated from
// (AGENTS.md non-negotiable). Keep it accurate and versioned with behavior changes.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// EF Core (SQLite) — a single persistent connection is kept open for the process lifetime.
// This ensures EF Core's migration executor sees schema changes (e.g. __EFMigrationsHistory)
// across all commands without relying on SQLite's schema cache being refreshed between
// connection pool checkouts, which is unreliable on Linux ARM64 with WAL mode.
var sqliteConnection = new SqliteConnection(builder.Configuration.GetConnectionString("Default"));
sqliteConnection.Open();
builder.Services.AddDbContext<GreenhouseDbContext>(o => o.UseSqlite(sqliteConnection));

// Repositories (scoped — share the request DbContext)
builder.Services.AddScoped<IMainConfigRepository, MainConfigRepository>();
builder.Services.AddScoped<IWifiCredentialsRepository, WifiCredentialsRepository>();

// OS network connector (registers INetworkConnector -> NmcliNetworkAdapter)
builder.Services.AddGreenhouseNetwork();

// MQTT messaging — the same singleton is IMessagingService and the IHostedService that connects
// at startup and reconnects on disconnect. Broker settings come from the "Mqtt" configuration
// section, never hardcoded.
builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection(MqttOptions.SectionName));
builder.Services.AddGreenhouseMqtt();

// BLE onboarding — registers the internal IBleTransport and the IEdgeUnitProvisioningTransport
// port. No BLE scan starts at process startup; scanning begins only when an onboarding use case
// calls the port.
builder.Services.AddGreenhouseBluetooth();

// Clock (used by WriteMainConfig)
builder.Services.AddSingleton(TimeProvider.System);

// Use cases (transient)
builder.Services.AddTransient<WriteMainConfig>();
builder.Services.AddTransient<ReadMainConfig>();
builder.Services.AddTransient<ConnectToNetwork>();
builder.Services.AddTransient<GetWifiCredentials>();
builder.Services.AddTransient<ReadSetupStatus>();

var app = builder.Build();

// Migrate before serving traffic so the schema exists on a clean host first start.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<GreenhouseDbContext>()
        .Database.MigrateAsync();
}

// Publish OpenAPI. No app.UseHttpsRedirection() — loopback stays plain HTTP (AGENTS.md).
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.Run();
