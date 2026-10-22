# Decisions, evidence and lessons

The "how did you…?" and "why didn't you…?" file. Every entry gives a short answer you can say out loud, then the evidence
behind it, what else we tried or considered, and what production would change. It is a living document: when something is
proven live, or proven wrong, change the status mark and the date, not the story.

**Status marks**

| Mark | Meaning |
| --- | --- |
| **[observed]** | We saw it happen in this build (tenant, logs, command output). Date given. |
| **[documented]** | Microsoft Learn or a named reference says so; we have not reproduced it. Source given. |
| **[open]** | Not yet proven here. Never present an [open] item as working. |

Environment for everything marked [observed]: one sandbox subscription in a tenant under a governance policy initiative, one resource
group, a single Azure region, 17–18 October 2026 (later dated items say so).

---

## Part A — Why the architecture has this shape

### A1. Why our own bot, and not Foundry's one-click "Publish to Teams"?

**Say:** "Because the SharePoint requirement is *as the person*, and for that something has to hold the person's token.
Foundry's publish gives you a bridge from Teams to the agent; it doesn't give you the person's token. Our bot does."

- **[documented]** Learn's own prerequisite for acting as the user is "a trusted application backend that authenticates
  users and can perform OBO as a confidential client" (*Use on-behalf-of flow with hosted agents*). Foundry "doesn't exchange
  or refresh" the token; the application does.
- **[documented]** A Foundry-published agent in Teams: "Published agents don't support streaming responses or citations";
  the built-in SharePoint tool "doesn't work when the agent is published to Microsoft Teams"; and Learn does not establish
  whose identity a published agent's tools receive (*Publish agents*, *tool authentication*).
- **[observed, other environment]** The Zava credit memo built exactly that Teams path (a hosted agent serving the Activity
  protocol itself) and, with no backend in the path, had to **withhold its Policy section in Teams** because no Search token
  could reach it. Its web front door, which has a backend (a BFF), had full capability. Compass's bot plays the BFF's role
  for Teams.
- **[documented]** Microsoft names our shape as supported: "Integrate with Microsoft 365 using Agents Toolkit … a proxy app
  … ideal for scenarios requiring custom logic, SSO" (*Custom engine agents for Microsoft 365*).

*Alternatives:* publish a hosted agent (loses the person's token); an autopilot (A2). *Production:* the bot is a trust
boundary: it can assert any person to the agents its role covers, so scope it to the agents it must call.

### A2. Why not an autopilot / Entra Agent ID agent?

**Say:** "An autopilot acts as itself. We need an agent that acts as the person."

- **[documented]** "An autopilot acts as itself under its own identity rather than on behalf of a user"; it has its own agent
  user account and consumes a licence per instance; prompt agents can't be autopilots (*Agent 365 integration with Foundry*,
  *Quickstart: Build your first autopilot*).
- **[open]** Whether a Foundry-published agent can obtain the Teams user's delegated token through Entra Agent ID is not
  documented. Treat as research, not as a plan.

### A3. Why one on-behalf-of exchange per system, and a federated credential instead of a secret?

**Say:** "A token for Graph is useless at our own API, and should be. And there is no secret anywhere to leak or rotate."

- **[documented]** OBO "changes the token's audience without changing which user is represented"; a token for the middle
  tier can't be used against Graph (*Use on-behalf-of flow…*).
- **[observed]** The bot app registration trusts the bot's managed identity through a federated credential
  (`compass-bot-mi-dev`); there is no client secret in the repo, Key Vault or app settings.
- **[observed]** Admin consent for the three downstream scopes (Graph Files.Read.All/Sites.Read.All, Azure AI Search
  user_impersonation, Zava API Member.Read) was granted once, tenant-wide, so no employee sees a prompt. The grants are
  `AllPrincipals` and were verified afterwards; the consent script itself also needs Graph PowerShell modules that were not
  installed in the shell we used, so the grants were applied through Graph REST.
