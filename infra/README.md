# Compass OBO - infrastructure (`infra/`)

Authored infrastructure for the Compass reference implementation (spec: `docs/Zava-Compass-OBO-DevSpec.md` §4, §9, §10, §11).
**Provisioned once, live**, into a sandbox tenant (one resource group, one Azure region) to prove the
Bicep and scripts actually work, not just lint clean. The resources and RBAC in [Approximations and honest
caveats](#approximations-and-honest-caveats) reflect that live run; the maintained step-by-step runbook, with what was and was not exercised, is
[`docs/DEPLOY.md`](../docs/DEPLOY.md); the older checklist is kept under [Live handoff](#live-handoff).
The hosted-agent publish path is documented in [Publishing the hosted agents](#publishing-the-hosted-agents).

## Layout

| Path | Purpose |
| --- | --- |
| `azure.yaml` | azd project `compass-obo`: services `bot` (App Service) and `mcp` (Function App), Bicep provider, pre/post-provision hooks |
| `main.bicep` | Resource-group composition root. Derives every name/tag once and passes them to modules |
| `main.parameters.json` | azd parameter file (`${AZURE_ENV_NAME}`, `${AZURE_LOCATION}`, `${BOT_APP_ID}`, `${ZAVA_API_APP_ID}`); no secrets |
| `modules/*.bicep` | One module per concern (below) |
| `scripts/*.ps1` | PowerShell 7 Entra / RBAC / test-data scripts (all `-WhatIf` capable, idempotent, secret-free) |

Names are `<resource-prefix>-<workloadName><environmentName>-<uniqueString(resourceGroup().id) first 6>` computed once in
`main.bicep` (`names` variable). Environments are limited to `dev` and `test`.

## Modules (dependency order)

| Module | Provisions |
| --- | --- |
| `appInsights.bicep` | Log Analytics workspace + workspace-based Application Insights (consumers read the connection string through an `existing` reference, so no secret output) |
| `keyVault.bicep` | Key Vault: RBAC authorization, soft delete, purge protection via `enablePurgeProtection`. No secrets are stored |
| `functionApp.bicep` | MCP host: Flex Consumption (FC1) Linux Function App, .NET 10 isolated, own user-assigned identity, identity-based storage (`allowSharedKeyAccess=false`), the storage data roles it needs, and the `ZavaApi__Auth__Authority / ValidAudiences__n / ValidIssuers__n` settings. Tagged `azd-service-name=mcp` |
| `appService.bicep` | Bot host: Linux plan + .NET 10 web app (HTTPS only). The **user-assigned managed identity** here is the principal trusted by the bot app's federated credential. Settings use placeholders/Key Vault URI only. Tagged `azd-service-name=bot` |
| `botService.bicep` | (Added beyond the spec list.) Azure Bot resource (single tenant, `msaAppId` = bot app id) + Microsoft Teams channel |
| `foundryProject.bicep` | `Microsoft.CognitiveServices/accounts` (kind `AIServices`, project management enabled, local auth disabled) + child `accounts/projects` + optional model deployment |
| `foundryAgents.bicep` | Per agent: `accounts/projects/applications` (the agent's addressable scope) and optionally `.../agentDeployments` (Hosted, protocol `Responses` 2.0.0). See approximations |
| `rbac.bicep` | Custom role **Compass Agent Impersonator (`<env>`)**, assignments to the bot identity per agent (or project), the built-in consumer role (also on the prompt agent), Key Vault Secrets User on the vault, and the knowledge-base roles (Foundry project identity -> Search Index Data Reader on the Search service; Search identity -> Cognitive Services User on the Foundry account) |
| `aiSearch.bicep` | Azure AI Search service (SystemAssigned identity, API-key auth disabled) that hosts the Foundry IQ knowledge base. The knowledge source, knowledge base and project connection are data-plane objects created by `scripts/Setup-KnowledgeBase.ps1` |

## Deployment order

1. `scripts/Setup-EntraApps.ps1 -Environment <env>` - creates the bot and Zava API app registrations (+ service principals). azd runs this in `preprovision` and sets `BOT_APP_ID`, `ZAVA_API_APP_ID`, `BOT_IDENTIFIER_URI`, `ZAVA_API_IDENTIFIER_URI`.
2. `azd provision` - Bicep (`appInsights -> keyVault -> functionApp -> appService -> botService -> botOAuthConnection -> foundryProject -> foundryAgents -> rbac`).
3. `Setup-EntraApps.ps1` again (azd `postprovision`) with `-ManagedIdentityPrincipalId/-ManagedIdentityClientId` from the Bicep outputs: adds the **federated identity credential** (the identity does not exist at step 1) and writes `.generated/teams-webApplicationInfo.json` for the Teams manifest.
4. `scripts/Grant-AdminConsent.ps1 -Environment <env>` (tenant admin; azd runs it only when `RUN_ADMIN_CONSENT=true`).
5. Optional fallback: `scripts/New-FoundryImpersonationRole.ps1` (if the Bicep per-agent scoping fails - see below).
6. Demo data: `Assert-DemoUsers.ps1` (read-only check), `Seed-SharePointTestData.ps1`, `Set-MemberMapping.ps1`.
7. `azd deploy` publishes `bot` and `mcp` (from `../src/...`, paths relative to `infra/`).

## azd usage

```powershell
cd infra
azd env new dev                 # the environment name must be 'dev' or 'test'
azd env set AZURE_LOCATION eastus2
azd provision                   # runs preprovision/postprovision hooks
azd deploy
```

azd substitutes only string values in `main.parameters.json`; boolean/array parameters keep their Bicep defaults. Override those with
`azd env set` plus a parameter-file entry, or pass `--parameters`/edit `main.parameters.json` at handoff.

Feature switches (all take effect on the bot App Service after `azd provision`):

| azd env value | App setting | Default | Effect |
| --- | --- | --- | --- |
| `SHAREPOINT_ROUTE` | `Bot__SharePoint__Route` | `KnowledgeBase` | `KnowledgeBase` (Foundry IQ, Search token), `Retrieval` (Copilot Retrieval API, Graph token) or `Both`. Unknown values are rejected by Bicep and by the bot at startup |
| `FRONT_DOOR_ENABLED` | `Bot__FrontDoor__Enabled` | `true` | Serves the web sign-in page and `POST /api/briefing` from the bot host. `false` removes both (404); Teams is unaffected |

### Web front door (M11c)

The page is a public client (MSAL.js, auth-code + PKCE, no secret) of the **same** `compass-bot-<env>` registration the Teams
SSO uses, so the bearer it sends is validated by the same `Bot:Auth` settings and exchanged by the same OBO broker. Two
things have to be true for sign-in to work: the page's origin is a registered SPA redirect URI (`Setup-EntraApps.ps1`
adds `SERVICE_BOT_ENDPOINT` automatically from the second run onward, or pass `-BotEndpoint`; `-LocalPort` adds a localhost URI for local runs and azd sets it for `dev` only), and the bot's own
`access_as_user` is consented tenant-wide (`Grant-AdminConsent.ps1`, or `RUN_ADMIN_CONSENT=true`). msal-browser 4.30.0 (LTS,
MIT) is vendored under `src/Bot/Compass.Bot.Host/wwwroot/lib/msal-browser/`; its provenance and hash are in the README there.

## Scripts (PowerShell 7, `-WhatIf` supported, no secrets, no `Write-Host`)

| Script | What it does |
| --- | --- |
| `Setup-EntraApps.ps1` | `compass-bot-<env>` (identifier URI `api://botid-{appId}`, scope `access_as_user`, pre-authorized Teams client ids, delegated Graph `Files.Read.All`/`Sites.Read.All` resolved from the Graph service principal, Zava API scope); a **single-page-application platform** with redirect URIs for the web front door: `<bot origin>/` with `-BotEndpoint`, and `http://localhost:<port>/` only when `-LocalPort` is passed (azd passes `-LocalPort 5240` for the `dev` environment only, so no other environment accepts a redirect to a developer's localhost; existing URIs are kept, so remove a stale one by hand; no secret); federated credential trusting the bot managed identity; `zava-credit-union-api-<env>` exposing `Member.Read`; service principals; emits an object / manifest fragment |
| `Grant-AdminConsent.ps1` | AllPrincipals delegated grants for Graph, the Zava API, Search, and the bot's own `access_as_user` (so the web front door never shows a consent prompt); updates existing grants instead of duplicating |
| `New-FoundryImpersonationRole.ps1` | Creates the custom role if missing and assigns it (plus the built-in consumer role) to the bot identity per agent. Same role name as `rbac.bicep`, so both are idempotent against each other |
| `Assert-DemoUsers.ps1` | **Read-only.** Verifies the two existing demo people (`daniel.park@<domain>`, `maya.lin@<domain>`) exist, are enabled and hold Teams and Microsoft 365 Copilot service plans; throws otherwise. Creates no user, group, password or Conditional Access policy |
| `Seed-SharePointTestData.ps1` | Libraries *Lending Policy* (Daniel only), *Portfolio and Collections* (Maya only) and *Member Services FAQ* (both) with unique permissions; uploads `infra/demo-data` Markdown as real `.docx` files converted in memory (PnP.PowerShell). The site must already exist. An earlier copy reported *Skipped (exists)* for files that were never uploaded (reproduced on a fresh site and fixed on 21 Oct 2026); still open the libraries in the browser afterwards. The `.docx` files can also be uploaded by hand. Install PnP.PowerShell for all users or outside OneDrive (its help files in a OneDrive module path trigger Purview DLP alerts) |
| `Set-MemberMapping.ps1` | Resolves both people's object ids from Graph and writes `Bot__Members__ByObjectId__<oid>` app settings (member numbers `ZCU-100234` / `ZCU-100871`) on the bot App Service; always `--output none` so settings never echo |
| `Compass.DemoDocuments.ps1` | Markdown-subset to `.docx` converter used by the seed script (the Retrieval API only does semantic retrieval for Office/PDF formats) |
| `Compass.Infra.Common.ps1` | Shared helpers dot-sourced by the scripts (naming, demo-user definitions, Graph connect, permission-id lookup) |
| `Setup-KnowledgeBase.ps1` | Idempotent REST PUTs (Az token, `-WhatIf` capable): remote SharePoint knowledge source, knowledge base, the Foundry project MCP connection used by the `SharePointKnowledgeAgent`, and an AI Search connection (`zava-foundry-iq-search`, Entra auth) that makes the knowledge base appear on the Foundry portal's Knowledge page. Data-plane preview API `2026-08-01-preview` |

Validate locally without any cloud access:

```powershell
az bicep build --file infra/main.bicep --outfile <scratch>/main.json   # repeat per module; never commit generated JSON
az bicep lint  --file infra/main.bicep
Invoke-ScriptAnalyzer -Path infra/scripts -Recurse -Severity Error,Warning
```

## Approximations and honest caveats

- **Calling hosted agents.** The bot calls each hosted agent on its own endpoint, `…/agents/{name}/endpoint/protocols/openai/responses?api-version=v1`. Foundry returns 400 for a hosted agent on the project-level `/openai/v1/responses` route. The agents host the Responses protocol; Invocations is for custom payloads (see [`docs/LIVE-FINDINGS.md`](../docs/LIVE-FINDINGS.md) §1).
- **Foundry hosted agents.** `accounts/projects/applications` and `.../agentDeployments` are real ARM types, but the
  *agent version* (container image, protocol 2.0.0 definition) is created through the Foundry **data plane**, not ARM.
  `foundryAgents.bicep` therefore creates the application scope per agent and only creates `agentDeployments` when
  `createAgentDeployments=true` and an `agentVersions` entry exists. **Confirmed live (first provision, westus3, 2026-10-17):** at
  API `2025-12-01` (valid for `accounts`/`accounts/projects` but not for the nested `applications`/`agentDeployments`
  types - the registered versions for those are `2025-10-01-preview` through `2026-09-15-preview`), provisioning
  failed with `NoRegisteredProviderFound`. Bumped to `2026-09-15-preview`, which resolved that error - but ARM then
  rejected the documented request body (`authorizationPolicy: { type: 'Default' }`, matching the published
  `azure-rest-api-specs` TypeSpec for this resource) with `Missing discriminator property [AuthorizationScheme]`.
  This looks like a live-service/published-spec mismatch on this preview surface as of 2026-10-17, not a mistake in
  this Bicep - re-check spec/service parity before retrying. **The documented fallback is what the sandbox
  deployment actually used, and it is now the default:** `createAgentApplications=false` (`CREATE_AGENT_APPLICATIONS=false`); RBAC
  is assigned at project scope and no `applications`/`agentDeployments` resources are created at all. Set
  `azd env set CREATE_AGENT_APPLICATIONS true` to retry per-agent scope.
- **Per-agent RBAC.** Expressed by assigning roles with `scope:` set to each agent's `applications` resource. **Not
  exercised live** because of the issue above - the sandbox run used the project-scope fallback
  (`FOUNDRY_RBAC_SCOPE=project`). Revisit per-agent scoping once the `applications` schema issue is resolved; see
  the demo script's "in production you would..." note on why per-agent scoping matters for blast-radius containment.
- **Impersonation permission is a *data action*.** `Microsoft.CognitiveServices/accounts/AIServices/agents/endpoints/UserIdentityImpersonation/action`
  is placed under `dataActions` of the custom role (not `actions`). **Confirmed live:** both the custom
  `Compass Agent Impersonator (dev)` role and the built-in `Foundry User` role assignment succeeded and are visible
  via `az role assignment list` against the project scope.
- **"Foundry Agent Consumer" is not on the built-in roles page.** Learn's hosted-agent permissions page names it as the least-privilege consumer role, so treat it as **not verified**. The role used here is *Foundry User* (formerly Azure AI User,
  `53ca6127-db72-4b80-b1b0-d745d6d5456d`), exposed as the parameter `agentConsumerRoleDefinitionId`. Its data actions on the built-in roles page are `Microsoft.CognitiveServices/*`, but Learn's hosted-agent page says impersonation is no longer included, which is why the custom impersonation role exists (see [`docs/IDENTITY-AND-ACCESS.md`](../docs/IDENTITY-AND-ACCESS.md)). It is deliberately assigned at the **same per-agent scope** - assigning it project-wide would defeat the per-agent isolation.
- **Role name suffix.** Custom role names are unique per tenant, hence `Compass Agent Impersonator (<env>)`.
- **`botService.bicep`** was added (not in the module list). The **Teams SSO OAuth connection** lives in `botOAuthConnection.bicep`
  (`deployBotOAuthConnection`, default true). Its `serviceProviderId` for "AAD v2 with Federated Credentials" comes from a
  Microsoft sample and is **not independently verified**; if the deployment rejects it, create the connection once in the portal and
  set `deployBotOAuthConnection=false`.
- **Agents SDK settings.** `Connections__ServiceConnection__Settings__*` (`AuthType=FederatedCredentials`, `ClientId`, `FederatedClientId`,
  `TenantId`, `Scopes`) and `AgentApplication__UserAuthorization__*` were checked against the Microsoft Learn "Configure authentication
  in your agent" pages. **Do not ship a placeholder for `AuthorityEndpoint`:** the SDK prefers it over `TenantId`, and a literal
  `<tenant-id>` there made every bot reply fail with an MSAL "authority must be well-formed" error (observed live).
  `appsettings.json` now ships `TenantId` as the placeholder and the readiness check reports a placeholder `AuthorityEndpoint`.
  `main.bicep` previously emitted `Compass__ZavaApi__*` keys that nothing read; it now emits the `Bot__Obo__*`,
  `Bot__Auth__*` and `Bot__Channel__*` keys the bot actually binds.
- **Flex Consumption + .NET 10** availability/region support, the `gpt-5.6-sol` default model and its quota, and the non-Graph role GUIDs (Key Vault Secrets User,
  Storage Blob/Queue/Table roles) are not live-verified.

## Policy-managed tenants: the sandbox opt-out

Some tenants carry a management-group policy initiative whose **Modify**
effects force settings such as `Microsoft.Storage/storageAccounts/publicNetworkAccess = Disabled`. With that applied, the
Flex Consumption Function App cannot receive its deployment package: `azd deploy mcp` fails with
`BlobUploadFailedException ... 403 (This request is not authorized to perform this operation)` because the deployment
service reaches the storage account over its public endpoint.

The policy honours an exclusion tag (read from the policy rule, not assumed): it is skipped when the resource **or its
resource group** carries `SecurityControl = Ignore` (defaults of the `AllowedTagName` / `AllowedTagValue` parameters;
the assignment does not override them). Because a Modify policy only acts at write time, tagging does not undo what it
already did, so after tagging the storage account must be written once (`az storage account update --public-network-access Enabled`).

`main.bicep` exposes this as `sandboxPolicyOptOut` (azd: `azd env set SANDBOX_POLICY_OPT_OUT true`), default **false**.
It adds the tag to every resource this template creates. Shared-key access stays **disabled**; the Function still authenticates
to storage with its managed identity. **Use this only in disposable demo tenants.** In a real environment keep
the policy, use a private endpoint for the storage account and VNet-integrate the Function App (see the production notes
in `docs/DEMO-SCRIPT.md`, Part 5).

## Publishing the hosted agents

The two hosted agents (`SharePointRetrievalAgent`, `ZavaApiAgent`) are deployed **from source - no container image, no ACR**
(Foundry code deploy: <https://learn.microsoft.com/azure/foundry/agents/how-to/deploy-hosted-agent-code>).
`tools/Compass.Tools.PublishAgents` automates it: `dotnet publish -r linux-x64 --self-contained false` per agent, the SDK zips
the output, a new agent version is created (runtime `dotnet_10`, entry point `dotnet <Assembly>.dll`, bundled dependencies,
Responses protocol `2.0.0`, 0.5 CPU / 1Gi), and the tool polls until the version is `active`.

```powershell
# Review the plan first - offline, no credential, no build:
dotnet run --project tools/Compass.Tools.PublishAgents -- --what-if

# Real run (needs `az login`/`azd auth login` as a Foundry Project Manager on the project):
cd infra
dotnet run --project ../tools/Compass.Tools.PublishAgents -- `
    --project-endpoint $(azd env get-value FOUNDRY_PROJECT_ENDPOINT) `
    --mcp-endpoint $(azd env get-value SERVICE_MCP_ENDPOINT)
```

Notes:

- `ZavaApi__McpEndpoint` is baked into the ZavaApiAgent version (Foundry environment is **immutable per version**), so publish
  after the Function App exists and re-publish if its host name changes. Foundry also auto-injects `FOUNDRY_PROJECT_ENDPOINT`
  and `APPLICATIONINSIGHTS_CONNECTION_STRING` into the container. The identical zip posted twice returns the existing version.
- `--agent <name>` publishes one agent only. The tool needs the **Foundry Project Manager** role on the project.
- The tool is deliberately standalone (it does not reference the bot projects): code deploy exists only in the prerelease
  `Azure.AI.Projects.Agents` 3.x line, which cannot share a dependency graph with the stable 2.x packages the bot uses.

**Running a hosted agent locally.** A hosted agent is an ordinary ASP.NET app, so no cloud is needed to start one:

```powershell
dotnet run --project src/Agents/Compass.Agents.Hosted.ZavaApi        # listens on http://localhost:8088
./tools/Call-LocalAgent.ps1                            # typed request, no token: expect status failed, code missing_token
```

`/readiness` returns `Healthy` and `/responses` behaves as it does in Foundry (observed 21 Oct). The launch profile (`Properties/launchSettings.json`, also used by Visual Studio F5) runs in the **Production** environment on purpose: in Development, ASP.NET Core validates scopes and the Foundry adapter resolves its scoped handler from the root provider at startup, so the host crashes with `Cannot resolve scoped service 'ResponseHandler' from root provider` (found 21 Oct; the profile said Development from 17 Oct until the fix). The person id comes from the `x-agent-user-id` header when there is no platform; pass `-ZavaApiToken` and run the Functions host to go further. The AppHost does not start the agents, and the bot cannot call a local agent (it calls the Foundry project endpoint). Visual Studio F5 with a breakpoint and the Foundry Toolkit Agent Inspector have not been verified with these .NET agents. See [`docs/LIVE-FINDINGS.md`](../docs/LIVE-FINDINGS.md) section 12.

**How the bot learns the versions.** The tool writes `infra/.generated/agent-versions.json` (git-ignored; `{ "<AgentName>": "<version>" }`)
and prints two equivalent ways to apply it. Both end up in the bot's `Bot__Foundry__<Agent>__Version` app settings:

1. Durable (survives re-provisioning) - `main.parameters.json` maps azd variables to the Bicep parameters
   `sharePointRetrievalAgentVersion` / `zavaApiAgentVersion`:
   `azd env set SHAREPOINT_RETRIEVAL_AGENT_VERSION <n>`, `azd env set ZAVA_API_AGENT_VERSION <n>`, then `azd provision`.
2. Immediate (no provision) - `az webapp config appsettings set -g <rg> -n <bot-app> --settings Bot__Foundry__ZavaApiAgent__Version=<n> ...`.
   Use this for a quick iteration, then do (1) before the next provision or the settings are blanked.

The bot reads versions at startup; restart the web app after changing them.

## Live handoff

> This is the original checklist. [`docs/DEPLOY.md`](../docs/DEPLOY.md) supersedes it: it is current, and it records which
> steps were exercised end to end. Where the two differ, follow `DEPLOY.md`.

Provisioning was run against a sandbox tenant - see [Approximations and honest caveats](#approximations-and-honest-caveats)
for what that run found and fixed.

1. **Prerequisites:** Azure subscription (Owner/User Access Administrator on the resource group), Entra roles (Application Administrator to run `Setup-EntraApps`;
   Global/Privileged Role Administrator for `Grant-AdminConsent`), modules `Microsoft.Graph.*`, `Az.Accounts`, `Az.Resources`, `PnP.PowerShell`, and `azd` + `pwsh` 7.
2. **✅ done.** `azd auth login`, `azd env new dev`, `azd env set AZURE_RESOURCE_GROUP <resource-group>`, set location
   (`<region>`), **`azd provision`**. Review the two-pass `Setup-EntraApps` output and confirm the federated credential exists on the bot app (issuer `https://login.microsoftonline.com/<tenant>/v2.0`, subject = bot identity principal id, audience `api://AzureADTokenExchange`) - confirmed live (`FederatedCredentialName: compass-bot-mi-dev`).
3. Run **`Grant-AdminConsent.ps1`** (or set `RUN_ADMIN_CONSENT=true`) as a tenant admin.
4. **Foundry (data plane):** confirm model deployment and quota (✅ `gpt-5.6-sol` deployed live), then **publish the hosted agents from source** - see [Publishing the hosted agents](#publishing-the-hosted-agents). Verify the role-assignment scopes and the Foundry User role id; resolve the `applications` API schema issue above before attempting per-agent scoping, or use `New-FoundryImpersonationRole.ps1` as a scripted alternative to the Bicep path.
5. **Azure Bot / Teams:** the OAuth connection is provisioned by `azd provision` (step 2). Build the Teams package with
   `teams/Build-TeamsPackage.ps1` (reads `.generated/teams-webApplicationInfo.json`) and upload `teams/dist/Compass-Teams-App.zip` in the Teams admin center (**Manage apps → Upload new app**), which publishes it to the org catalog; a user's own upload can become an admin submission instead. Then add the app for each person. The manifest declares the `personal`, `groupChat` and `copilot` scopes, so Compass can also be added to a group chat (people must @mention it, and it answers with one reply rather than a stream). To change the package, bump `version` in `teams/manifest.json`, rebuild it, and upload it in the admin center as a new version of the existing app (Manage apps, the app, Upload file); the manifest `id` must not change. If a person's chat later says the bot cannot be messaged, or the bot logs `BotDisabledByAdmin`, check the app's status and availability in the admin center. See [`docs/LIVE-FINDINGS.md`](../docs/LIVE-FINDINGS.md) §3.
   Poll `GET https://<bot-host>/health/ready` until it returns 200; a 503 lists the **names** of missing settings (never values).
6. **MCP Function App:** webhook auth is `Anonymous` (`host.json` and the `AzureFunctionsJobHost__extensions__mcp__system__webhookAuthorizationLevel`
   app setting) so no `mcp_extension` system key is needed. This is deliberate: the host's JWT audience/issuer middleware is the gate and
   the OBO token is the only credential. Caveat: azure-functions-mcp-extension#138 reports the setting is ignored when App Service
   Authentication (Easy Auth) is enabled on host versions before ~4.1045; this build does not use Easy Auth. For production, put the
   Function behind APIM or a private endpoint, or keep the system key (Key Vault) in addition to the JWT check.
7. **Demo data:** `Assert-DemoUsers.ps1` (read-only), create the SharePoint site, `Seed-SharePointTestData.ps1`, then `Set-MemberMapping.ps1`. See [`demo-data/README.md`](demo-data/README.md). The demo people already exist; no script creates users, groups or passwords. Allow about 30 minutes after the files appear for the index to answer from them.
8. `azd deploy`, then smoke-test via the Teams client; set `enablePurgeProtection=true` for any shared environment.

### Knowledge-base route (M9a: `SharePointKnowledgeAgent`)

The second route to the same SharePoint content: a Foundry prompt agent calls a Foundry IQ knowledge base on Azure AI Search, which queries SharePoint live with the **person's** Search-scope OBO token. In the sandbox (`dev`, `srch-compass-dev-<suffix>`), steps 1 and 3 are applied: the knowledge base `zava-kb` (`answerSynthesis`, `gpt-5.6-sol`) and the `remoteSharePoint` knowledge source `zava-sharepoint` (no site filter) exist on the Search service, and the Foundry project connection `compass-knowledge-base` points at the KB's MCP endpoint. **Observed live on 18 Oct:** with the demo documents uploaded and indexed (about 30 minutes), it answers as the person (Daniel from *Lending Policy*, Maya from *Portfolio and Collections*, both from the shared FAQ) with real, linked sources; both people hold Copilot licences (see the licensing step and [`demo-data/README.md`](demo-data/README.md)).

1. ✅ `azd provision` created the Search service (`deployKnowledgeBase=true` by default) live. The prompt-agent application scope and its RBAC are blocked by the same `applications` schema issue above, so they used the project-scope fallback along with the hosted agents. Confirm `Microsoft.Search/searchServices@2025-05-01` is available in the region (confirmed: northcentralus) and that the preview `2026-08-01-preview` data plane is enabled for the service.
2. `Grant-AdminConsent.ps1` now also consents the bot app to Azure AI Search `user_impersonation` (needed for the `https://search.azure.com/.default` OBO exchange; `Bot:Obo:SearchScopes`). `Setup-EntraApps.ps1` adds the matching requested permission.
3. Run `scripts/Setup-KnowledgeBase.ps1 -SearchServiceName <AZURE_SEARCH_NAME> -FoundryProjectResourceId <project id> -FoundryAccountName <account> -ModelDeployment <deployment>` (add `-WhatIf` first). Names must match `KNOWLEDGE_BASE_NAME` / `KNOWLEDGE_BASE_CONNECTION_NAME`.
4. Publish the agent version: `dotnet run --project src/Bot/Compass.Bot.Host -- --publish-knowledge-agent` (reads `Bot:Foundry:*`; prints the agent name and version).
5. **Foundry portal Knowledge page (optional, cosmetic):** the page lists knowledge bases only for an AI Search resource connected to the project. `Setup-KnowledgeBase.ps1` creates that connection (`zava-foundry-iq-search`, Entra auth) in addition to the MCP connection the agent uses. Only people who open the page need Search Index Data Reader and Search Service Contributor on the service. **End users need no Search role at all**: the agent reaches the knowledge base as the project managed identity, and the person's own token only trims the SharePoint results.
6. **Licensing:** the querying person needs a Microsoft 365 Copilot licence, or the tenant must enable pay-as-you-go for the Retrieval API; remote SharePoint is limited to 200 requests per user per hour.
7. Live isolation test: set `COMPASS_TEST_FOUNDRY_PROJECT_ENDPOINT` and `COMPASS_TEST_SEARCH_SCOPES` alongside the existing `COMPASS_TEST_*` variables (see `tests/Compass.IdentityIsolation.Tests`).
