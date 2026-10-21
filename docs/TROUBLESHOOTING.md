# Troubleshooting

Every row here is a failure that happened while building and running this reference, with the evidence for the cause where we have it. Where the cause is a diagnosis rather than something we reproduced, the row says so. Tags follow the rest of the docs: **observed**, **documented**, **open** (see [`DECISIONS-AND-LESSONS.md`](DECISIONS-AND-LESSONS.md)).

If something fails that is not here, check [`LIVE-FINDINGS.md`](LIVE-FINDINGS.md) first (it is the record of what broke), then the bot's readiness endpoint: `GET <bot>/health/ready` reports configuration problems such as a placeholder left in an authority setting.

## Provisioning and deployment

| Symptom | Cause | Fix | Evidence |
| --- | --- | --- | --- |
| `azd provision` fails creating `accounts/projects/applications` with `Missing discriminator property [AuthorizationScheme]` | The current preview API (`2026-09-15-preview`) rejected the documented request shape for per-agent Foundry application resources | `CREATE_AGENT_APPLICATIONS` now defaults to `false`: RBAC is assigned at project scope. Set it to `true` only to retry per-agent scope | [`infra/modules/foundryAgents.bicep`](../infra/modules/foundryAgents.bicep) header, [`infra/README.md`](../infra/README.md) |
| `azd deploy mcp` fails with `BlobUploadFailedException ... 403` | A governance policy forces storage `publicNetworkAccess = Disabled`; the Flex Consumption deployment reaches storage over its public endpoint | Disposable tenants only: `azd env set SANDBOX_POLICY_OPT_OUT true`, then write the storage account once. Real environments: private endpoints and VNet integration | [`infra/README.md`](../infra/README.md) "Policy-managed tenants", [DECISIONS B4](DECISIONS-AND-LESSONS.md#b4-mcp-server-on-azure-functions) |
| The bot is slow to answer the first message, or a Teams message times out | A Free App Service plan cold-starts in about 55 seconds, longer than Bot Service waits. In one tenant a governance automation resized the plan to Free overnight | Keep the plan at B1 with Always On and assert it in your readiness checks. Whether the `CostControl` tag stops the automation is **open** | [DECISIONS B5](DECISIONS-AND-LESSONS.md#b5-the-bot-host-app-service) |
| The Function App logs `AuthorizationPermissionMismatch` for `AzureWebJobsStorage` | Not determined. Requests are not affected. The MCP extension's SSE transport uses host storage queues, and Learn lists the queue roles it needs | Cause **not verified**: check the host storage roles against Learn | [LIVE-FINDINGS §9](LIVE-FINDINGS.md#9-smaller-items) |
| `azd provision` fails in the `preprovision` hook with `InteractiveBrowserCredential authentication failed: A window handle must be configured` | `Connect-MgGraph` signs in through the Windows account manager, which needs a window. A shell started by an agent, CI or a remote session has none. **Observed** | Supply a token from the signed-in Azure CLI account: `$env:COMPASS_GRAPH_ACCESS_TOKEN = (az account get-access-token --resource-type ms-graph --query accessToken -o tsv)`, then run `azd provision` again. `Compass.Infra.Common.ps1` uses it when set. Re-issue it if it expires or after you activate an admin role | `Connect-CompassGraph` in `infra/scripts/Compass.Infra.Common.ps1` |
| `azd provision` seems to have stopped part-way but the deployment shows `Succeeded` in the portal | The azd process was interrupted while Azure kept deploying, so azd never recorded the outputs or ran the `postprovision` hook. **Observed** | Run `azd provision` again. It is idempotent: it reports no changes, stores the outputs and runs the hook | [DEPLOY.md](DEPLOY.md) step 2 |
| `dotnet` warns `NU1603` that `Azure.AI.Extensions.OpenAI 3.0.0-alpha.20261008.2` was not found | `Azure.AI.Projects.Agents` 3.0.0-beta.5 asks for an alpha build that is no longer on NuGet, so NuGet picks a different version. **Observed** as a warning only; publishing worked | None needed today. Pin the package when a stable release exists | `tools/Compass.Tools.PublishAgents` |
| `az ... --query "[?...]"` fails with `was unexpected at this time` | On Windows `az` is a `.cmd` shim, and `cmd` mangles brackets, pipes and parentheses inside `--query`. **Observed** | Fetch with `-o json`, pipe to `ConvertFrom-Json` and filter in PowerShell, or use a query with no brackets | n/a |
| A script says it is not authorised to grant admin consent | Admin consent needs a Global Administrator or Privileged Role Administrator | Ask a tenant admin to run `infra/scripts/Grant-AdminConsent.ps1`, or set `RUN_ADMIN_CONSENT=true` as one | `Grant-AdminConsent.ps1`, [`IDENTITY-AND-ACCESS.md`](IDENTITY-AND-ACCESS.md) |

## Identity and sign-in

| Symptom | Cause | Fix | Evidence |
| --- | --- | --- | --- |
| Every bot reply fails and logs "The authority ... must be in a well-formed URI format" | A literal `<tenant-id>` placeholder left in `Connections:ServiceConnection:Settings:AuthorityEndpoint`. The Agents SDK prefers it over `TenantId` | Leave `AuthorityEndpoint` unset and set `TenantId` (the shipped file already does). `GET /health/ready` now reports a placeholder | [LIVE-FINDINGS §2](LIVE-FINDINGS.md#2-a-placeholder-in-appsettingsjson-broke-every-bot-reply-m11h) |
| The web page signs in but the redirect fails, or a consent prompt appears | The page's origin is not a registered SPA redirect URI, or the bot app's own `access_as_user` is not consented tenant-wide | Run `Setup-EntraApps.ps1 -BotEndpoint <bot url>` (azd does this from the second provision), and `Grant-AdminConsent.ps1` | [`infra/README.md`](../infra/README.md) "Web front door" |
| The MCP host cannot validate tokens when run locally | Its options validation requires an authority, at least one issuer and one audience before it can validate a request | Copy `src/Mcp/Compass.Mcp.ZavaApi.Host/local.settings.sample.json` to `local.settings.json` and set the `ZavaApi__Auth__*` values | [`LOCAL-DEVELOPMENT.md`](LOCAL-DEVELOPMENT.md) |

## Agents and Foundry

| Symptom | Cause | Fix | Evidence |
| --- | --- | --- | --- |
| Calling a hosted agent returns HTTP 400 "Hosted agents can only be called through the agent endpoint" | The project-level `/openai/v1/responses` route is for prompt agents. A hosted agent has its own endpoint | The bot calls `/agents/{name}/endpoint/protocols/openai/responses?api-version=v1`; `HostedAgentEndpointPolicy` rewrites the SDK's URI | [LIVE-FINDINGS §1](LIVE-FINDINGS.md#1-hosted-agents-are-called-on-their-own-endpoint-m11f) |
| A hosted agent returns `status: failed`, `code: missing_token` | No `x-client-*` token header reached it. This is the designed fail-closed behaviour, and the expected result of `tools/Call-LocalAgent.ps1` | Send the token header the agent names in the message | [LIVE-FINDINGS §12](LIVE-FINDINGS.md#12-where-the-agents-live-how-they-ship-how-to-debug-them-locally-21-oct) |
| `dotnet run` on a hosted agent crashes with `Cannot resolve scoped service 'ResponseHandler' from root provider` | In the Development environment ASP.NET Core validates scopes, and the Foundry adapter resolves its scoped handler from the root. The launch profile used to say Development | The launch profiles now run in Production, as Foundry does. Pull the fix, or set `ASPNETCORE_ENVIRONMENT=Production` | [LIVE-FINDINGS §12](LIVE-FINDINGS.md#12-where-the-agents-live-how-they-ship-how-to-debug-them-locally-21-oct) |
| Locally, the agent logs an empty person id | `x-ms-user-identity`, which the bot sends to Foundry, does not reach a handler through the adapter. With no platform, the id comes from `x-agent-user-id` | Send `x-agent-user-id` locally | [LIVE-FINDINGS §12](LIVE-FINDINGS.md#12-where-the-agents-live-how-they-ship-how-to-debug-them-locally-21-oct) |
| Publishing or creating an agent returns 403 `does not have permissions for ...agents/write`, naming an object id that is not the account you signed in with | `DefaultAzureCredential` picked a different account than the one with the role. Reproduced by running the publish tool from a clean clone: the id named was not in the tenant directory and not the `az` account | Set `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` and sign in with `az login` as the intended account (**observed**: publishing then succeeded with the same account) | [`src/Agents/Compass.Agents.Prompt.LearnDocs/README.md`](../src/Agents/Compass.Agents.Prompt.LearnDocs/README.md), [DEPLOY.md](DEPLOY.md) step 4 |
| The Foundry Knowledge page is empty or shows no `zava-kb` | The portal needs a `CognitiveSearch` project connection, and the signed-in person needs Search roles | `Setup-KnowledgeBase.ps1` creates the connection; grant Search Index Data Reader and Search Service Contributor to the people who open that page. Do not press Save (retrieval reasoning effort shows as unselected). End users need no Search role | [LIVE-FINDINGS §7](LIVE-FINDINGS.md#7-the-foundry-portals-knowledge-page) |

## Teams

| Symptom | Cause | Fix | Evidence |
| --- | --- | --- | --- |
| A person's Compass chat says "You cannot send messages to this bot" | Cause **open**. Admin policies and the app listing were identical to a working user's. It cleared after the package was re-uploaded as a new version | Check the app's status and availability in the Teams admin center; re-upload the package as a new version; allow time for the client to refresh | [LIVE-FINDINGS §3](LIVE-FINDINGS.md#3-teams-delivery-what-was-tried-and-what-is-still-open) |
| The bot's reply is refused with `403 BotDisabledByAdmin` | Seen once, then not again; why is **open** | Same checks as above | [LIVE-FINDINGS §3](LIVE-FINDINGS.md#3-teams-delivery-what-was-tried-and-what-is-still-open) |
| An uploaded Teams app never becomes available | A user's own custom-app upload can become a submission that an admin must publish. Uploading as an admin publishes directly | A tenant admin uses Manage apps, Upload new app | [LIVE-FINDINGS §3](LIVE-FINDINGS.md#3-teams-delivery-what-was-tried-and-what-is-still-open) |
| The app is not offered in Microsoft 365 Copilot, or does not behave as an agent | A hand-built manifest is not treated as an agent unless it declares `copilotAgents.customEngineAgents` and the `copilot` scope | Use `teams/manifest.json` as shipped (it declares both, and `groupChat`) | [DECISIONS B7](DECISIONS-AND-LESSONS.md) |
| In a group chat or Copilot there is no streaming status | Streaming is a one-to-one Teams feature. Group chats and Copilot get one status line and one reply | Expected. Note that a group-chat reply is visible to everyone in the chat | [LIVE-FINDINGS §3](LIVE-FINDINGS.md#3-teams-delivery-what-was-tried-and-what-is-still-open) |

## Data and answers

| Symptom | Cause | Fix | Evidence |
| --- | --- | --- | --- |
| The seed script prints "Skipped (exists)" but a library is empty | The existence check treated "no error" as "exists", and `Get-PnPFile -AsListItem` returns `$null` instead of throwing for a missing file (PnP.PowerShell 3.4.1). **Observed** on a fresh site: all five files were skipped and every library was empty | Fixed in `Seed-SharePointTestData.ps1`, which now tests the result for `$null`; after the fix the same run uploaded all five files. If you see it on an older copy, update the script or upload the `.docx` files by hand. Still open each library in the browser as the person it belongs to | [LIVE-FINDINGS §4](LIVE-FINDINGS.md#4-sharepoint-demo-data) |
| The knowledge base answers "I don't know" for a person who should have access | The documents are not uploaded or indexed yet (allow about 30 minutes), or the person lacks a Microsoft 365 Copilot licence | Check the library as that person, wait for indexing, check the licence with `Assert-DemoUsers.ps1` | [LIVE-FINDINGS §4 and §5](LIVE-FINDINGS.md#5-knowledge-base-answers-citations-and-what-is-shown) |
| `PnP.PowerShell` triggers data-loss-prevention alerts | Installing it with `-Scope CurrentUser` put its help files in a OneDrive-redirected module folder | Install it for all users or outside any synced folder | [DECISIONS](DECISIONS-AND-LESSONS.md) lessons table |
| The first turn after idle takes about 35 seconds | A cold start across several services (a hosted-agent readiness probe appears just before its response). Not isolated | Warm the system with one question before a demo | [LIVE-FINDINGS §11](LIVE-FINDINGS.md#11-traceability-and-the-minimal-mcp-agent-19-oct) |

## Observability

| Symptom | Cause | Fix | Evidence |
| --- | --- | --- | --- |
| The Foundry Traces tab says "Create or connect an App Insights resource" and lists nothing | Foundry tracing is off until an Application Insights resource is connected to the project, and hosted agents export nothing until then | Connect it (Agents, Traces, Connect, or `foundryProject.bicep` with `appInsightsName`). The project's managed identity needs Monitoring Metrics Publisher on the resource | [LIVE-FINDINGS §11](LIVE-FINDINGS.md#11-traceability-and-the-minimal-mcp-agent-19-oct) |
| No workflow spans from the bot | Agent Framework workflow telemetry is opt-in: the builder must call `WithOpenTelemetry()`. Registering the ActivitySource source alone does nothing | Already fixed in `BriefingWorkflowFactory.cs`; a test checks it | [LIVE-FINDINGS §11](LIVE-FINDINGS.md#11-traceability-and-the-minimal-mcp-agent-19-oct) |

## Running locally

| Symptom | Cause | Fix | Evidence |
| --- | --- | --- | --- |
| `dotnet run --project apphost/Compass.AppHost` waits at "Trusting certificates..." | The Aspire dev-certificate trust prompt | Accept it, run `dotnet dev-certs https --trust` once, or use the unsecured-transport settings in the local development guide | [`LOCAL-DEVELOPMENT.md`](LOCAL-DEVELOPMENT.md) |
| A full end-to-end turn is not possible locally | Sign-in, OBO and retrieval need a real tenant | Use `dotnet test tests/Compass.IdentityIsolation.Tests`, which exercises the whole flow against fakes | [`LOCAL-DEVELOPMENT.md`](LOCAL-DEVELOPMENT.md) |