- **[observed]** Teams SSO needs the Teams client IDs pre-authorized on the bot app and a second federated credential for the
  Bot Service OAuth connection (`compass-bot-teams-sso-dev`).

### A4. How does the person travel from the bot to Foundry?

**Say:** "Three different things on the wire, and people confuse them constantly."

- **[documented]** The bot's own `https://ai.azure.com` token authorizes the call; `x-ms-user-identity` carries the person's
  object id (an id, not a token); each `x-client-*` header carries one downstream token, without a `Bearer` prefix. Foundry
  forwards `x-client-*` to the container and drops the caller's `Authorization`.
- **[observed]** One class (`FoundryHostedAgentGateway`) is the only place that sets `x-ms-user-identity`, so there is one
  place to audit.
- **[observed]** The role that lets the bot do this is a data action,
  `…/agents/endpoints/UserIdentityImpersonation/action`. Learn says it "isn't included in built-in roles", but on 18 Oct
  2026 `az role definition list` shows a built-in role named **Foundry Agent User Identity Impersonation** holding exactly that
  action. We still assign our custom role (scoped to the project). **[open]** Switch to the built-in role and delete the custom
  one, and fix the wording wherever we say "no built-in role has it".

### A5. Why two hosted agents and one prompt agent?

> Since 19 Oct the project also holds `LearnDocsAgent`, a minimal prompt agent whose only tool is the public Microsoft Learn MCP server (`samples/LearnDocsAgent`). It is a teaching baseline for the talk, not part of Compass's runtime.
> The same agent now lives as a project at `src/Agents/Compass.Agents.Prompt.LearnDocs` (builder, provisioner, client, tests).

**Say:** "Hosted agents are your code and get your tokens as headers. A prompt agent is declarative and doesn't. Each branch
got the one it needed."

- **[documented]** Hosted agents receive forwarded `x-client-*` headers; prompt agents have no container and no headers.
- **[observed]** Our knowledge agent is a prompt agent, so the Search token reaches it as a **structured input**
  (`search_auth_token`). **That is a smell.** Learn says: "Don't pass secrets, access tokens, or other credentials as
  structured inputs" (*Structured inputs*). **[open]** Replace it (see A6 and Part D).
- **[observed]** Hosted agents ship as a source zip on a .NET 10 runtime using the `Azure.AI.AgentServer.Responses` adapter
  (1.0.0-beta.6), no container registry; versions published with `Compass.Tools.PublishAgents`. Both agents reached `active`
  on 18 Oct 2026.

### A6. Which way to SharePoint, and why?

**Say:** "Three honest options and two we ruled out. The knowledge base and the Retrieval API ask the same engine; they differ
in who writes the plumbing."

| Option | Verdict | Evidence |
| --- | --- | --- |
| **Copilot Retrieval API** from a hosted agent (route A) | Built | **[documented]** Queries SharePoint live as the person. Needs a Microsoft 365 Copilot licence (or pay-as-you-go) per caller. Hybrid search only for `.doc/.docx/.pptx/.pdf/.aspx/.one`; 200 requests per user per hour; 25 results. **[observed]** The seed script converts demo files to `.docx` for this reason. |
| **Foundry IQ knowledge base**, remote SharePoint source (route B) | Built, **primary** | **[documented]** The remote source "uses the Copilot Retrieval API (preview)", so licence, limits and permissions are the same as A, plus Azure AI Search; preview. **[observed, 18 Oct]** Run live as Daniel and Maya through the web page: each gets answers only from documents they may read, with real linked sources (B3). |
| **Indexed** SharePoint source | Ruled out | **[documented]** Preview ACL ingestion; permissions are as of the last sync, so a revoked person can still read until the next run. |
| Foundry **SharePoint tool** | Ruled out | **[documented]** "Doesn't work when the agent is published to Microsoft Teams." |
| Toolbox **`user-entra-token`** to the knowledge base (the Heenanl reference) | Ruled out | **[documented, third-party reference]** The person becomes the *caller* of Azure AI Search, so every end user needs the **Search Index Data Reader** Azure role. We don't give employees Azure roles. |

