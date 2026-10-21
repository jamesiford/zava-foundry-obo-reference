# Local development

What you can build, test and run on a developer machine with **no Azure access**, and what needs a real tenant. For the tenant deployment see [`DEPLOY.md`](DEPLOY.md).

Evidence tags: **observed** (run and seen), **documented** (from Microsoft Learn or a package's docs, not run here), **open** (not known).

## Prerequisites

| Need | For | Check |
| --- | --- | --- |
| .NET 10 SDK | build and test | `dotnet --version` (the repo targets .NET 10) |
| Docker | the Aspire AppHost (Azurite is the Functions storage emulator) | `docker version` |
| Azure Functions Core Tools (`func`) | running the MCP server host | `func --version` |

`infra/scripts/Test-Prerequisites.ps1 -Mode Local` checks these for you and changes nothing.

## Build and test

```powershell
dotnet build Compass.slnx
dotnet test Compass.slnx
```

**Observed (21 Oct 2026, clean clone):** 0 build errors; 419 tests, 416 pass and 3 are skipped. The three skipped tests are the `Live_*` identity-isolation variants, which run only when `COMPASS_TEST_TENANT_CONFIGURED` is set and a real tenant is configured. The build shows 8 warnings: a `NU1902` advisory for a transitive `MessagePack` 2.5.302 from the Aspire Functions integration, `NU1603` resolving a pre-release `Azure.AI.Extensions.OpenAI` for the publish tool, and two nullable warnings in test code. They are visible, not suppressed.

| Test project | Covers |
| --- | --- |
| `Compass.Agents.Contracts.Tests` | Agent classes, header and token handling, JWT redaction (including the real OpenTelemetry pipeline) |
| `Compass.Bot.Application.Tests` | Person resolution, OBO commands, turn handler fail-closed mapping, session IDs; the per-turn scope (which agents and tokens a question uses, the route as a ceiling, unlinked members); the composer; hosted-agent gateway routing; configuration readiness |
| `Compass.Agents.Prompt.Tests` | The knowledge prompt agent (request shape, fail-closed token handling, citation annotations) and the Learn docs prompt agent definition |
| `Compass.Mcp.ZavaApi.Tests` | CQRS handlers against seeded SQLite; JWT validation (valid, expired, wrong audience, missing) |
| `Compass.IdentityIsolation.Tests` | The dev spec §12 cases 1 to 6 against fakes; the web front door (`/api/briefing`: 401 paths, per-person isolation, token-leak scan, claims challenge, body validation, page CSP guards); live variants of cases 1 and 2 |
| `Compass.Tools.PublishAgents.Tests` | The publish tool's request building |

## Run the offline pieces under Aspire

```powershell
dotnet run --project apphost/Compass.AppHost
```

This starts the Aspire dashboard, Azurite (a container), the MCP Functions host and the bot. **Observed on Windows:** the dashboard, the bot (`/health` returns 200), the Functions host (root returns 200; the MCP route answers 405 to a GET, which means it is registered) and the Azurite container all came up.

The AppHost does **not** run the Foundry agents. The Foundry project endpoint and the knowledge-base MCP endpoint are inert placeholder parameters (`foundry-project-endpoint`, `knowledge-base-mcp-endpoint`); override them with `Parameters:<name>` in user secrets or the environment to point at a deployed project. NuGet has preview `Aspire.Hosting.Foundry` packages whose method names include `AsHostedAgent`; what they do locally is **open** (not tried, see [`LIVE-FINDINGS.md`](LIVE-FINDINGS.md)).

Things to know:

- On first run `dotnet run` may sit at "Trusting certificates…" waiting for a dev-certificate prompt. Accept it, or run `dotnet dev-certs https --trust` once (the standard fix; **documented**, not tried here). The route that was **observed** to work is to bypass it:

  ```powershell
  $env:ASPIRE_ALLOW_UNSECURED_TRANSPORT = 'true'
  $env:ASPNETCORE_URLS = 'http://localhost:15019'
  $env:ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL = 'http://localhost:19197'
  $env:ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL = 'http://localhost:20106'
  dotnet apphost/Compass.AppHost/bin/Debug/net10.0/Compass.AppHost.dll   # after dotnet build
  ```

  The dashboard login URL, with a one-time token, is printed to the console.
- The MCP host needs token-validation settings before it can validate a request (options validation requires an authority, at least one issuer and one audience). Copy `src/Mcp/Compass.Mcp.ZavaApi.Host/local.settings.sample.json` to `local.settings.json` and set the `ZavaApi__Auth__*` values. The Entra app registrations come from `infra/scripts/Setup-EntraApps.ps1`.
- Sign-in, OBO and the SharePoint retrieval need a real tenant, so a full end-to-end turn is not possible locally. Use `dotnet test tests/Compass.IdentityIsolation.Tests` to exercise the whole flow against fakes.

## Run a hosted agent on your machine

A hosted agent is an ASP.NET host on port 8088 that speaks the Responses protocol. Locally:

```powershell
dotnet run --project src/Agents/Compass.Agents.Hosted.ZavaApi
powershell -NoProfile -ExecutionPolicy Bypass -File ./tools/Call-LocalAgent.ps1
```

`tools/Call-LocalAgent.ps1` posts a Responses request to `http://localhost:8088/responses` with the local identity header `x-agent-user-id`. The launch profile runs in `Production` on purpose: **observed** that the `Development` environment crashes the Foundry agent adapter at startup (see [`LIVE-FINDINGS.md`](LIVE-FINDINGS.md) and [`TROUBLESHOOTING.md`](TROUBLESHOOTING.md)). With no token the agent must fail closed (`status: failed`, code `missing_token`). Pass `-ZavaApiToken` to go further; the agent then calls the MCP server named by `ZavaApi__McpEndpoint` (default `http://localhost:7071/runtime/webhooks/mcp`), so run the Functions host first.

## Publishing agents

`tools/Compass.Tools.PublishAgents` publishes the hosted and prompt agent versions to a Foundry project through the data plane (Bicep cannot do this yet). `--what-if` prints the plan and exits, with no build, no network and no credential. See [`DEPLOY.md`](DEPLOY.md) for when it runs.
