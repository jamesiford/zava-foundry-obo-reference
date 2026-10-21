# Compass OBO Reference Build — Development Specification

**For:** GitHub Copilot (coding agent) / the engineer driving it, building a full, real, end-to-end .NET 10 / C#
reference implementation of the identity pattern described in `Zava-Compass-OBO-Teams-to-SharePoint.md` (the "section 9
spike," extended per the decisions below).

> **Historical document.** This is the plan the build started from. The build has since diverged in places; see
> [`IMPLEMENTATION-NOTES.md`](IMPLEMENTATION-NOTES.md) for the differences and [`IDENTITY-AND-ACCESS.md`](IDENTITY-AND-ACCESS.md) for the
> identity design as built (for example, the bot also acquires an Azure AI Search token, so §9 hop 2 lists two audiences where the code uses three).

**Status (when written):** Ready to build from. Derived from `Zava-Compass-OBO-Teams-to-SharePoint.md`, `diagrams/model.json`,
`diagrams/Zava-Compass-OBO-Sequence.html`, and `diagrams/Zava-Compass-OBO-Architecture.html`, plus the clarifying Q&A
captured in §1.

**Not this:** This is not Zava Credit Union's production Compass bot. It's a standalone, fully real (no mocks/stubs in the
identity path) demonstration solution that proves the pattern end-to-end on a dev/test tenant, built so Zava Credit Union's own
engineers can read the code and carry the pattern into production Compass.

---

## 1. Decisions and assumptions log

Every open question from the discovery pass, resolved here. Treat this section as the contract for what Copilot
should build; if reality diverges (a package doesn't exist, a preview feature behaves differently), update this
section first, then the rest of the spec.

| # | Topic | Decision |
| --- | --- | --- |
| D1 | Scope | The section 9 spike, extended to **prove permission exclusion live**: two test users, different SharePoint access, same question, order-independent correct results, no post-retrieval pruning in our code. |
| D2 | Agents to build (one class each) | Three agents: two **hosted** agents and one Foundry **prompt** agent (M9a added the third). 1) `SharePointRetrievalAgent` (§5.1, Graph Retrieval API — GA, the brief's recommended path). 2) `ZavaApiAgent` (calls the MCP server over `x-client-zava-api-token`). 3) `SharePointKnowledgeAgent` (M9a — Foundry IQ remote-SharePoint knowledge base, driven by the Search-scope OBO token). The remaining SharePoint retrieval options from brief §5 (indexed knowledge sources, Search + ACLs/filters, the SharePoint tool) are **discussed and compared, not built** — see §7. This keeps the build small and keeps the teaching point about *why* 5.1 was chosen intact without carrying four agent deployments. |
| D3 | MCP server | Real, functioning Azure Functions (isolated worker, .NET 10) project. Fronts a **simulated core-banking "Member Accounts" domain** (in-process/EF Core SQLite store) since Zava Credit Union's real Workday/core-banking systems aren't reachable from this build. The Function App itself does real OBO-token audience validation — only the system behind it is simulated. This boundary is a port/adapter so a real core-banking adapter is a drop-in replacement later. |
| D4 | Teams bot | New, real, end-to-end Microsoft 365 Agents SDK (C#) bot project — Teams SSO, real OAuth connection, real MSAL OBO exchange. No simulated identity anywhere in the chain. |
| D5 | Hosting | Bot → Azure App Service (Linux, .NET 10). MCP server → Azure Functions (Flex Consumption or Premium, isolated worker). The hosted agents → Azure AI Foundry (agent hosting is a Foundry-managed surface, not App Service/Functions). Use VS's native publish profiles as the manual/inner-loop deployment path; use `azd` (Azure Developer CLI) + Bicep for the real, repeatable, automated path. Both are kept in sync by the same Bicep/`azure.yaml`. |
| D6 | Local orchestration | A **.NET Aspire AppHost** project composes the Bot host and the MCP Function App for local dev (service discovery, OpenTelemetry, dashboard), using Aspire's Azure App Service / Azure Functions hosting integrations. **Caveat:** Aspire has no first-class "Foundry agent" hosting integration as of this writing; the two hosted agents are provisioned by Bicep/`azd` and referenced into the AppHost as endpoint parameters, not as Aspire-orchestrated containers. Flag and re-verify this at build time. |
| D7 | Entra/Foundry setup | Fully spec'd and automated (infra project + one-time setup scripts), not assumed pre-provisioned. See §10. |
| D8 | Testing | Include unit tests per layer, functional tests for the MCP Function App, and the two-test-user / both-orders / negative-path isolation suite from brief §9, automated for CI. See §12. |
| D9 | Packages | Prefer GA; use preview only where the brief shows no GA alternative (hosted-agent Responses/Invocations protocol). **Exact NuGet package IDs for "Microsoft Agent Framework" and the Foundry SDK are moving targets in preview — verify the current IDs on NuGet.org / Microsoft Learn before `dotnet add package`; this spec names the best-known IDs as placeholders and flags each.** |
| D10 | Mediator library | Spec assumes **MediatR**-shaped CQRS (the de facto Clean Architecture convention). MediatR's commercial licensing terms have changed in recent major versions — **before scaffolding, confirm current licensing is acceptable for this build; if not, substitute the open-source `Mediator` (martinothamar) source-generator package or a small hand-rolled `ISender`/`IRequestHandler<,>` abstraction.** The contracts in this spec (`IRequest<T>`, `IRequestHandler<T,R>`) are written to be trivially portable between all three. **Resolved at M3: hand-rolled.** MediatR v13+ requires a commercial license key, so `Compass.Mcp.ZavaApi.Application` ships its own small `ISender` / `IRequestHandler<,>` / `IPipelineBehavior<,>` (`Messaging/`) with the same shapes; no mediator package is referenced. Later projects (Bot, Agents) should reuse the same approach — extract it to a shared project when a second consumer appears. Note: `CompassHeaders` (§8) lives in `Compass.Shared.Identity.Tokens` (per §4) rather than Agents.Contracts so the MCP host can use it without referencing the Agents layer. |
| D11 | Domain for the MCP demo | "Member Accounts" (credit-union-shaped: member profile, account summary, recent transactions) — matches model.json's "Core banking — system of record" node and gives SharePoint and MCP demos a shared, plausible Zava Credit Union narrative (a loan officer's assistant). |
| D12 | CI/CD | GitHub Actions assumed (repo is GitHub-hosted per the working environment). Pipeline: build → unit tests → (on `main`) `azd provision` + `azd deploy` to a dev environment → run the isolation test suite against that live dev deployment. |
| D13 | Test tenant & automated OBO proof | The two-user/both-orders suite needs real Entra tokens without interactive sign-in in CI. Use a **dedicated test tenant**, two test user accounts excluded from Conditional Access/MFA, and MSAL's Resource Owner Password Credential (ROPC) flow **strictly inside the test harness** to obtain each test user's initial token, which then feeds the real OBO chain exactly as Teams SSO would. Document this prominently as test-tenant-only, never production. |

---

## 2. Demo narrative (what this build proves, in order)

1. A Zava Credit Union employee (a loan officer) opens Teams and asks **Compass** (this demo bot) about a member and a policy
   document.
2. Compass resolves the person once (Teams SSO), gets one OBO token per downstream system, and never lets a model,
   prompt, or tool argument carry who the caller is (Compass identity contract rule 1–2).
3. `SharePointRetrievalAgent` calls the Copilot Retrieval API with the person's own Graph token. **User A**, who has
   access to a "Lending Policy" SharePoint folder, sees it; **User B**, who doesn't, never sees it in the result —
   with no post-retrieval pruning in Compass's own code. SharePoint's permissions, via Microsoft 365's index, decide.
4. `ZavaApiAgent` calls the MCP server (Azure Functions) with `x-client-zava-api-token`; the Function validates the
   token's audience and returns the member's account summary — proving MCP's "never forward a token to a different
   API" rule, independent of SharePoint.
5. The whole exchange is run as **User A then User B**, and again as **User B then User A** — pass criterion:
   identical per-person results regardless of order, no cross-user bleed, no raw token in any log or trace.
6. Negative paths: missing token (no downstream call made — fail closed), expired/invalid token (surfaced to the
   bot, which re-acquires), and a simulated claims-challenge (carried back to Teams sign-in).

---

## 3. High-level architecture

```
Teams (User A / User B)
   │  Teams SSO (Activity, Bot Service token store)
   ▼
Compass.Bot.Host  (Agents SDK, ASP.NET Core, App Service)
   │  MSAL confidential client OBO  (per downstream API, per turn)
   │  ├─ Graph token (Files.Read.All, Sites.Read.All)
   │  └─ Zava Credit Union API token (api://zava-mcp-api)
   │
   │  x-ms-user-identity: <object id>
   │  x-client-graph-access-token / x-client-zava-api-token
   ▼
Azure AI Foundry project
   ├─ SharePointRetrievalAgent (hosted)  → Graph /copilot/retrieval → SharePoint Online
   └─ ZavaApiAgent             (hosted)  → Compass.Mcp.ZavaApi (Azure Functions, MCP) → simulated core banking
```

Reuse `diagrams/Zava-Compass-OBO-Sequence.html` (time view) and `diagrams/Zava-Compass-OBO-Architecture.html` (C4
structure, L1–L3) as the living architecture reference for the identity pattern itself; this build implements the two
agents above, with the remaining SharePoint retrieval options from the brief retained as a discussion/decision
record rather than additional running code — see §7.

---

## 4. Solution and project layout

```
Compass.sln
├── src/
│   ├── Bot/
│   │   ├── Compass.Bot.Host              (Agents SDK bot — ASP.NET Core, App Service target)
│   │   ├── Compass.Bot.Application       (MediatR commands/queries, orchestration — Clean Architecture "Application")
│   │   ├── Compass.Bot.Domain            (PersonContext, DownstreamTokenSet, value objects — "Domain", zero deps)
│   │   └── Compass.Bot.Infrastructure    (MSAL OBO client, Foundry client adapter, Graph client adapter — "Infrastructure")
│   ├── Agents/
│   │   ├── Compass.Agents.Contracts                   (shared DTOs, header name constants, IHostedAgent interface — referenced by Bot + both agent projects)
│   │   ├── Compass.Agents.Hosted.SharePointRetrieval   (SharePointRetrievalAgent — hosted agent container)
│   │   └── Compass.Agents.Hosted.ZavaApi               (ZavaApiAgent — hosted agent container)
│   ├── Mcp/
│   │   ├── Compass.Mcp.ZavaApi.Host            (Azure Functions isolated worker — MCP server transport + token validation)
│   │   ├── Compass.Mcp.ZavaApi.Application     (CQRS handlers: GetMemberSummary, GetAccountSummary, ListRecentTransactions)
│   │   ├── Compass.Mcp.ZavaApi.Domain          (Member, Account, Transaction entities)
│   │   └── Compass.Mcp.ZavaApi.Infrastructure  (IMemberAccountRepository + EF Core/SQLite seeded implementation — the simulated "core banking" adapter boundary)
│   └── Shared/
│       ├── Compass.Shared.Identity         (header-name constants, scope/role constants, token-audience helpers used by Bot + MCP Function App)
│       └── Compass.Shared.Observability    (OpenTelemetry enrichers, token-redaction log processor — used everywhere)
├── apphost/
│   ├── Compass.AppHost            (.NET Aspire AppHost — local orchestration + azd deployment manifest source)
│   └── Compass.ServiceDefaults    (Aspire service defaults: OTel, health checks, resilience — referenced by Bot.Host and Mcp.Host)
├── infra/
│   ├── azure.yaml               (azd project definition)
│   ├── main.bicep               (entry point; resource group scope)
│   ├── modules/
│   │   ├── appService.bicep
│   │   ├── functionApp.bicep
│   │   ├── foundryProject.bicep
│   │   ├── foundryAgents.bicep         (hosted agent deployments, container protocol 2.0.0, RBAC role assignments for the two agents)
│   │   ├── keyVault.bicep
│   │   ├── appInsights.bicep
│   │   └── rbac.bicep                  (custom "Foundry Agent Impersonator" role definition + assignments)
│   └── scripts/
│       ├── Setup-EntraApps.ps1         (bot app registration, API app registration, federated identity credential)
│       ├── Grant-AdminConsent.ps1      (delegated permission grants for Graph and Zava Credit Union API scopes)
│       ├── New-FoundryImpersonationRole.ps1
│       ├── Assert-DemoUsers.ps1        (read-only check that the two existing demo people exist and are licensed)
│       ├── Set-MemberMapping.ps1       (maps the demo people's object ids to member numbers in bot app settings)
│       ├── Compass.DemoDocuments.ps1   (Markdown to .docx converter for the seeded documents)
│       └── Seed-SharePointTestData.ps1 (three libraries with deliberately different permissions for Daniel Park / Maya Lin)
└── tests/
    ├── Compass.Bot.Application.Tests         (unit — MediatR handlers, OBO orchestration logic, mocked token/Foundry clients)
    ├── Compass.Agents.Contracts.Tests        (unit — header/contract shape, serialization)
    ├── Compass.Mcp.ZavaApi.Tests             (unit + functional — handlers, token-audience validation middleware)
    └── Compass.IdentityIsolation.Tests        (integration — two-user/both-orders suite + negative paths, §12)
```

Target framework: `net10.0` everywhere except the Functions host, which uses `net10.0` on the isolated worker model
(confirm Azure Functions .NET 10 isolated-worker support is GA at build time; Functions typically trails the .NET
SDK GA by a few months — if .NET 10 isn't yet supported on Functions, pin `Compass.Mcp.ZavaApi.Host` to the latest
supported LTS/STS and note the gap here).

The one Foundry IQ path that **is** built (M9a) is the remote-SharePoint knowledge base behind
`SharePointKnowledgeAgent`; `infra/modules/aiSearch.bicep` provisions only the Azure AI Search service it needs, and
the knowledge source, knowledge base and Foundry connection are created by `Setup-KnowledgeBase.ps1` (data plane, not
Bicep). No indexed source or Search index is provisioned — those remain the discussed-but-not-built options in §7.

M9 added `Compass.Agents.Prompt.SharePointKnowledge` (the third agent, a Foundry **prompt** agent) and
`Compass.Bot.Orchestration` (the Microsoft Agent Framework workflow that runs the agents in scope for the question in parallel; see §7.4).

---

## 5. Clean Architecture + CQRS conventions

Applies uniformly to `Compass.Bot.*` and `Compass.Mcp.ZavaApi.*`:

- **Domain** — entities, value objects, domain events. No package references beyond the BCL.
- **Application** — MediatR `IRequest<TResponse>` / `IRequestHandler<TRequest,TResponse>` pairs, one file per
  use case (`Commands/`, `Queries/` folders), `IPipelineBehavior<,>` for cross-cutting concerns (validation via
  FluentValidation, logging, the token-redaction behavior). Depends only on Domain + abstraction interfaces defined
  here (e.g., `IObOTokenBroker`, `IMemberAccountRepository`) — never on Infrastructure directly.
- **Infrastructure** — implements the Application-layer interfaces: MSAL OBO exchange, Graph/Foundry HTTP clients,
  EF Core repository. Registered via `IServiceCollection` extension methods (`AddCompassBotInfrastructure()`,
  `AddZavaApiInfrastructure()`).
- **Host/Presentation** — `Compass.Bot.Host` (Activity handlers calling `ISender.Send(...)`) and
  `Compass.Mcp.ZavaApi.Host` (Azure Functions triggers calling `ISender.Send(...)`). Thin: parse input, send the
  MediatR request, map the result to the transport response.

Representative commands/queries (names are the actual class names Copilot should generate):

| Host | Type | Name | Purpose |
| --- | --- | --- | --- |
| Bot | Query | `GetMemberBriefingQuery` | Orchestrates one turn: resolve person → OBO tokens → fan out to both agents → compose a single answer |
| Bot | Command | `AcquireDownstreamTokenCommand` | Wraps one MSAL OBO exchange for one downstream audience; returns a short-lived, in-memory-only token handle (never persisted) |
| Bot | Query | `InvokeSharePointRetrievalAgentQuery` | Calls `SharePointRetrievalAgent`'s Foundry endpoint with the person's Graph token |
| Bot | Query | `InvokeZavaApiAgentQuery` | Calls `ZavaApiAgent` with the person's Zava Credit Union API token |
| MCP | Query | `GetMemberSummaryQuery` | Member profile lookup by member number |
| MCP | Query | `GetAccountSummaryQuery` | Account balances for a member |
| MCP | Query | `ListRecentTransactionsQuery` | Last N transactions for an account |

---

## 6. Coding standards

Applies to every project in the solution (Bot, Agents, MCP, Shared, infra scripts where practical). Reviewers
should block a PR on violations here with the same weight as a failing test — these rules exist to keep an
identity-sensitive codebase readable and safe to extend after this reference build ships.

### SOLID

- **Single Responsibility** — one class per agent (already the project rule); one handler per MediatR
  command/query; one reason to change per class. A handler orchestrates, it doesn't also parse HTTP, validate
  tokens, and format a Teams card.
- **Open/Closed** — new downstream systems or agents are added by implementing an existing interface
  (`IHostedAgent<,>`, `IObOTokenBroker`, `IMemberAccountRepository`), never by branching on a type/string inside an
  existing class. Example: adding a third hosted agent means a new class + DI registration, not an `if` branch in
  `GetMemberBriefingQuery`.
- **Liskov Substitution** — every `IHostedAgent<TRequest,TResponse>` implementation must honor the same contract:
  fail closed on a missing token, never swallow a downstream exception into a "success" result, never log a raw
  token. A test double standing in for a real agent in a unit test must be safely substitutable without the test
  asserting anything false.
- **Interface Segregation** — keep interfaces narrow and audience-specific (`IGraphRetrievalClient` only exposes
  `RetrieveAsync`; `IZavaApiMcpClient` only exposes what `ZavaApiAgent` needs). Don't force the MCP Function App to
  depend on a Graph-shaped interface or vice versa.
- **Dependency Inversion** — Application-layer code depends only on interfaces it defines; Infrastructure
  implements them and is wired up via DI in the Host project only. Domain has zero outward dependencies.

### DRY

- Token-handling logic (header lookup, fail-closed check, redaction) lives once in `Compass.Shared.Identity` /
  `Compass.Shared.Observability` and is consumed everywhere else — never copy-pasted per agent. The fail-closed check
  shown on both agent classes in §8 (`if (!context.ForwardedTokens.TryGetValue(...))`) should be a shared extension
  method (e.g. `context.RequireToken(CompassHeaders.GraphAccessToken)`), not hand-rolled per class.
- Cross-cutting concerns (validation, logging, token redaction, retry/resilience) are MediatR
  `IPipelineBehavior<,>` implementations applied once in the pipeline, not repeated inside each handler.
- Header name constants, role names, and scope strings are defined exactly once (`CompassHeaders`,
  `Compass.Shared.Identity`) and referenced everywhere — no magic strings for `"x-client-graph-access-token"` etc.
  scattered across projects.
- Bicep: shared values (resource naming, tags, location) come from `main.bicep` parameters/variables consumed by
  every module — not re-declared per module.

### Documentation and comments

- Public types and members in `Compass.Agents.Contracts`, every `IHostedAgent<,>` implementation, and every
  `IObOTokenBroker`/token-handling class get an XML doc comment (`<summary>`) explaining **intent**, especially
  *why* a security rule exists (e.g., "fails closed — see Compass identity contract rule 5" rather than just
  restating the `if` statement).
- Inline comments are reserved for non-obvious logic: why a token has no `Bearer` prefix on the wire, why a
  header's value is never cached past one call, why ROPC is acceptable only in the test harness (§12). Do **not**
  add comments that restate what the code already says (`// increment counter` above `count++`).
- Every project has a short `README.md` (or assembly-level XML doc) stating its Clean Architecture layer and what
  it must never depend on.
- Nullable reference types enabled solution-wide; treat a new nullable warning as a review blocker, not a suppress
  target.

### Other conventions

- File-scoped namespaces, `async`/`await` with `CancellationToken` threaded through every call, `Async` suffix on
  async methods, `I`-prefixed interfaces, immutable `record`/`record struct` for DTOs and MediatR
  requests/responses.
- `.editorconfig` plus an analyzer set (e.g., `Microsoft.CodeAnalysis.NetAnalyzers` with security rules enabled) is
  checked in at the solution root and enforced in CI (`dotnet build -warnaserror` for the security-relevant rule
  set at minimum).
- Every rule in this section is a direct enabler of the §14 security checklist — if a SOLID/DRY violation would
  make it easier to accidentally log a token or duplicate a fail-open path, treat it as a security bug, not a
  style nit.

---

## 7. SharePoint retrieval: why Retrieval API, and when you'd choose differently

We're building exactly one SharePoint retrieval path (`SharePointRetrievalAgent`, brief §5.1). The brief documents
five other ways to get SharePoint content into an agent response with OBO-correct permissions; we're not building
them, but the choice of 5.1 only makes sense in contrast to them. Use this table as the authoritative "why not X"
answer for code review and for Zava Credit Union's own future design decisions — it's the brief's §5.7 comparison, annotated
with when each alternative *would* be the right call.

| Option (brief §) | How it enforces permissions | Why **not** chosen here | When you *would* choose it |
| --- | --- | --- | --- |
| **5.1 Copilot Retrieval API (built)** | SharePoint's own permissions, live, via Microsoft 365's index — no pruning, no re-sync lag | — (this is the build) | Default choice whenever the calling persons hold (or the tenant has) Microsoft 365 Copilot licensing and GA/production posture is wanted over preview |
| **5.2 Foundry IQ knowledge base, remote SharePoint source (built in M9a)** | Same live SharePoint permission check, routed through Azure AI Search instead of Graph directly, using the person's **Search-scope** OBO token (`x-client-search-token`) | — built as `SharePointKnowledgeAgent` (M9a) so the two routes to the same SharePoint content can be compared side by side. It stays preview (Foundry IQ) and costs an extra hop and a Search service; 5.1 remains the recommended default | You need **one retrieval call across several knowledge sources** (SharePoint plus, say, a non-SharePoint index) and can accept preview status |
| 5.3 Foundry IQ knowledge base, indexed SharePoint source | Azure AI Search checks **ingested ACL snapshots**, not SharePoint live — permission changes only take effect after the next indexer/ACL sync | Deliberately the wrong shape for a demo about live permission exclusion: a revoked user can still see content until re-sync. Also preview, and requires standing up an Azure AI Search index + indexer just for this path | Content the Retrieval API can't reach at all (e.g., a non-Microsoft-365-indexed corpus), where some permission staleness is an accepted tradeoff |
| 5.4 Azure AI Search index + ACLs/filters | Either Search checks ingested ACLs (preview, same staleness issue as 5.3), or **your own code** builds a group-based filter (GA, but correctness is entirely your code's responsibility) | This build has no pre-existing SharePoint-backed Search index to migrate off of post-retrieval pruning; nothing to retrofit | Zava Credit Union already has a SharePoint-backed Search index and a post-retrieval-pruning design today — this is the realistic **interim step** to stop pruning after retrieval without a full platform change |
| 5.5 Foundry SharePoint tool | Needs the person's own Foundry identity; every caller needs a Foundry User license | Explicitly ruled out by the brief for Teams-published agents ("doesn't work when the agent is published to Microsoft Teams") | Not applicable to any Teams-fronted hosted-agent design; only relevant for a direct-to-Foundry client scenario outside Teams |
| 5.6 Knowledge base via MCP | Same knowledge-base permission model as 5.2/5.3, exposed as an MCP endpoint | No documented toolbox per-request token injection for hosted agents; would require the same direct-call pattern as 5.2 for no added benefit here | A **prompt agent** (not a hosted agent) that needs to bind a knowledge base as a declarative tool via `structured_inputs`, where the extra MCP indirection buys you reuse across multiple prompt agents |

Since M9a, 5.1 and 5.2 already run side by side in the demo (two OBO routes to the same SharePoint content, which the
person's permissions filter identically). If Zava Credit Union later wants to see 5.3/5.4 as well (e.g., to make the re-sync-lag risk visible to a wider
audience), that's a natural **phase 2** extension of this same solution: add an `IndexedKnowledge` project following the same `IHostedAgent<,>`-shaped wrapper pattern used
for the agents here, plus an Azure AI Search index and indexer. Keep that as a clearly separate milestone so the core
demo stays small.

---

## 8. Agent catalog (one class per agent)

Shared contracts (`Compass.Agents.Contracts`):

```csharp
namespace Compass.Agents.Contracts;

public static class CompassHeaders
{
    public const string UserIdentity = "x-ms-user-identity";
    public const string GraphAccessToken = "x-client-graph-access-token";
    public const string ZavaApiToken = "x-client-zava-api-token";
    public const string AgentUserId = "x-agent-user-id"; // set by the platform on hop 4, read-only to agent code
}

public interface IHostedAgent<TRequest, TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, AgentCallContext context, CancellationToken ct);
}

// AgentCallContext carries only what already arrived in headers — never a raw bearer token logged, never cached
// beyond the single call.
public sealed record AgentCallContext(string PersonObjectId, IReadOnlyDictionary<string, string> ForwardedTokens);
```

### 7.1 `SharePointRetrievalAgent` (hosted — `Compass.Agents.Hosted.SharePointRetrieval`)

- Protocol: Responses, container protocol 2.0.0 (per brief §4 deployment prerequisites).
- Reads `x-client-graph-access-token`; calls `POST https://graph.microsoft.com/v1.0/copilot/retrieval` with that
  token, `Files.Read.All` + `Sites.Read.All` delegated scopes.
- Returns hits with site URLs; does **no** additional filtering — the whole point is that SharePoint already
  decided.
- Foundry RBAC: **Foundry Agent Consumer** + custom role with
  `Microsoft.CognitiveServices/accounts/AIServices/agents/endpoints/UserIdentityImpersonation/action`, scoped to
  this agent only.

```csharp
namespace Compass.Agents.Hosted.SharePointRetrieval;

public sealed class SharePointRetrievalAgent : IHostedAgent<RetrievalRequest, RetrievalResult>
{
    private readonly IGraphRetrievalClient _graph; // Infrastructure-layer HTTP client, injected

    public SharePointRetrievalAgent(IGraphRetrievalClient graph) => _graph = graph;

    public async Task<RetrievalResult> HandleAsync(RetrievalRequest request, AgentCallContext context, CancellationToken ct)
    {
        if (!context.ForwardedTokens.TryGetValue(CompassHeaders.GraphAccessToken, out var graphToken) || string.IsNullOrEmpty(graphToken))
        {
            // Fail closed — rule 5 of the Compass identity contract.
            throw new MissingDownstreamTokenException(CompassHeaders.GraphAccessToken);
        }

        return await _graph.RetrieveAsync(graphToken, request.Query, request.MaxResults, ct);
    }
}
```

### 7.2 `ZavaApiAgent` (hosted — `Compass.Agents.Hosted.ZavaApi`)

- Reads `x-client-zava-api-token`; calls the MCP server (`Compass.Mcp.ZavaApi.Host`) over its MCP transport (HTTP),
  forwarding that token unchanged, never exchanging or re-minting it (brief §4, "Why this also fixes MCP").

```csharp
namespace Compass.Agents.Hosted.ZavaApi;

public sealed class ZavaApiAgent : IHostedAgent<MemberBriefingRequest, MemberBriefingResult>
{
    private readonly IZavaApiMcpClient _mcp;

    public ZavaApiAgent(IZavaApiMcpClient mcp) => _mcp = mcp;

    public async Task<MemberBriefingResult> HandleAsync(MemberBriefingRequest request, AgentCallContext context, CancellationToken ct)
    {
        if (!context.ForwardedTokens.TryGetValue(CompassHeaders.ZavaApiToken, out var apiToken) || string.IsNullOrEmpty(apiToken))
            throw new MissingDownstreamTokenException(CompassHeaders.ZavaApiToken);

        return await _mcp.GetMemberBriefingAsync(apiToken, request.MemberNumber, ct);
    }
}
```

### 7.3 `SharePointKnowledgeAgent` (prompt — `Compass.Agents.Prompt.SharePointKnowledge`, added in M9a)

- A Foundry **prompt** agent (declarative, no container), created by `SharePointKnowledgeAgentProvisioner`
  (`--publish-knowledge-agent`). It binds the Foundry IQ remote-SharePoint knowledge base as an MCP tool at
  `{search}/knowledgebases/{kb}/mcp?api-version=2026-08-01-preview`.
- Reads `x-client-search-token` (OBO token for `https://search.azure.com/.default`) and sends it as the MCP tool's
  `Authorization` header per call, so Azure AI Search queries SharePoint **as the person** and SharePoint's own
  permissions decide what comes back — the same outcome as 7.1 by a different route.
- Runs in-process in the bot through the Foundry SDK Responses client (`FoundryResponsesClientFactory`), not as a
  hosted container, so it implements the same `IHostedAgent<KnowledgeQueryRequest, KnowledgeQueryResult>` shape.
  Per-call headers are built per request and never stored on a shared client.
- Prerequisite: the person needs a Microsoft 365 Copilot licence for the remote SharePoint source.
- Infra: `infra/modules/aiSearch.bicep` (service only) and `Setup-KnowledgeBase.ps1` (knowledge source, knowledge base
  and Foundry connection — data plane, not Bicep).

### 7.4 Orchestration (`Compass.Bot.Orchestration`, added in M9c)

`GetMemberBriefingQueryHandler` resolves the person and the session, classifies the question into a `BriefingScope` (the
configured route, narrowed to accounts, policy or both; see below), resolves the member number only when accounts are in
scope, and performs one OBO exchange per audience *in scope*, then hands a `BriefingPlan` to `IBriefingOrchestrator`. The
implementation runs a Microsoft Agent Framework workflow built per turn: `briefing-start` fans out to the executors in scope
among `sharepoint-retrieval`, `sharepoint-knowledge` and `zava-api` (each sends the existing `Invoke*AgentQuery` with its own
token), a fan-in barrier delivers their `BranchResult`s to `briefing-compose`, which yields the `MemberBriefing` only once
every branch in scope has reported. A failed branch aborts the run and the orchestrator rethrows the original typed
exception, so fail-closed behaviour and the claims-challenge path are unchanged. The workflow is built per turn so no state
can cross people.

**Per-turn scope (M11k).** `QuestionFocusClassifier` is deterministic and reads only the wording. `BriefingRoutePolicy.ScopeFor`
can only remove branches the route allows, never add one; an unclear question keeps all of them. A branch outside the scope is
never built and its token is never requested, which is why the decision is made before the exchange rather than with
conditional edges inside the graph. `MemberBriefing.Accounts` is therefore optional, and `BriefingComposer` shows exactly what
ran, dropping a knowledge answer that is blank or "I don't know".

---

## 9. Identity / OBO implementation detail (hop by hop)

Maps directly to the table in brief §4.

1. **Teams → Bot Service → `Compass.Bot.Host`** — Agents SDK OAuth connection configured for the bot's own API
   (`api://botid-{appid}/access_as_user`; the dev spec first said `defaultScopes`); Teams SSO per `l-sdk-sso` (desktop/mobile/web client IDs pre-authorized,
   Token Exchange URL set, `webApplicationInfo` in the Teams manifest).
2. **Bot → Entra (OBO)** — `Compass.Bot.Infrastructure` implements `IObOTokenBroker` using MSAL's confidential
   client, `AcquireTokenOnBehalfOf`, one call per downstream audience (Graph, Zava Credit Union API) per turn. Client
   credential is a **federated identity credential on the bot's app registration trusting the bot's managed
   identity** — no secret anywhere (brief §4, §8; `l-mi-fic`).
3. **Bot → Foundry** — the bot's own `https://ai.azure.com` token authenticates the call; `x-ms-user-identity` is
   set to the person's object ID (never a token); each downstream token goes into its own `x-client-*` header,
   **no `Bearer` prefix**.
4. **Foundry → container** — platform behavior, not our code: `x-client-*` forwarded, `Authorization` dropped,
   `x-agent-user-id` added.
5. **Hosted agent → SharePoint / MCP** — each agent class in §8 uses the token from its own `x-client-*` header
   exactly once, never logs it, never caches it past the call.
6. **MCP server → simulated core banking** — `Compass.Mcp.ZavaApi.Host` validates the audience/issuer of
   `x-client-zava-api-token` (standard JWT bearer validation middleware configured for the Zava Credit Union API app
   registration), then calls `IMemberAccountRepository` — the system's "own delegation," simulated here.

Cross-cutting, enforced in `Compass.Shared.Identity` + `Compass.Shared.Observability` and checked by tests in §12:

- No token is ever written to a log, trace, or exception message (a `TokenRedactingLogProcessor` /
  `TelemetryProcessor` scrubs any string matching a JWT shape before it reaches App Insights).
- Each person gets their own session ID from the bot; the bot never accepts a client-supplied session,
  conversation, or response ID (brief §6 rule 8).
- Fail closed everywhere: missing/expired token → no downstream call, surfaced as a typed exception the bot maps
  to "please sign in again" rather than falling back to app-only.

---

## 10. Infrastructure as code

`infra/azure.yaml` (azd) drives:

1. **Entra app registrations** (via `Setup-EntraApps.ps1`, Microsoft Graph PowerShell/CLI — app registration
   creation isn't fully Bicep-native): bot app registration + federated identity credential trusting the bot's
   user-assigned managed identity; Zava Credit Union API app registration exposing OBO-able scopes; Teams app manifest with the
   bot's client IDs.
2. **Admin consent** (`Grant-AdminConsent.ps1`): grants the delegated permissions (Graph `Files.Read.All`,
   `Sites.Read.All`; Zava Credit Union API's own scope) — requires an admin running it once per tenant; script is idempotent
   and safe to re-run.
3. **Core Azure resources** (Bicep modules): App Service plan + site (bot), Function App + storage (MCP), Key
   Vault, Application Insights + Log Analytics, managed identities, RBAC assignments.
4. **Foundry resources**: Foundry project, the two hosted agents deployed with container protocol 2.0.0, the
   custom "Foundry Agent Impersonator" role (`…/agents/endpoints/UserIdentityImpersonation/action`) assigned to the
   bot's managed identity scoped to each hosted agent individually (brief §4, §8 — never grant end users Foundry
   roles for this pattern).
5. **Demo fixtures** (`Assert-DemoUsers.ps1`, `Seed-SharePointTestData.ps1`, `Set-MemberMapping.ps1`): two existing demo
   people (Daniel Park, Credit Officer; Maya Lin, Portfolio Analyst - West) are verified read-only, never created by the
   repo; three SharePoint document libraries with deliberately different sharing so Daniel can see "Lending Policy"
   files Maya cannot, and vice versa for "Portfolio and Collections", plus a shared "Member Services FAQ" control library
   — this is the dataset the demo runs against (sources in `infra/demo-data`, uploaded as `.docx`).

No Azure AI Search or Foundry IQ knowledge base modules are needed for this build (see §7).

All scripts are PowerShell 7, idempotent, parameterized by environment (`dev`/`test`), and safe to re-run inside
`azd provision` hooks (`preprovision`/`postprovision`).

---

## 11. .NET Aspire + deployment

- `Compass.AppHost` references `Compass.Bot.Host` and `Compass.Mcp.ZavaApi.Host` as Aspire resources (App Service and
  Azure Functions hosting integrations respectively), wires Application Insights/OpenTelemetry via
  `Compass.ServiceDefaults`, and exposes the two Foundry hosted-agent endpoints and the test-tenant config as
  external parameters/connection strings (not orchestrated containers — see D6).
- Local inner loop: `dotnet run --project apphost/Compass.AppHost` — Aspire dashboard shows the bot and the Function
  App together; both call the already-provisioned dev Foundry project (hosted agents aren't meaningfully "local"
  — they run in Foundry even in dev).
- Deployment: `azd up` (provision + deploy everything, including Foundry resources) is the source of truth.
  Individual-project VS "Publish" profiles (right-click → Publish, targeting the same App Service/Function App) are
  kept working for engineers who want a fast single-project redeploy during iteration — document in the repo README
  which one to use when.
- Re-verify at build time whether Aspire's Azure App Service / Azure Functions hosting integrations have reached
  GA; if still preview, pin the preview version explicitly and note it.

---

## 12. Testing strategy

| Project | Kind | Covers |
| --- | --- | --- |
| `Compass.Bot.Application.Tests` | Unit | MediatR handlers with mocked `IObOTokenBroker`/agent clients; fail-closed behavior when a token is missing; session-ID isolation logic |
| `Compass.Agents.Contracts.Tests` | Unit | Header constants, request/response (de)serialization, `AgentCallContext` never exposes a token in `ToString()`/logging |
| `Compass.Mcp.ZavaApi.Tests` | Unit + functional | CQRS handlers against the seeded `IMemberAccountRepository`; JWT audience/issuer validation middleware (valid, expired, wrong-audience, missing token) |
| `Compass.IdentityIsolation.Tests` | Integration (runs against a live dev deployment) | The brief §9 proof, below |

**`Compass.IdentityIsolation.Tests` — required cases:**

1. **Two users, both orders** — run User A then User B, and User B then User A, against
   `SharePointRetrievalAgent`. Assert: each sees exactly their permitted document(s), never the other's,
   independent of call order.
2. **MCP identity passthrough** — `ZavaApiAgent` call for User A and User B returns each person's own member data;
   Function App logs show the correct person per request.
3. **No token in logs/traces** — scan captured App Insights/OTel output from the above runs for any JWT-shaped
   string; must find none.
4. **Negative: missing token** — call an agent with no `x-client-*` header present; assert
   `MissingDownstreamTokenException` / 4xx, and no downstream call was attempted (verify via the downstream
   fake/spy or Function App log absence).
5. **Negative: expired/invalid token** — force an expired token; assert the bot receives a typed failure and
   attempts a fresh OBO acquisition rather than retrying the same token or falling back to app-only.
6. **Negative: claims challenge** — simulate a Conditional Access claims challenge response from Entra; assert it
   propagates back to the bot as a "please re-authenticate" Teams message rather than being swallowed.

**Test-user token acquisition (test-tenant only):** a small MSAL ROPC helper in the test project obtains each test
user's initial token non-interactively (test tenant, CA/MFA-exempt accounts only — never used against a production
tenant); that token is fed into the same `IObOTokenBroker` code path production Teams SSO would populate, so the
system under test is identical to production except for how the very first token arrived. The **live Teams demo** (M10)
does not use ROPC at all: it signs in the two existing demo people (Daniel Park, Maya Lin) interactively through Teams
SSO, and no repo script creates users, groups, passwords or Conditional Access exclusions.

---

## 13. Observability

- `Compass.Shared.Observability` provides a `TokenRedactingProcessor` (OpenTelemetry `BaggageProcessor`/log
  enricher) applied in every host (`Compass.Bot.Host`, `Compass.Mcp.ZavaApi.Host`, and configured into each agent's
  Foundry deployment logging where supported) that regex-scrubs JWT-shaped substrings before export.
- Application Insights (via Aspire `ServiceDefaults` + Bicep) across bot → Foundry → MCP, correlated by a
  per-turn operation ID (distinct from the per-person session ID — never log the person's object ID and a token
  together, but the object ID itself is fine to log per the brief's "the Function's logs show each person").
- Dashboards/queries for the demo: "requests by person, by agent, with zero token leakage" — this is literally
  the isolation test's assertion made visible for the live demo audience.

---

## 14. Security checklist (Compass identity contract → implementation → test)

| Contract rule (brief §6) | Implementation | Verified by |
| --- | --- | --- |
| 1. Person resolved once, by the bot | `Compass.Bot.Application` resolves `PersonContext` from the validated Teams SSO token exactly once per turn; never passed as a prompt/tool argument | `Compass.Bot.Application.Tests` |
| 2. One OBO per downstream API per turn, never logged, request-only retention | `AcquireDownstreamTokenCommand` + `IObOTokenBroker`; no token field is ever serialized to logs (enforced by `TokenRedactingProcessor` + unit test asserting `AgentCallContext`/exceptions never `ToString()` a token) | `Compass.Agents.Contracts.Tests`, §12 case 3 |
| 3. Only the bot may assert a person to Foundry | Only `Compass.Bot.Infrastructure`'s Foundry client sets `x-ms-user-identity`; role scoped per agent in Bicep | Manual RBAC review + §12 case 1/2 |
| 4. The store decides; no post-retrieval pruning | `SharePointRetrievalAgent` returns results unfiltered from the Retrieval API | Code review; §12 case 1 |
| 5. Fail closed | Every agent class throws `MissingDownstreamTokenException` rather than falling back to app-only | §12 case 4 |
| 6. No agent calls another on a person's behalf without a proven path | Out of scope for this build — each of the two agents is called directly by the bot only | Architecture review |
| 7. Prove it with two people, both orders | §12 case 1/2 | CI |
| 8. Per-person session isolation | Bot mints its own session ID per person; never accepts client-supplied session/conversation/response IDs | `Compass.Bot.Application.Tests` |

---

## 15. Build milestones (sequenced backlog for Copilot)

1. **M0 — Repo & solution scaffold.** `Compass.sln`, empty projects per §4, package references pinned (verify D9/D10
   package IDs first), `Compass.ServiceDefaults` wired into `Compass.Bot.Host` and `Compass.Mcp.ZavaApi.Host`.
2. **M1 — Infra baseline.** Bicep modules for App Service, Function App, Key Vault, App Insights; `azd up` succeeds
   with empty app code deployed ("hello world" on each host).
3. **M2 — Entra & Foundry setup automation.** `Setup-EntraApps.ps1`, `Grant-AdminConsent.ps1`, Foundry project +
   RBAC role Bicep; run once against the dev tenant.
4. **M3 — MCP server.** `Compass.Mcp.ZavaApi.*` complete: domain, EF Core/SQLite seeded repository, CQRS handlers,
   JWT audience validation, unit + functional tests green.
5. **M4 — Hosted agents.** `SharePointRetrievalAgent`, `ZavaApiAgent` implemented and deployed to Foundry
   (container protocol 2.0.0); call them directly (bypassing the bot) with a manually-obtained test token to prove
   wiring before the bot exists.
6. **M5 — Bot.** `Compass.Bot.*` complete: Teams SSO, MSAL OBO, MediatR orchestration (`GetMemberBriefingQuery`
   fanning out to both agents), deployed to App Service, working end-to-end in Teams for a single test user.
7. **M6 — Test data & isolation suite.** `Seed-SharePointTestData.ps1` and the demo fixtures (originally
   `Seed-TestUsers.ps1`, replaced in M10 by the read-only `Assert-DemoUsers.ps1`),
   `Compass.IdentityIsolation.Tests` all six cases green in CI.
8. **M7 — Aspire + CI/CD.** `Compass.AppHost` local inner loop working; GitHub Actions pipeline (build → unit tests →
   `azd provision`/`deploy` to dev → isolation suite against live dev) green.
9. **M8 — Demo polish.** Script for the §2 narrative, dashboards from §13, README tying it back to
   `Zava-Compass-OBO-Teams-to-SharePoint.md` and including the §7 "why not the other SharePoint options" table for
   anyone reviewing the build.

10. **M9 — Knowledge-base agent, Foundry SDK, MAF workflow.** Delivered in four steps, one commit each:
    - **M9a** — `SharePointKnowledgeAgent` (`Compass.Agents.Prompt.SharePointKnowledge`): Foundry prompt agent bound to a
      Foundry IQ remote-SharePoint knowledge base over MCP; Search audience/scope/header; `aiSearch.bicep`;
      `Setup-KnowledgeBase.ps1`; Entra Search `user_impersonation` consent.
    - **M9b** — `FoundryHostedAgentGateway` now uses the Foundry SDK v2 Responses client (`Azure.AI.Projects` 2.0.1,
      `Azure.AI.Extensions.OpenAI` 2.0.0) instead of raw `HttpClient`; agents are addressed by `agent_reference` (a prompt agent
      on the project-level route; hosted agents on their own endpoint since M11f).
    - **M9c** — the `Task.WhenAll` fan-out is replaced by a Microsoft Agent Framework workflow
      (`Microsoft.Agents.AI.Workflows` 1.25.0) in `Compass.Bot.Orchestration`: start → three branch executors → fan-in
      barrier → compose. Identity work (person resolution, OBO exchanges) stays in `GetMemberBriefingQueryHandler`.
    - **M9d** — docs.

11. **M11 — streaming, route switch, web front door, and the follow-ups from the first live run.** Detail in the README
    (deviation 12) and `LIVE-FINDINGS.md`: Teams streaming and the `SHAREPOINT_ROUTE` switch; the web front door; **M11f** hosted
    agents called on their own endpoint; **M11g** review fixes (in-stream claims retry, exact route values, localhost redirect
    for `dev` only); **M11h** the `AuthorityEndpoint` placeholder fix and the readiness check; **M11i** real knowledge citations
    and a composer that shows only what ran; **M11j** the Foundry Knowledge page connection; **M11k** per-turn branch scope.

*(Phase 2, optional, not scheduled: add the §7 indexed-source and Search-ACL paths as additional prompt agents if
Zava Credit Union wants to see them running side by side later.)*

---

## 16. Open risks / verify-before-build

- **Package IDs** for "Microsoft Agent Framework" and the Foundry SDK's hosted-agent adapters
  (`Azure.AI.AgentServer.Responses`, `Azure.AI.AgentServer.Invocations` per the brief's citation) — confirm current
  names/versions on NuGet.org and Microsoft Learn; these are fast-moving preview surfaces. **Resolved for this build:** the
  agents use the Responses protocol only (`Azure.AI.AgentServer.Responses`); the Invocations package is not used.
- **MediatR licensing** — confirm acceptable before scaffolding (D10).
- **Aspire Azure App Service / Functions hosting integrations** — confirm GA status and exact API shape (D6, §11).
- **Azure Functions .NET 10 isolated-worker support** — confirm GA timing; may need to target the latest supported
  runtime instead (§4 footnote).
- **Retrieval API licensing** — each test user needs a Microsoft 365 Copilot add-on license (or pay-as-you-go
  preview enabled on the tenant) for `SharePointRetrievalAgent` to return anything; provision this on the test
  tenant before M6.
- **ROPC in the test tenant** — only relevant to the optional live isolation suite: confirm tenant Conditional Access
  truly excludes the accounts it uses; ROPC fails silently in unexpected ways under CA/MFA. The live Teams demo does
  not need it.

---

## Appendix: source cross-reference

- Brief: `Zava-Compass-OBO-Teams-to-SharePoint.md` (all section numbers cited above refer to this file; §5 there is the
  source for the §7 comparison table above).
- Diagrams (in `diagrams/`): `Zava-Compass-OBO-Sequence.html`/`.png` (time view), `Zava-Compass-OBO-Architecture.html`/`_L1/_L2/_L3.png`
  (C4 structure) — these already show the full six-option SharePoint comparison and the ruled-out paths; no changes
  needed for this scoped build.
- Model source: `diagrams/model.json` (feeds `diagrams/build_sequence.py` and the architecture HTML) — unchanged by this build.