- **Retracted, then measured.** Earlier material said route B is "slower per query" with no data behind it. **[observed, 18 Oct]** Now measured
  from the progress events, as Daniel in one session: route A (Graph retrieval) 3.5 to 7.3 s (n=5, median about 3.6 s); route B (knowledge
  base) 7.8 to 12.0 s (n=6, median about 10.5 s). Roughly three times slower in our test. The likely cause is the extra model work on
  route B (query planning and synthesis in the knowledge base, then the prompt agent), which we did **not** isolate. Say "about three times slower
  in our test", never a benchmark. The Retrieval route also returned content for Daniel once the documents were indexed.
- **Why our caller model keeps people role-free:** the service (project identity) is the Search caller and the person's token
  rides in `x-ms-query-source-authorization`. The credit memo's note says the same: "Search needs both headers". **[observed, 18 Oct]**
  Daniel and Maya hold no role on the Search service, and both get answers. Only the people who open the Foundry portal's
  Knowledge page need Search roles (B3).
- **[open]** Better replacement for the structured input: code (a hosted agent or the bot), not a model, calls
  `knowledgebases/{kb}/retrieve` with the service identity in `Authorization` and the person's Search token in
  `x-ms-query-source-authorization`.

### A7. Why MCP on Azure Functions for our own data, and why do we validate the audience ourselves?

**Say:** "Our member API is ours, so we issue it its own audience, and the server checks that the token was meant for it."

- **[documented]** The MCP specification says a server must not forward a token it received to a different API.
- **[observed, other environment]** Foundry refuses to send a Microsoft-audience token to a custom MCP endpoint:
  `400 tool_user_error: Cannot pass Microsoft token to untrusted MCP endpoint` (credit memo, Fabric attempt). So our API needs
  its own app registration and audience; a Graph token is rejected at the Zava API (a test proves it).
- **[observed, customer, 1 Oct]** A toolbox `oauth2` connection put a separate sign-in and consent in front of each MCP server
  even though the bot already had the person from Teams. That is why Compass calls the MCP server from agent code with the
  person's token in a header.
- **[observed]** The MCP webhook is `Anonymous` at the platform; the JWT audience/issuer middleware is the gate. Production
  would put APIM or a private endpoint in front (Part C).

### A8. Why Microsoft Agent Framework, the Foundry SDK v2, and a hand-rolled mediator?

**Say:** "A workflow is a graph you can read, test and extend. The SDK gives typed agent addressing. The mediator is ours
because the popular one stopped being free."

- **[observed]** The workflow is start → the executors in scope for the question → fan-in barrier → compose; a fresh workflow per
  turn; any branch failure fails the turn with the original typed exception (fail closed). Tests cover order and failure. Which
  executors are in scope is decided per turn (A10).
- **[observed]** `AgentAdministrationClient.CreateAgentVersionAsync` publishes the prompt agent as an immutable version.
- **[observed]** MediatR's current major versions are commercial, so `Compass.Shared.Messaging` is a small in-repo mediator.

### A9. Why a web front door as well as Teams?

**Say:** "Two front doors, one backend. Teams is where people are; the web page proves the identity chain without Teams."

- **[observed]** On 18 Oct 2026 the web page was the first surface to complete a turn as a real person (Daniel and Maya, in both
  directions); a Teams one-to-one chat worked for Maya later that day (B7). A page that calls the same backend (the credit memo's BFF
  pattern) removes the dependency on Teams being healthy, which is why it stayed the primary live surface for the demo.

### A10. Why does a question only run the branches it needs?

**Say:** "Two gates, and both can only take work away: what this deployment allows, and what this question is about."

- **[observed]** Before any token is exchanged, a deterministic classifier reads the question and narrows the turn to accounts,
  policy or both, inside the deployment's route. An accounts question ran only "Reading your accounts"; a policy question ran
  only the knowledge base; a question that mentioned both ran everything (18 Oct, as Daniel). Tests cover which agents and tokens
  each question type uses, the route as a ceiling, and a policy question for a person with no linked member record.
