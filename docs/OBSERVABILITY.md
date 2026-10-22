# Observability and token hygiene

What is instrumented, how tokens are kept out of telemetry, how that is tested, and what a reviewer should look for.
This describes what the code **does today**; anything the dev spec (§13) describes that is not built is called out
under [Not implemented](#not-implemented--known-limits).

## What is instrumented

| Host | Instrumentation | Where it goes |
| --- | --- | --- |
| `Compass.Bot.Host` | `AddServiceDefaults()` → OpenTelemetry logs (with scopes), metrics (ASP.NET Core, HttpClient, runtime) and traces (ASP.NET Core, HttpClient); `/health` and `/alive` in Development | OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (Aspire sets it); **Azure Monitor** when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set (infra sets it) |
| `Compass.Mcp.ZavaApi.Host` (Functions) | Same ServiceDefaults, plus `UseFunctionsWorkerDefaults()` | OTLP via Aspire; **Azure Monitor** when the connection string is set |
| Hosted agents (`Compass.Agents.Hosted.*`) | Redaction processors in our code; the hosting library (`Azure.AI.AgentServer.Responses`) bundles the OpenTelemetry distro and exports to the project's Application Insights **once the project has one connected** (Foundry injects the connection string). No exporter in our code | Application Insights, after connecting it to the Foundry project (19 Oct); nowhere before |

Locally, run the AppHost and open the Aspire dashboard to see the bot and the MCP host's logs, traces and metrics side by
side. In Azure, Application Insights (workspace-based, created by `infra/modules/appInsights.bicep`) receives the bot
and MCP host telemetry.

Since M9 each turn's three agent calls run inside a Microsoft Agent Framework workflow (Compass.Bot.Orchestration) and the
Foundry calls go through the Foundry SDK client; both emit through the bot's existing OpenTelemetry pipeline, so the
redaction processors below also cover the workflow's executor events and the SDK's HTTP spans. The knowledge agent runs
in the bot process, so it logs through the bot host rather than a separate agent host.

## The token-redaction processors

`Compass.Shared.Observability` (`AddTokenRedaction()`), registered in the bot host, the MCP host and both agent hosts:

- **`JwtRedactor`** matches the compact JWT shape — three base64url segments, header beginning `eyJ` — and replaces each
  match with `[REDACTED-TOKEN]`. It deliberately does *not* match arbitrary long strings, so URLs, member numbers
  (`ZCU-100234`) and claims-challenge strings are left alone.
- **`TokenRedactingLogProcessor`** rewrites a log record's `Body`, `FormattedMessage` and every string attribute
  before export.
- **`TokenRedactingActivityProcessor`** rewrites span tags and the span display name before export (HTTP client
  instrumentation is the usual way a header value lands in a span).

This is a **backstop, not the control.** The primary control is never putting a token in a message at all — enforced by
tests and by the agent code (each token is used once from its `x-client-*` header and never logged or cached).

### Registration order matters

OpenTelemetry processors run in registration order, so redaction must be registered **before** any exporter. The bot and
MCP hosts therefore call `AddTokenRedaction()` *before* `AddServiceDefaults()` (which adds the OTLP exporter) and before the
Azure Monitor exporter. `RedactionPipelineTests` proves both halves against the real pipeline: tokens never reach an exporter
when redaction is first, and *do* reach it when redaction is registered after the exporter.

## Microsoft Agent 365 export (hosted agents; exercised in dev up to the Agent 365 side)

The hosting library already owns an Agent 365 exporter and, in dev, exports without any switch from us; this repo adds spans and an explicit
flag. Publish with `--agent365` to write `FOUNDRY_AGENT365_TRACING_ENABLED=true` into the definition (Foundry accepts it). With the flag on,
`ValidateAgent365Tracing` fails the agent at startup (naming the missing variables, never their values) unless `FOUNDRY_HOSTING_ENVIRONMENT`,
`FOUNDRY_AGENT_INSTANCE_CLIENT_ID` and `FOUNDRY_AGENT_TENANT_ID` are set. Our code adds an `invoke_agent` span per request and an `execute_tool`
span around the MCP call and the Graph retrieval call, carrying tenant, agent id, agent name and tool name. Content capture is off, and a failed
span keeps the exception type name only. The redactors run before the exporter; Application Insights export is unchanged. The platform adds a
`user.id` to every span; in dev it was the bot's managed identity, not a person. Foundry's own prompt-agent spans carry question and answer text
(platform tracing, not ours). See [DECISIONS B6a](DECISIONS-AND-LESSONS.md) for the live check and the open questions.

## How it is tested

| What | Test | What it proves |
| --- | --- | --- |
| The matcher | `Compass.Agents.Contracts.Tests` → `JwtRedactorTests` | Redacts tokens, leaves ordinary text/claims challenges alone, null-safe |
| The processors in the real pipeline | `RedactionPipelineTests` | An exporter registered after redaction receives no token in a log message, log attribute, span tag or span name; ordering is load-bearing |
| End to end, whole turn | `Compass.IdentityIsolation.Tests` → **`TokenLeakTests` (case 3)** | See below |

**Case 3** runs a full turn through the real bot application code and both real agent hosts (only Entra, SharePoint and the
Zava API are faked). It captures everything that could leak — bot logs, agent logs, exception `ToString()` output and HTTP
response bodies — through in-memory logger providers, then asserts none of it contains **either** a JWT-shaped
substring (`JwtRedactor.ContainsToken`) **or** the exact value of any token the fake Entra minted for the run. It includes
positive controls (a planted token *is* detected) so a broken detector cannot pass silently.

## What a reviewer should look for in traces

In Application Insights (or the Aspire dashboard), for a real turn:

1. **Search for `eyJ`** across `traces`, `dependencies` (HTTP client spans) and `exceptions`. Expect no hits other than
   `[REDACTED-TOKEN]`. Kusto: `union traces, dependencies, exceptions | where * contains "eyJ"`.
2. **Check the `dependencies` rows** for the bot → agent and agent → MCP calls: the URL and target are present; no
   `x-client-*` header values appear in custom dimensions.
3. **Per-person visibility is fine and intended:** the person's object ID may appear in logs ("the Function's logs show each
   person"). A token must never appear *with* it.
4. **Failure paths log a type, not a message.** Fail-closed turns log only the exception type (`BriefingTurnHandler`), e.g.
   `Turn failed closed with ClaimsChallengeException`.
5. **Per-person session IDs** (`x-compass-session-id`, derived per person) are distinct between User A and User B; a shared
   session ID would be a defect.

## Not implemented / known limits

- **Exception text is scrubbed only when it contains a token.** `TokenRedactingLogProcessor` replaces a log record's exception with a
  redacted copy (`RedactedException`: same type name, redacted message and stack trace) when its text holds a JWT. Span
  `StatusDescription` is redacted too. Exception *events* on an Activity cannot be edited, so the redactor drops the whole span from every exporter if an event holds a token; no code of ours adds
  any, and Agent 365 spans record only the exception type name. Agent 365 spans do not carry a conversation id (open; see B6a in
  DECISIONS-AND-LESSONS), so a run may not group into a conversation there.
- **Hosted agents export only when the Foundry project has Application Insights connected.** Before 19 Oct it was not, so agent-side
  traces and logs were absent. Now they appear (role `ZavaApiAgent`), with Foundry's own `invoke_agent` span (role `agentsv2`). Open:
  a steady stream of exporter self-telemetry (see the Agent 365 section below). Offline tests prove the redactors run ahead of any
  exporter registered after them; the live agent has not been re-checked.
- **Cross-service correlation works through standard trace context.** Observed: all 27 `ZavaApiAgent` calls had the bot's operation id on the
  MCP Function's requests. There is still **no custom per-turn operation ID** as spec §13 describes.
- **No dashboards or saved queries** ("requests by person, by agent, with zero token leakage") are authored; the Kusto query
  above is the manual equivalent.
- **Pattern-based only.** Only JWT-shaped values are redacted. Opaque (non-JWT) credentials, such as a client secret, would
  not be caught — none are used in this design (federated credentials, no secrets).
- **Live behaviour:** the Azure Monitor wiring has run against the real resource since 18 Oct. The bot and Function export requests,
  dependencies and traces; the bot's service name is now `compass-bot`; Agent Framework workflow spans are switched on with `WorkflowBuilder.WithOpenTelemetry()` (opt-in; registering the source alone does nothing) and a test checks
  they are emitted without message content. Seen live on 20 Oct (workflow.session, executor.process per executor, edge_group.process, message.send), in the same operation as the Foundry, hosted-agent and MCP spans.
