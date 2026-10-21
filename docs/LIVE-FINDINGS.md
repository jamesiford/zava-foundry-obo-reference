# Live findings (18 October 2026)

What the first live run against a sandbox tenant (one resource group, one Azure region) taught us, and how
each finding is known. Every claim carries one of three tags:

- **[observed]**: seen directly in this run (a request, a log line, a screen, a command's output).
- **[documented]**: stated by Microsoft Learn or the SDK source; not independently reproduced here.
- **[open]**: not established. Where there is a guess, it is labelled as one.

Times are UTC unless stated. The sandbox's local time is PDT (UTC-7).

## 1. Hosted agents are called on their own endpoint (M11f)

| Finding | Tag |
| --- | --- |
| Foundry hosted agents expose the **Responses** protocol (`/responses`, OpenAI-compatible, conversational) or the **Invocations** protocol (`/invocations`, custom payloads); the agent chooses what it hosts. Ours use `Azure.AI.AgentServer.Responses`, so they speak Responses. | documented ([Host Agent Framework agents as Foundry hosted agents](https://learn.microsoft.com/azure/foundry/how-to/develop/framework-hosted-agents)); our agents' protocol observed |
| A hosted agent is reached at `…/agents/{name}/endpoint/protocols/openai/responses?api-version=v1`. | documented ([Manage hosted agent sessions](https://learn.microsoft.com/azure/foundry/agents/how-to/manage-hosted-sessions)); observed |
| The project-level route `…/openai/v1/responses` with an `agent_reference` returns **400** for a hosted agent: "Hosted agents can only be called through the agent endpoint". The bot was using it, and the member-data branch failed. | observed |
| The agent's own endpoint accepts the same SDK-shaped body, including `agent_reference`, and reached the container (the agent's own validation error came back). | observed |
| Stable `Azure.AI.Extensions.OpenAI` 2.0.0 has no agent-endpoint client; `GetProjectResponsesClientForAgentEndpoint` exists only in the 3.0.0-beta line (checked by reflection on the beta.5 package). | observed |
| Fix: `HostedAgentEndpointPolicy` rewrites the URI per call and the stable SDK stays. Prompt agents (the knowledge agent) keep the project-level route. The previous gateway test asserted the wrong route, so it passed. | observed |

## 2. A placeholder in `appsettings.json` broke every bot reply (M11h)

| Finding | Tag |
| --- | --- |
| `appsettings.json` shipped `Connections:ServiceConnection:Settings:AuthorityEndpoint` as `https://login.microsoftonline.com/<tenant-id>`. Bicep sets `TenantId` but never `AuthorityEndpoint`, so the literal placeholder was used. | observed |
| Result: the Agents SDK failed outbound token requests with "The authority (including the tenant ID) must be in a well-formed URI format" (App Insights, 31 ms, so a config error, not a network one). | observed |
| The readiness check did not catch it: it only complained when both `AuthorityEndpoint` and `TenantId` were placeholders. It now reports a placeholder `AuthorityEndpoint` even when `TenantId` is set, and the file ships `TenantId` as the placeholder instead. | observed (tests) |
| Setting `Connections__ServiceConnection__Settings__AuthorityEndpoint` on the live app removed the error. | observed |

## 3. Teams delivery: what was tried, and what is still open

Timeline (observed unless noted):

1. Messages sent in the Compass chat showed as sent but produced **no** `/api/messages` request (App Insights) between
   19:37 (the first message after the Teams channel was checked) and 21:12, apart from the install event below.
2. At 20:33 a Teams install event did reach the app and failed with the authority error above.
3. At 21:18 Teams requests were arriving, and one reply failed with **403 `BotDisabledByAdmin`**: "The tenant admin disabled
   this bot". It did not recur.
4. At 22:08 (3:08 PM PDT) Maya's one-to-one chat worked end to end: a streaming "Checking your member record…" status, then
   her briefing with only her own accounts.

Ruled out, with evidence:

| Candidate | Result |
| --- | --- |
| Teams channel `acceptedTerms=false` on the Azure Bot | The working Zava credit memo bot also has `false`. Setting `true` was not shown to matter. |
| `disableLocalAuth: true` on the bot (the credit memo bot has `false`) | Flipped to `false` for one test with no change, then restored. |
| Teams app manifest differences (separate Teams app id, no SSO `webApplicationInfo`, wider scopes) | A test package with all three changes delivered, but the app named Compass from the original package (Teams app id equal to the bot id, SSO, identity permission, personal scope) also works for Maya. Not the cause. |
| Azure Bot messaging endpoint, Teams channel enabled, manifest bot id | All correct. A message sent through the bot's Web Chat channel reached the app (three requests, 202). |

Authority workaround removed (18 Oct, 17:28 PDT): the live app's `Connections__ServiceConnection__Settings__AuthorityEndpoint`
setting was deleted so only the code fix remained (`/health/ready` 200, no `Authority`-named setting except the front door's
`Bot__Auth__Authority`). Maya then asked "What are my accounts and balances?" in Teams and got her own two accounts and net
balance, streamed, with no knowledge-base text because the question was accounts-only (App Insights: `POST /api/messages` 202,
then the streaming request completed in 22.6 s). **Observed: the code fix alone works; the app setting was redundant.**

Two things that look causal but are not proven:

- The authority fix and the clearing of the admin block both happened between the failing and the working period. The
  authority fix is now shown to be sufficient for replies working (above), but whether the one-off `BotDisabledByAdmin` at
  21:18 mattered is **[open]**.
- Nothing was changed in the Teams admin center by the operator, so why the bot reported `BotDisabledByAdmin` once is
  **[open]**.

Daniel and group chats (18 Oct, evening):

- **Daniel in Teams now works. [observed]** His Compass chat had shown "You cannot send messages to this bot" (later "Chat is turned off for this
  meeting") after reinstalling the app, while Maya's worked. At 6:21 PM PDT he asked "What are my accounts and balances?" in the one-to-one chat
  and got his own briefing (ZCU-100234: Everyday Checking, Auto Loan, Primary Share Savings, net balance 7,357.77), with no knowledge-base
  text. Maya's accounts did not appear.
- **What fixed it is not known. [open]** In the Teams admin center both users had identical policies (all 23 visible policy types Global /
  org-wide default) and an identical Compass app listing (Zava Credit Union, Everyone), so admin configuration did not differ **(observed)**.
  The manifest 1.0.1 package was uploaded to the existing app shortly before, and the app had been deleted and re-added several times, so a
  per-user client or catalog refresh is the guess. We cannot say whether the upload, the passage of time, or both made the difference.
- **Group chat with an @mention works. [observed]** Daniel added Compass to a chat with the System Administrator account and sent
  "@Compass What are my accounts and balances?" at 6:22 PM PDT. Compass replied "Working on your briefing. I'll reply here shortly." and then
  posted his briefing. App Insights shows the matching POST /api/messages requests returning 202, with no exceptions in that window.
  A group chat gets one status line and one reply, not the streaming status of a one-to-one chat.
- **Group chats show the reply to everyone in the chat. [observed, design note]** The briefing is built from the person who asked, but it is
  posted into the shared chat, so everyone in it can read it. The one-to-one chat is the right surface for member data; a group chat suits
  non-sensitive questions. Production would decide whether to answer member-data questions in a group at all.
- **Microsoft 365 Copilot works. [observed]** At about 7:36 PM PDT Daniel opened Compass in the Microsoft 365 Copilot app (his licence shows
  M365 Copilot Premium), asked "What are my accounts and balances?", and got "Working on your briefing. I'll reply here shortly." followed by his own
  briefing (ZCU-100234, net balance 7,357.77). App Insights shows the matching POST /api/messages requests returning 202 and no exceptions in
  the window. Like a group chat, Copilot gets one status line and one reply, not the streaming status. The reply is not
  streamed and carries no citation chips in this screenshot; this question was accounts-only, so none were expected.

Teams admin center behaviour worth knowing **[observed]**:

- A custom app uploaded by a user can become a **submission** ("request created") that an admin must publish. The Publish
  button was greyed out for one submission; uploading the package as an admin (**Manage apps → Upload new app**) published it
  directly.
- Installing a published app for specific users from the admin center failed with a generic error ("We couldn't install this
  app"). The cause was not established; one guess is that the people already had an app with the **same bot id** installed,
  since every package shares it. **[open]**
- A newly published org app was not listed under **Built for your org** for a user several minutes after publishing.

## 4. SharePoint demo data

| Finding | Tag |
| --- | --- |
| `Seed-SharePointTestData.ps1` printed **Skipped (exists)** for all five files while Daniel's *Lending Policy* library was **empty** (opened in the browser as Daniel). The existence check treats "no error" as "exists", and `Get-PnPFile -AsListItem` can return nothing for a missing file instead of throwing. Reproduced on 21 Oct 2026 from a clean clone on a brand-new site (every library empty, every file "Skipped (exists)"); the cause is confirmed: `Get-PnPFile -AsListItem` returns `$null` in PnP.PowerShell 3.4.1. The script now tests for `$null`, and the same run then uploaded all five files. Still check the libraries in the browser before trusting the output. | observed (reproduced and fixed) |
| The libraries, their unique permissions and Daniel's access to *Lending Policy* were correct. | observed |
| Uploading the five `.docx` files by hand worked. They can be generated without PnP by dot-sourcing `Compass.DemoDocuments.ps1` and calling `ConvertTo-DocxByteArray` on each Markdown file. | observed |
| After the upload, Daniel's policy question was answered from the documents within about 30 minutes. | observed |
| Both demo people hold Microsoft 365 Copilot (checked through Graph licence details). | observed |
| Installing `PnP.PowerShell` into the CurrentUser module path put its help files inside OneDrive and triggered Purview DLP "File Blocked" emails. Install it for all users, or outside OneDrive. | observed |

## 5. Knowledge-base answers, citations and what is shown

| Finding | Tag |
| --- | --- |
| The prompt agent's inline marker read `【6:0†source】`: the model names every source with the literal word "source". The briefing therefore showed "Sources: source" with no link. | observed |
| The Responses message carries **`url_citation` annotations** with the real SharePoint URL (the title is the URL too). The `mcp_call` output also lists the documents the knowledge base returned. | observed (one call, made as the admin) |
| Fix: sources are read from the annotations (title = file name without extension, http(s) only), markers are stripped from the answer, and Teams and the web page get a titled, working link. The old marker parsing remains as a fallback and never yields the word "source". | observed |
| The composer used to print every branch's output, so an accounts question showed "I can't access your personal accounts" from the knowledge base, and a policy question showed the member's balances. | observed |

## 6. Per-turn branch scope (M11k)

The workflow's shape was fixed per deployment (`SHAREPOINT_ROUTE`). It is now also narrowed per turn:

- The handler classifies the question (`QuestionFocusClassifier`, deterministic, wording only) into accounts, policy, or both.
- `BriefingRoutePolicy.ScopeFor(focus)` returns a `BriefingScope`: the route's branches, minus those the question does not need.
  It can only remove branches, never add one the route forbids. An unclear question keeps everything the route allows.
- Tokens are exchanged only for the audiences in scope, the member record is resolved only when accounts are in scope (so a
  policy question needs no linked member), and `BriefingWorkflowFactory` builds only those branches.
- The composer shows exactly what ran, and drops a knowledge answer that is blank or "I don't know".

Observed live as Daniel: an accounts question ran only "Reading your accounts"; a policy question ran only the knowledge base;
a question mentioning both ran all branches. **[observed]** No speed-up from skipping is claimed; the browser overhead dominates.

**Route A against route B, measured (18 Oct, Daniel, route set to `Both` for the test, then restored).** The progress events carry each
branch's elapsed time. Graph retrieval (route A) took 3.5 to 7.3 s per question (n=5; the median was about 3.6 s). The knowledge base
(route B) took 7.8 to 12.0 s (n=6; the median was about 10.5 s). So route B was roughly three times slower in this sample, and the
Retrieval route returned content for Daniel (`From SharePoint:` with the document text) once the documents were indexed. A small, single-session
sample, not a benchmark. The likely cause is the extra model work on route B (query planning and answer synthesis in the knowledge base, then the
prompt agent), which was **not** isolated. **[observed (timing); open (cause)]**

## 7. The Foundry portal's Knowledge page

| Finding | Tag |
| --- | --- |
| The page was empty ("Connect to an AI Search resource") although `zava-kb` exists on the Search service. The only project connection was the `RemoteTool` MCP connection the agent uses, which the page does not list. | observed |
| Adding a `CognitiveSearch` project connection with Entra auth (`zava-foundry-iq-search`) is the portal's own way to show the knowledge base. The Search service has key auth disabled, so Entra is required. Created live and added to `Setup-KnowledgeBase.ps1`. | observed |
| After the connection, the Knowledge page lists `zava-kb`, and its detail page shows the description, the `gpt-5.6-sol` model, *Answer synthesis*, the retrieval and answer instructions, and the knowledge source `zava-sharepoint` (*Microsoft SharePoint (Remote)*, Active). The portal shows *Retrieval reasoning effort* as unselected although the service reports `auto`; saving from the portal was not tried, so do not press Save during a demo. | observed (screenshot, 18 Oct) |
| Only the people who open the page need **Search Index Data Reader** and **Search Service Contributor** on the service (granted to the admin and the operator). **End users need no Search role**: Daniel and Maya have none, and their briefings work, because the agent reaches the knowledge base as the project managed identity and the person's own token only trims the SharePoint results. | observed |

## 8. Isolation, observed through the web front door

| Question | Daniel | Maya |
| --- | --- | --- |
| Maximum debt-to-income ratio (*Lending Policy*, Daniel only) | 43%, exceptions need written VP approval, never above 50% | "I don't know" |
| Delinquency watchlist (*Portfolio and Collections*, Maya only) | "I don't know" | Fresno Central 1.9%, Reno South 1.7% |
| Accounts | ZCU-100234 only | ZCU-100871 only |

Each person saw only their own accounts in every run. The shared *Member Services FAQ* answered for Daniel (wire cutoff 3:30 PM
Eastern); it was not asked as Maya. The live xUnit isolation variants (`COMPASS_TEST_TENANT_CONFIGURED`) were **not** run.
**[open]**

## 9. Smaller items

- **Claims challenge in the stream (M11g).** A step-up demand raised inside a branch arrives as an `error` event on a 200
  response, not a 401 header. The page now retries once with the decoded claims for either form, and the backend maps a broker
  failure that carries claims (JSON from MSAL, base64 on the wire) to the same challenge. Behaviour checked against a mocked
  page in Node and by tests; **not** triggered live. **[observed (offline)]**
- **Route values are exact.** `Enum.TryParse` accepted `KnowledgeBase,Retrieval` as `Retrieval`; only an exact route name is accepted now.
- **Localhost redirect URI** is registered only with `-LocalPort`; azd passes it for the `dev` environment only. Environments
  that already have it keep it until removed by hand.
- **Function App storage warning.** The MCP Function App logs `AuthorizationPermissionMismatch` for `AzureWebJobsStorage`
  every 30 seconds while the host is awake, although its identity holds the Blob, Queue and Table roles, the storage account allows
  public network access, and MCP calls succeed. Checked on 18 Oct: it is a Flex Consumption app (`dotnet-isolated` 10.0) whose deployment
  container and `AzureWebJobsStorage__*` settings both name the same user-assigned identity (`id-mcp-…`); the host created its
  `azure-webjobs-hosts`, `azure-webjobs-secrets` and `app-package` containers; the identity holds Blob Data Owner, Queue Data Contributor and
  Table Data Contributor with no condition; and Application Insights shows no failed storage dependency. The failing operation could not be
  identified from outside, so the cause is **[open]**. It does not affect requests.
- **Answers rendered as raw markdown on the web page.** The page showed the briefing as plain text, so the account list appeared as "- …"
  lines and the knowledge base's headings and bold appeared literally. The page now builds paragraphs, lists, headings and bold from DOM
  nodes and text nodes only. A deployed script change also reached an open browser late, because static files had no `Cache-Control`;
  they are now `no-cache`. Both are in PR #13. **[observed]** Teams rendered Maya's answer (account list and bold) correctly.

## 10. Still to verify

- Whether our redaction runs ahead of the hosted agent's exporter (one clean turn so far, which is weak evidence), and why the knowledge branch took 23.8 s in the first turn after idle (section 11).
- Microsoft 365 Copilot with a policy question (the one test was accounts-only), so citations there are not yet seen.
- What cleared Daniel's "cannot send" state (the 1.0.1 upload, time, or a client refresh), and why BotDisabledByAdmin appeared once.
- Whether member-data questions should be answered in group chats at all.
(The authority fix alone is shown to be sufficient for replies, see section 3.)
- The live xUnit isolation variants; the claims-challenge retry against a real step-up demand.
- The Function App storage warning.

## 11. Traceability and the minimal MCP agent (19 Oct)

Reason: the afternoon workshop asks attendees to "confirm security and traceability", so we checked what we can actually show.

| Finding | Tag |
| --- | --- |
| The Foundry project had **no Application Insights connection**. The Traces tab for `ZavaApiAgent` said "Create or connect an App Insights resource to enable tracing" and listed nothing. | observed |
| Learn: server-side tracing needs no code; connect Application Insights to the project (Agents, Traces, Connect). Hosting libraries bundle the OpenTelemetry distro and Foundry injects `APPLICATIONINSIGHTS_CONNECTION_STRING` once monitoring is on. | documented |
| We connected `appi-compass-dev-<suffix>` using the **project managed identity** (the other option is an API key) and granted that identity Monitoring Metrics Publisher on the resource. The connection is `appi-compass-dev-<suffix>-9782` (category AppInsights). | observed |
| Afterwards the hosted agent appeared in Application Insights (role `ZavaApiAgent`: `POST /responses`, the exception for a malformed test input, many dependency and trace rows), Foundry's `invoke_agent` span appeared (role `agentsv2`), and the Traces tab listed runs. | observed |
| The hosted agent exports a steady stream of telemetry about its own exporter: about 36 posts to an `observabilityService` OTLP endpoint in four minutes while idle, each with a token-acquisition span. About 590 traces and 290 dependencies in 25 minutes. Cause and switch-off unknown. | observed / open |
| Before connecting, the standard trace id already crossed the Foundry hop: all 27 `ZavaApiAgent` calls shared an operation id with the MCP Function's requests. The hosted agent's own spans were missing. | observed |
| The bot's service name was `unknown_service:dotnet`. The bot now sets `compass-bot` (deployed 19 Oct); the role `compass-bot` appears in Application Insights with requests, dependencies and traces (319 rows in 6 hours on 20 Oct). | observed |
| **Agent Framework workflow spans did not appear, and the cause was ours.** Listening to the `Microsoft.Agents.AI.Workflows` source was not enough: workflow telemetry is **opt-in** through `WorkflowBuilder.WithOpenTelemetry()`, which `BriefingWorkflowFactory` never called. A 20 Oct turn from Daniel in Teams (21:42 UTC) showed `compass-bot` and Foundry's `responsesapi` spans but no workflow spans. A small standalone program reproduced the fix: with `.WithOpenTelemetry()` it emitted `workflow.build`, `edge_group.process`, `message.send`, `executor.process` and `workflow_invoke`. | observed |
| `.WithOpenTelemetry()` is now in the factory with content recording left off (`EnableSensitiveData` unset), and `WorkflowTelemetryTests` asserts that executor spans are emitted and that no span carries `executor.input`, `executor.output` or `message.content`. Deployed 20 Oct (after the Daniel turn). **Seen live** in the next turn (below). | observed (test and live) |
| **A live turn on 20 Oct, 22:11 UTC (Daniel, Teams, a question about both accounts and policy), is one trace.** One operation id covers `compass-bot` (1 request, 31 dependencies), Foundry's `agentsv2` (the `invoke_agent` request) and `responsesapi` (3 spans for the prompt agent), `ZavaApiAgent` (the hosted agent: 1 request, 7 dependencies) and `func-mcp-compass-dev-<suffix>` (7 requests, 4 dependencies). | observed |
| In that turn the bot's workflow spans appear in order: `workflow.build` (0.28 s), `workflow.session` and `workflow_invoke` (24.0 s), `executor.process briefing-start`, `executor.process sharepoint-knowledge` (23.8 s), `executor.process zava-api` (10.8 s) and `executor.process briefing-compose`. The executor spans carry only `executor.id`, `executor.type` and `message.type`: no `executor.input`, `executor.output` or `message.content`. | observed |
| That turn took 34.9 s end to end, with the knowledge branch at 23.8 s (earlier measurements were about 10 s). A hosted-agent `GET /readiness` probe appears just before its `POST /responses`, which is consistent with a cold start after idle. Not isolated. Warm the system up before a live demo. | observed / open |
| A search for `eyJ` and `Bearer ` across requests, dependencies, traces and exceptions in the 40 minutes covering that turn, which reached the hosted agent and the MCP function, found nothing. It covers one turn and JWT-shaped or `Bearer` strings only; it does not prove the redaction runs ahead of the hosting library's exporter. | observed (one turn) |
| The 21:42 UTC Daniel turn went through the bot and the prompt agent only (Foundry `responsesapi` spans), so the hosted agent and the MCP function were not exercised. The hosted-agent token check therefore still rests on a test ping. A question that mentions both accounts and policy exercises every branch. | observed |
| No `eyJ` or `Bearer ` string in any request, dependency, trace or exception in the three days to 19 Oct, including the new agent telemetry. Only a test ping has gone through the hosted agent since connecting. | observed (weak for the hosted agent) |
| Two Learn pages disagree: the setup page says tracing is GA for prompt and hosted agents, the hosted-agent quickstart says it is in preview. | documented |
| **Minimal MCP agent:** `src/Agents/Compass.Agents.Prompt.LearnDocs` (first as the file-based `samples/LearnDocsAgent`, which it replaces) publishes a prompt agent whose only tool is the public Microsoft Learn MCP server (`https://learn.microsoft.com/api/mcp`; tools `microsoft_docs_search`, `microsoft_code_sample_search`, `microsoft_docs_fetch`). It answered with a Learn URL. | observed |
| Its trace: `invoke_agent LearnDocsAgent:1` (8.6 s), then `execute_tool mcp_microsoft-learn.microsoft_docs_search` (2.3 s), `execute_tool ..._docs_fetch` (2.3 s) and `chat` (0.8 s). | observed |
| `foundryProject.bicep` can now create the connection and role (parameter `appInsightsName`). **Built, not deployed**: the live connection was made in the portal, so a later `azd provision` would add a second connection with a different name. | built |
| Creating agents with `DefaultAzureCredential` failed with 403 (it picked another account); `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` fixed it. | observed |

## 12. Where the agents live, how they ship, how to debug them locally (21 Oct)

| Finding | Tag |
| --- | --- |
| The agents are code. Two hosted agents (`Compass.Agents.Hosted.SharePointRetrieval`, `.ZavaApi`) are ASP.NET apps whose `Program.cs` calls `ResponsesServer.Run`; two prompt agents (`Compass.Agents.Prompt.SharePointKnowledge`, `.LearnDocs`) are a definition published with `CreateAgentVersionAsync`. Nothing about the agents was created in the portal. | observed |
| `tools/Compass.Tools.PublishAgents --what-if` prints the plan, offline: `dotnet publish` for linux-x64 (framework-dependent), the SDK zips it, a version is created (runtime `dotnet_10`, entry point `dotnet <assembly>.dll`, bundled dependencies, Responses 2.0.0, 0.5 CPU, 1 GiB, `ZavaApi__McpEndpoint` baked into the ZavaApi version), then it polls until active. | observed |
| Learn recommends `azd` or the Foundry Toolkit for VS Code for most source-code deployments, and says the .NET source-code deployment APIs are only in a prerelease package. | documented |
| `dotnet run --project src/Agents/Compass.Agents.Hosted.ZavaApi` starts the Foundry hosting adapter on `http://localhost:8088`: `/readiness` returns `Healthy`, and `/responses` accepts a request and returns the same typed failures as in Foundry. No cloud needed. | observed |
| A typed request (`{ "memberNumber": ... }`) with no token returns `status: failed`, `code: missing_token` ("Required downstream token header 'x-client-zava-api-token' is missing; refusing to call the downstream service."). `tools/Call-LocalAgent.ps1` makes the call. | observed |
| Locally the person id comes from `x-agent-user-id`. `x-ms-user-identity`, which the bot sends to Foundry, does not reach a handler (the log showed an empty person until `x-agent-user-id` was sent). `AgentCallContextFactory` lists both as fallbacks, so the second never fires through the real adapter. Harmless (the agent fails closed either way), but the comment overstates it. | observed |
| With a fake token present the call returns `server_error`, because the agent then tries the MCP server at its default `http://localhost:7071/runtime/webhooks/mcp`, which was not running. | observed |
| **The committed launch profile crashed the hosted agents.** `dotnet run --project src/Agents/Compass.Agents.Hosted.ZavaApi` (which uses `Properties/launchSettings.json`, profile `local`, `ASPNETCORE_ENVIRONMENT=Development`) died at startup with `Cannot resolve scoped service 'Azure.AI.AgentServer.Responses.ResponseHandler' from root provider`: in Development ASP.NET Core validates scopes, and the adapter's `MapResponsesServer` resolves its scoped handler from the root. It was missed because the first local tests used `--no-launch-profile` (Production). Found when someone other than the author ran the commands. The profiles for both hosted agents now say Production, as in Foundry; verified with the profile and with `FOUNDRY_PROJECT_ENDPOINT` set. Visual Studio F5 uses the same profile, so it would have failed the same way. | observed |
| F5 in Visual Studio with a breakpoint in `HostedAgentResponseHandler`, and debugging an isolation test (`HostedAgentInProcessExtensions`, TestServer) with a breakpoint in an agent class, are expected to work and have **not** been verified. | open |
| Agent Inspector (Foundry Toolkit for VS Code) connects to a local agent (default `localhost:8088`), shows requests and tool calls, and for supported Agent Framework workflows shows the execution graph and source locations. `azd ai agent run` starts the agent and opens the inspector. Learn's samples are Python; we have not tried either with our .NET agents. A prompt agent has no local runtime. | documented / open |
| The AppHost (`apphost/Compass.AppHost`) runs the bot (and so the workflow) and the MCP Function with an Azurite emulator. It does not run the Foundry agents; its comment says Aspire has no hosted-agent integration (written 17 Oct). | observed |
| NuGet now has `Aspire.Hosting.Foundry` (13.6.2-preview) and `Aspire.Hosting.AgentFramework.DevUI` (1.24.0-preview). The Foundry package's XML docs list `AddFoundry`, `AddProject`, `AddModelDeployment`, `AddPromptAgent`, `AsHostedAgent` (with `HostedAgentProtocol`), `WithAppInsights` and `RunAsFoundryLocal`. We read the method names only; what `AsHostedAgent` does when the AppHost runs locally is unknown. The bot also cannot call a local agent today, because it calls the Foundry endpoint and `HostedAgentEndpointPolicy` rewrites the path. A spike prompt is in `docs/presenter/prompts/ghcp-aspire-foundry-spike.md`. | documented (names) / open (behaviour) |
| Package versions in the build (21 Oct): `Azure.AI.Projects` 2.0.1, `Azure.AI.Projects.Agents` 2.0.0, `Azure.AI.Extensions.OpenAI` 2.0.0, `Azure.Identity` 1.21.0 (stable); `Azure.AI.AgentServer.Responses` 1.0.0-beta.6 (brings `AgentServer.Core` 1.0.0-beta.26) and, in the publish tool only, `Azure.AI.Projects.Agents` 3.0.0-beta.5 (prerelease); `Microsoft.Agents.AI.Workflows` 1.25.0 (brings `Microsoft.Agents.AI` 1.25.0 and `Microsoft.Extensions.AI` 10.10); `ModelContextProtocol` 2.2.0 and `Microsoft.Azure.Functions.Worker.Extensions.Mcp` 1.6.0. Newer prereleases exist: Foundry SDK 2.1.0-beta.4, `AgentServer.Responses` 1.0.0-beta.9, `Microsoft.Agents.Hosting.AspNetCore` 1.10.3-beta. `Microsoft.Agents.AI.*` is Agent Framework; `Microsoft.Agents.Hosting.*` and `.Authentication.Msal` (1.8.77) are the Microsoft 365 Agents SDK that hosts the bot. | observed |