- **Why a classifier before the exchange, not conditional edges in the graph:** MAF can route inside the graph, but tokens are
  exchanged before the workflow runs, so a skipped branch would still have had its token requested. Deciding first keeps the
  least-privilege property the route switch already had: a branch that is not built cannot run, and a token never requested cannot
  be misused.
- **Why deterministic, not a model:** it only chooses which already-permitted parts to show or run, so it can never widen access;
  a model would add a call, latency and a way to be argued with. The cost is brittle wording rules, and an unclear question simply
  keeps everything.
- **Side effect worth knowing:** the answer now shows exactly what ran. Before, an accounts question printed "I can't access your
  personal accounts" from the knowledge base, and a policy question printed the member's balances.
- **[open]** We did not measure a speed-up; the browser overhead dominated. Do not claim one.

---

## Part B — How we configured it, and what bit us

### B1. Identity plumbing
- **[observed]** Entra apps, federated credentials, consent and Teams pre-authorization are scripted and idempotent
  (`Setup-EntraApps.ps1`). Graph PowerShell's interactive sign-in fails in a shell with no window handle (WAM). Fix: the scripts
  accept `COMPASS_GRAPH_ACCESS_TOKEN` from a signed-in `az` session.
- **[observed]** Privileged roles are activated through PIM and expire: Global Administrator lapsed overnight, which silently
  greyed out the admin center's "Add agent". Tokens in a browser session don't pick up a new role until a fresh sign-in.
- **[observed]** Conditional Access evaluation means `az` tokens go stale after a role change; run `az login` again.

### B2. Foundry
- **[observed]** The first provision failed on an unregistered Foundry `applications` API version, then on a live schema
  mismatch ("Missing discriminator property [AuthorizationScheme]"). Fallback: `CREATE_AGENT_APPLICATIONS=false` and project-scope
  RBAC. **[open]** Per-agent application scope when the preview schema is fixed.
