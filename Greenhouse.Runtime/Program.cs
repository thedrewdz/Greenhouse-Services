using Greenhouse.Api.Hubs;
using Greenhouse.Bluetooth;
using Greenhouse.Core.Configuration;
using Greenhouse.Core.EdgeUnits;
using Greenhouse.Core.Onboarding;
using Greenhouse.Core.Setup;
using Greenhouse.Mqtt;
using Greenhouse.Network;
using Greenhouse.Runtime.HostedServices;
using Greenhouse.Storage;
using Greenhouse.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Loopback-only binding — the UI runs on the same device; never expose an external interface.
// Port matches the http profile in Properties/launchSettings.json; do not bind 0.0.0.0.
builder.WebHost.UseUrls("http://127.0.0.1:5150");

builder.Services.AddControllers();

// SignalR carries the onboarding observation channel at /hubs/onboarding. It is push-only:
// the REST resources remain the authoritative source of onboarding state.
builder.Services.AddSignalR();

// OpenAPI — the daemon publishes the contract the WebUI client is generated from
// (AGENTS.md non-negotiable). Keep it accurate and versioned with behavior changes.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// EF Core (SQLite) — a single persistent connection is kept open for the process lifetime.
// This ensures EF Core's migration executor sees schema changes (e.g. __EFMigrationsHistory)
// across all commands without relying on SQLite's schema cache being refreshed between
// connection pool checkouts, which is unreliable on Linux ARM64 with WAL mode.
//
// Owned here rather than by the container: DI only disposes what it creates itself, so an
// AddSingleton(instance) registration would never be closed. `await using` on a top-level
// statement compiles to a try/finally around app.Run(), so it closes on a faulted host too.
await using var sqliteConnection = new SqliteConnection(builder.Configuration.GetConnectionString("Default"));
await sqliteConnection.OpenAsync();
var dbOptions = new DbContextOptionsBuilder<GreenhouseDbContext>()
    .UseSqlite(sqliteConnection)
    .Options;

// GreenhouseDatabase owns a short-lived context per operation and serialises them, so that one
// shared connection is never used concurrently — background heartbeat and configuration work now
// writes outside any request. It also lets repositories be singletons, which is what long-lived
// services need to depend on them without capturing a request scope.
// Registered as a factory, not an instance, so the container disposes it with the host.
builder.Services.AddSingleton(_ => new GreenhouseDatabase(dbOptions));

// Repositories
builder.Services.AddSingleton<IMainConfigRepository, MainConfigRepository>();
builder.Services.AddSingleton<IWifiCredentialsRepository, WifiCredentialsRepository>();
builder.Services.AddSingleton<IEdgeUnitRepository, EdgeUnitRepository>();
builder.Services.AddSingleton<IOnboardingSessionRepository, OnboardingSessionRepository>();

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

// Clock (used by WriteMainConfig and the onboarding/publish timeouts)
builder.Services.AddSingleton(TimeProvider.System);

// Onboarding workflow — a singleton because a session's scan and provisioning tasks outlive the
// request that started them. Its notifier is the SignalR hub adapter.
builder.Services.AddSingleton(OnboardingTimeouts.Default);
builder.Services.AddSingleton<IOnboardingNotifier, SignalROnboardingNotifier>();
builder.Services.AddSingleton<IOnboardingWorkflow, OnboardingWorkflow>();

// Runtime configuration publishing — one background pump owns publish, ack correlation, and the
// bounded retry budget for every Edge Unit.
builder.Services.AddSingleton(ConfigurationPublishPolicy.Default);
builder.Services.AddSingleton<EdgeUnitConfigurationPublisher>();
builder.Services.AddSingleton<IEdgeUnitConfigurationPublisher>(
    provider => provider.GetRequiredService<EdgeUnitConfigurationPublisher>());
builder.Services.AddSingleton<ProcessHeartbeat>();

// Long-running message subscriptions, registered at startup rather than from request handlers.
builder.Services.AddHostedService<HeartbeatSubscriptionService>();
builder.Services.AddHostedService<EdgeUnitConfigurationService>();

// Use cases (transient)
builder.Services.AddTransient<WriteMainConfig>();
builder.Services.AddTransient<ReadMainConfig>();
builder.Services.AddTransient<ConnectToNetwork>();
builder.Services.AddTransient<GetWifiCredentials>();
builder.Services.AddTransient<ReadSetupStatus>();
builder.Services.AddTransient<UpdateEdgeUnitMapping>();

var app = builder.Build();

// Migrate before serving traffic so the schema exists on a clean host first start.
await using (var migrationContext = new GreenhouseDbContext(dbOptions))
{
    await migrationContext.Database.MigrateAsync();
}

// Publish OpenAPI. No app.UseHttpsRedirection() — loopback stays plain HTTP (AGENTS.md).
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapHub<OnboardingHub>(OnboardingHub.Path);
app.Run();
