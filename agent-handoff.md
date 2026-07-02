# Agent Handoff

This file is for local, time-bound session state only.

Durable policy, canonical context, architecture, MQTT contracts, ADRs, and skill guidance live in the Greenhouse Documentation repository:

- https://github.com/thedrewdz/Greenhouse-Documentation/blob/main/README.md

## Current Workspace State

- Repository purpose: Greenhouse Main Unit services (headless C#/.NET brain).
- Status: repository scaffolded for agentic assistants (AGENTS.md, CLAUDE.md, skill packs, ADR index). No application code yet.

## Current Progress Snapshot

- Solution scaffolded (12 projects) + new `Greenhouse.Network` project added.
- Epic #7 (Main Unit Setup — Services) implemented through the code layers:
  - #8 EF Core entities (`MainConfigEntity`, `WifiCredentialsEntity`, internal to Storage),
    `GreenhouseDbContext`, design-time factory, `InitialCreate` migration. EF Core 8.0.28.
  - #9 `IMainConfigRepository` / `IWifiCredentialsRepository` (Core.Configuration) + EF
    implementations (Storage.Repositories); `WifiCredentials` upsert stamps `SavedAt`.
  - #10 `INetworkConnector` port + `ConnectResult` + `NetworkConnectorUnavailableException`
    (Core.Networking); `NmcliNetworkAdapter` (new `Greenhouse.Network`) over an
    `INmcliCommandRunner` seam; `AddGreenhouseNetwork()` extension.
  - #11 use cases (Core.Setup): WriteMainConfig, ReadMainConfig, ConnectToNetwork,
    GetWifiCredentials, ReadSetupStatus; `MainConfigValidation` shared with the API.
  - #12/#13/#14 controllers (Api): MainConfig (GET/POST + PUT/DELETE 501), WifiConfig
    (GET/POST), Setup (GET status) + DTOs and the `validation-error` envelope.
- Tests green: Api 20, Core 16, Network 11, Storage 8. Full solution builds clean.
- Chose bare-minimum logging (Pi storage, no cloud sink): adapter logs only on
  failure/timeout, never the password.

## Open Questions

- Adapter naming reconciliation pending in canonical docs: implemented as
  `NmcliNetworkAdapter`; spec/#22 say `NetworkManagerAdapter`, #10 said
  `NmcliNetworkConnectorAdapter`. Doc-update pass needed.

## Next Actions

- #37 (new sub-issue of #7): wire the Setup services into `Program.cs` + run EF migrations at
  startup; add `Greenhouse.Runtime` -> `Greenhouse.Network` reference; add
  `ConnectionStrings:Default` to appsettings. This is the only remaining #7 item and is
  hardware/host-dependent for full verification.
- Storage skill (`dotnet-storage-and-persistence.md`) says "WiFi credentials are not stored
  in the app database" — contradicts the ready-for-dev spec (WifiCredentials table). Flag for
  a documentation-repo update.

## Resume Prompt

```text
Read AGENTS.md, agent-handoff.md, and the Greenhouse Documentation README, then continue Main Unit services work using the relevant canonical architecture, device model, and MQTT documentation. Honor canonical ADRs 0001 and 0002.
```