- **[observed]** The legacy `gpt-4.1` deployment was unavailable; the project uses `gpt-5.6-sol`.
- **[observed]** Publishing a hosted-agent version failed with `403 … does not have permissions for …/agents/write` when the
  .NET credential chain picked up a different identity than the signed-in sandbox administrator (an identity that does not
  exist in this tenant's directory). Forcing `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` made it use the administrator, who
  holds Owner plus Foundry User, and both versions published. Lesson: when a data-plane call is refused, check *which* identity
  the SDK actually used before changing any role.
- **[observed, 18 Oct]** The bot called its hosted agents on the project-level `/openai/v1/responses` route and Foundry answered 400:
  "Hosted agents can only be called through the agent endpoint". A hosted agent is reached at
  `…/agents/{name}/endpoint/protocols/openai/responses`. Our agents host the Responses protocol; Invocations is for custom payloads.
  The stable SDK (2.0.0) only targets the project-level route, so a small pipeline policy rewrites the URI. The earlier test had asserted
  the wrong route, so it passed.

### B3. Knowledge base
- **[observed, 18 Oct]** The knowledge base answers as the person. As Daniel: the debt-to-income ratio (43 percent, exceptions need
  written VP approval, never above 50) from *Consumer Lending Policy*, and "I don't know" for the delinquency watchlist. As Maya: the
  reverse. Documents became answerable about 30 minutes after they were uploaded.
- **[observed]** The prompt agent's inline marker reads `【6:0†source】`: the model names every source with the literal word
  "source", so the first briefings said "Sources: source" with no link. The Responses message carries `url_citation` annotations with
  the real SharePoint address, so sources are read from those (title from the file name), the markers are stripped, and Teams and the
  page get a working link.
- **[observed]** The seed script reported "Skipped (exists)" for files that were never uploaded; Daniel's library was empty when opened
  in the browser. Upload by hand if that happens, and always open the libraries after seeding.
- **[observed]** The Foundry portal's Knowledge page was empty although `zava-kb` existed: the page lists knowledge bases only for an
  AI Search resource connected to the project, and our only connection was the `RemoteTool` MCP connection. A `CognitiveSearch`
  connection with Entra auth (`zava-foundry-iq-search`) fixes that; the service has key auth disabled. Only people opening the page need
  Search roles. **[observed, 18 Oct]** After the connection the page lists `zava-kb`, and its detail page shows the model, answer synthesis,
  the instructions and the source `zava-sharepoint` (Microsoft SharePoint (Remote), Active). *Retrieval reasoning effort* shows as unselected
  although the service says `auto`; do not press Save in the portal during a demo.
- **[observed]** `Setup-KnowledgeBase.ps1` creates the remote SharePoint source, the knowledge base and a Foundry connection
  (`ProjectManagedIdentity`, audience `https://search.azure.com/`) over the preview API `2026-08-01-preview`. It now falls back
  to an `az` token when `Az.Accounts` isn't installed.
- **[observed]** SharePoint needs time to index new files before any retrieval returns them; plan 15 minutes to an hour after
  seeding, and ask the shared FAQ question first as a control.

### B4. MCP server on Azure Functions
- **[observed]** The first deploy failed with a storage **403**: a governance Modify policy forces storage
  `publicNetworkAccess = Disabled`, which the Flex Consumption deployment can't use. Resolution: tag the resource group
  `SecurityControl = Ignore`, then re-write the storage account (Modify policies act at write time). In production you would use
  private endpoints instead of opting out.

### B5. The bot host (App Service)
- **[observed]** **Why App Service at all:** the bot is the only component that is neither an MCP server nor a Foundry agent, and
  it must hold the on-behalf-of exchange. Foundry can't host it (a hosted agent never receives the person's token). Container
  Apps would also work (the credit memo's BFF runs there); App Service was chosen so a Visual Studio developer can right-click
  publish.
- **[observed]** **Cold starts break Teams.** On the Free (F1) plan there is no Always On; the app idles out and a cold start took
  about 55 seconds, longer than Bot Service waits. Use B1 or better with Always On.
- **[observed]** **Something resized our plan.** The Bicep asks for B1 with Always On and every deployment requested B1, but at
  01:36 PDT on 18 Oct the activity log shows a governance automation account updating the web app and the plan, and the plan
  was Free. We restored B1 and added a `CostControl=Ignore` tag. **[open]** Whether that tag stops it.
- **[observed]** Linux App Service rejects hyphens in app-setting names (`400 Bad Request`), so member mappings key on the
  object id without hyphens and the bot accepts either form.
- **[observed]** The agents SDK needs `Connections__ServiceConnection__Settings__AuthorityEndpoint` (or a well-formed tenant
  authority); without it MSAL threw "The authority … must be in a well-formed URI format" on the first Teams message.
  **[observed, 18 Oct]** The cause was a literal `<tenant-id>` placeholder shipped in `appsettings.json` for `AuthorityEndpoint`, which
  the SDK prefers over `TenantId`; the readiness check now reports it. **[observed, 18 Oct]** With the live
  `AuthorityEndpoint` app-setting workaround removed, Maya's Teams message still worked, so the code fix alone is sufficient.

### B6. Telemetry and redaction
- **[observed]** The redaction processors have to be registered **before** the exporters; the first version registered them after,
  so it redacted nothing. A test that captures every log, trace and exception and fails on any JWT-shaped string caught it.
- **[observed, 19 Oct]** Foundry tracing is **off until an Application Insights resource is connected to the project**: the Traces tab said
  "Create or connect an App Insights resource" and listed nothing, and the hosted agents exported nothing. We connected
  `appi-compass-dev-<suffix>` (project managed identity, role Monitoring Metrics Publisher on the resource). Then the hosted agent
  appeared in Application Insights (role `ZavaApiAgent`: `POST /responses`, exceptions, dependencies), Foundry's own
  `invoke_agent` span appeared (role `agentsv2`), and the Traces tab listed runs. **[documented]** The hosting library bundles
  the OpenTelemetry distro, and Foundry injects `APPLICATIONINSIGHTS_CONNECTION_STRING` into the container when project monitoring is
  on, with no code change (Learn: *Export hosted agent telemetry by using OpenTelemetry*). So the earlier claim that hosted agents
  emit no telemetry was true only because monitoring was off.
- **[observed, 19 Oct]** A prompt agent with a remote MCP tool (`LearnDocsAgent`) produced `invoke_agent`, one `execute_tool
  mcp_microsoft-learn.*` per MCP call, and the model `chat` span, with no code.
- **[observed]** The standard W3C trace id already crossed the Foundry hop before monitoring was connected: every one of 27 `ZavaApiAgent` calls
  shared an operation id with the MCP Function's requests. What was missing was the hosted agent's own spans. There is still no
  custom per-turn correlation id (production gap).
- **[open]** The hosted agent also exports a steady stream of telemetry about its own observability exporter (about 36 posts to an
  `observabilityService` OTLP endpoint in four minutes while idle, plus token-acquisition spans). Cause and switch-off unknown.
- **[open]** Whether our token-redaction processors run before the hosting library's exporter in the hosted agent. A search for `eyJ`
  and `Bearer ` across three days of telemetry found nothing, but only a test ping has gone through the hosted agent since connecting. Offline order is now tested; see B6a.
- **[observed, 20 Oct]** Agent Framework workflow telemetry is **opt-in**: spans appear only if the builder calls `WithOpenTelemetry()`; registering the `Microsoft.Agents.AI.Workflows` source is not enough. We had not, so there were no workflow spans. It is now on, with content recording off, and a test checks both. **[observed, 20 Oct 22:11 UTC]** Seen live: one operation id spans the bot, Foundry, the hosted agent and the MCP function, and the workflow and executor spans carry no message content.
- **[observed]** The bot's service name was `unknown_service:dotnet`; it is now set to `compass-bot`. Application Insights ingestion lags by a few
  minutes, so wait before concluding "nothing arrived".

### B6a. Agent 365 observability: findings and what was built (offline only; nothing run against a live tenant)
**What was built (option 1: use the hosting library's own exporter).** Export is **off by default**. `--agent365` on the publish tool sets
`FOUNDRY_AGENT365_TRACING_ENABLED=true` in both hosted agents. `AddAgent365Tracing` fails fast at startup, naming the missing variables
(never their values), if that flag is on but `FOUNDRY_HOSTING_ENVIRONMENT`, `FOUNDRY_AGENT_INSTANCE_CLIENT_ID` or `FOUNDRY_AGENT_TENANT_ID`
is missing. We add our own `invoke_agent` span around each request and an `execute_tool` span around the MCP call (ZavaApi) and the Graph
retrieval call (SharePoint), in `Agent365Telemetry.cs`. Application Insights export is unchanged and runs alongside. Content capture is off;
span status carries only the exception type name. The brief's token-resolver interface and `Agent365__*` settings were **not** built: the
library owns token acquisition, so there is nothing for them to configure.
**Redaction gaps closed.** The activity redactor now also covers `StatusDescription`; the log redactor replaces an exception whose text holds a
token with a redacted copy (`RedactedException`). Activity event tags (exception events) still cannot be mutated and stay a known gap; our code
adds none. Tests prove an exporter registered after the redactors never sees a token (and a control shows one registered before them does),
which closes the redaction-order **[open]** item in B6 for the offline pipeline. The live agent has not been re-checked.
**Still [open] (needs a live tenant):** who injects the two `FOUNDRY_*` variables at runtime and whether Foundry accepts user-set `FOUNDRY_*`
names (`--agent365` relies on it); whether the platform's own setting overrides our default-off; the items below marked **[open]**.

Findings from the decompiled packages. Evidence tags: **[observed]** read in the decompiled `Azure.AI.AgentServer.Core 1.0.0-beta.26` and `Microsoft.OpenTelemetry 1.0.0-beta.1`;
**[documented]** Learn or the package README; **[open]** cannot be settled offline.
- **[observed]** `Azure.AI.AgentServer.Responses` (beta.6) already brings `Microsoft.OpenTelemetry` transitively, with the Agent 365 runtime
  (exporter, `InvokeAgentScope`, `ExecuteToolScope`, `BaggageBuilder`) merged into that one assembly. No new package is needed.
- **[observed]** The hosting library itself calls `UseMicrosoftOpenTelemetry` while building the host. The distro throws
  "Multiple calls to UseMicrosoftOpenTelemetry on the same IServiceCollection are not supported", so **this repo must not call it**.
- **[observed]** The library already turns on its own Agent 365 export (app-only, `/observabilityService/...` route) when
  `FOUNDRY_HOSTING_ENVIRONMENT` is set, `FOUNDRY_AGENT365_TRACING_ENABLED` is `true` and `FOUNDRY_AGENT_INSTANCE_CLIENT_ID` is set. It acquires its
  token with `DefaultAzureCredential` on the instance managed identity. This very likely explains the idle `observabilityService`
  stream in B6. **[open]** Who sets those variables in the container (the platform or us) is unknown offline.
- **[observed]** Our `configure` callback, where `AddTokenRedaction()` runs, executes before the library's telemetry setup, so our
  redactors are registered ahead of the Agent 365 exporter. The library's `FoundryEnrichmentProcessor` is registered after the exporter and
  sets agent id and tenant id in `OnEnd`. **[open]** A test must prove the order, and whether the enrichment can lose a race with the
  batch export is unverified.
- **[observed]** The exporter runs on a batch thread, never on the request path; it catches token and HTTP failures. It does log exception
  objects, which pass through our log redactor. Agent 365 receives traces only.
- **[observed]** The distro copies baggage keys, including content keys (`gen_ai.input.messages` and others) and PII keys (`user.email`,
  `user.name`), onto spans. Never put those in baggage. Our baggage is limited to tenant, agent and conversation ids.
- **[documented]** Spans are accepted only if the URL agent id, the token `appid` and each span's `gen_ai.agent.id` match, and the tenant
  matches. A tenant with no assigned M365 E7 or Agent 365 licence returns 200 but drops spans (`tenant_not_licensed`). An agent instance
  registered with Agent 365 needs no `Agent365.Observability.OtelWrite` role; an unregistered identity needs that role plus admin consent.
  Creating an Entra agent identity alone does not register the instance.
- **[open]** Whether the managed-identity token the library acquires is accepted by Agent 365 (Learn's S2S recipe is a different, two-step
  agent-identity token chain), and whether the library validates it as app-only.
- **[open]** The idle-stream cause may instead be a feedback loop (HTTP instrumentation of the exporter's own posts).

### B7. Teams packaging and publishing (status: **[observed]** one-to-one works for Maya and Daniel, and a group-chat @mention works, and Microsoft 365 Copilot works)
- **[observed]** `developer.name` over 32 characters is rejected by the Teams admin center
  ("Schema validation failed at 'developer.name'").
- **[observed]** A hand-built manifest without `copilotAgents.customEngineAgents` and the `copilot` scope is not treated as an
  agent. Foundry will export the package it generates (`POST /agents/{name}/microsoft365/zip`); ours differed in exactly those
  two places. After adding them, Compass appeared in the Agent 365 registry (Available) and Teams listed its capabilities as
  "Agent" and "Bot". Learn agrees: custom engine agents need manifest 1.21+ with that declaration.
- **[observed]** First install: Teams delivered the message and the bot's reply was refused with **403 BotDisabledByAdmin**
  ("The tenant admin disabled this bot"). After the manifest fix: Teams shows messages delivered but **nothing reaches Azure Bot
  Service or the app**. Admin settings checked and ruled out: Manage apps (Unblocked, Everyone), Agent and plugin access, User
  access, agent management rules; permission policies no longer exist.
- **[observed]** The credit memo's Foundry-published bot works in this same tenant; ours does not. The difference is how the
  Teams app and bot identity are created.
- **[observed, 18 Oct]** Maya's one-to-one chat now works end to end: a streaming "Checking your member record…" status, then her briefing,
  with only her own accounts. What changed before it worked: the `AuthorityEndpoint` placeholder was fixed (B5), and at about 21:18 UTC one
  reply still failed with `403 BotDisabledByAdmin` and did not recur. We changed nothing in the Teams admin center, so **we cannot say
  which of the two mattered, or whether both did.** **[open]**
- **[observed, ruled out]** The Azure Bot's Teams channel `acceptedTerms=false` (the working credit memo bot has the same), `disableLocalAuth`,
  and the manifest (a test package with a separate Teams app id, no SSO block and wider scopes delivered, but so did the original package).
  A message sent through the bot's Web Chat channel reached the app.
- **[observed]** Admin center behaviour: a user's custom-app upload can become a submission that an admin must publish; uploading as an
  admin (Manage apps, Upload new app) published directly; installing for specific people failed with a generic error (cause
  **[open]**).
- **[observed, 18 Oct evening]** Daniel's Compass chat, which had said "You cannot send messages to this bot", now works: his own accounts and
  balance came back at 6:21 PM PDT, and nothing of Maya's. The two people's Teams admin center policies (all 23 visible types Global) and Compass
  app listing were identical, so admin configuration was never the difference. **[open]** What cleared it: the manifest 1.0.1 upload,
  time, or a client refresh; we cannot say which.
- **[observed]** Group chats: manifest 1.0.1 adds the `groupChat` scope. Daniel added Compass to a chat and @mentioned it; it replied
  with a status line and then his briefing (one reply, not a stream). **Design note:** the reply is visible to everyone in the chat, so
  member data belongs in a one-to-one chat.
- **[observed, 18 Oct evening]** Microsoft 365 Copilot: Daniel opened Compass there and asked for his accounts and balances; a status line and then his own
  briefing came back (one reply, no streaming). The manifest declares Compass as a custom engine agent (earlier bullet in B7). A policy question in Copilot, and its citations, are not yet seen. **[open]**

---

## Part C — What a governed tenant did to us (and what production would do instead)

| What happened | When | Lesson |
| --- | --- | --- |
| Policy forced storage public access off; Function deploy 403 | 17 Oct | Opt-out tag in a sandbox; private endpoints in production |
| Governance automation resized the bot's plan to Free | 18 Oct, 01:36 PDT | Treat sandbox config as mutable; assert the plan and Always On in the readiness checks |
| A data-loss-prevention policy blocked files hourly | 18 Oct | A module installed with `-Scope CurrentUser` landed in a OneDrive-redirected folder; install tooling outside synced folders |
| PIM Global Admin expired | 18 Oct | Plan privileged work in windows; re-sign-in after activation |
| Preview APIs changed under us | 17 Oct | Pin API versions, keep fallbacks, say "preview" out loud |
| A custom Teams app needed an admin to publish it, and one reply was refused as "BotDisabledByAdmin" | 18 Oct | Know where the admin center holds submissions and app status before the demo; check both when a bot goes quiet |

**What production adds (not built):** VNet and private endpoints; APIM / an AI gateway in front of Foundry and MCP; content
filters and prompt-injection policy; Sentinel detections and a correlation id; resilience policies; multi-region; per-agent
scoping and Entra Agent ID.

---

## Part D — Open questions, stated honestly

1. Why did Teams not deliver to our bot at first, and why can Daniel not message it now (B7)? Maya works. **[open]**
2. ~~Does route B (knowledge base) return correct, person-scoped answers live?~~ **[observed, 18 Oct]** Yes, for both people through the web page (B3).
3. Replace the structured-input token with a retrieve call using two headers (A6). **[open]**
4. ~~Is route B slower than route A?~~ **[observed, 18 Oct]** About three times slower in a small single-session sample (A6); the cause is not isolated.
5. Move to the built-in impersonation role (A4). **[open]**
6. Can a Foundry-published agent get a person's delegated token through Entra Agent ID (A2)? **[open]**, research only.
