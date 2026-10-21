# Service catalog: what each piece is, why it's there, and what it needs

For every service in Compass: what it does, why we need it, what has to be configured, which identity it runs as, which
roles and permissions it holds (and which it needs from others), what must exist before it, and what bit us. Facts come from the
repository (file paths given) and from the live sandbox tenant on 18 October 2026.

**Marks:** **[repo]** from code or infra in the repo · **[live ✓]** also confirmed in the sandbox tenant on 18 Oct 2026 ·
**[documented]** from Microsoft Learn · **[open]** unproven or not in the repo. For the *why* behind the choices see
[`DECISIONS-AND-LESSONS.md`](DECISIONS-AND-LESSONS.md).

## The three rules this catalog proves

1. **No end user holds any Azure role, anywhere.** People need a Microsoft 365 Copilot licence, SharePoint access, and Teams.
   Every Azure role below belongs to a service identity. (The Heenanl pattern was rejected because it breaks this.)
2. **No secret exists.** Every credential is a managed identity or a federated credential. Key Vault is deployed but holds
   nothing.
3. **Every permission has one owner and one reason.** If a row below has no reason, delete the permission.

## Build order (what must exist before what)

| # | Step | Needs first | Run by | Needs these rights |
| --- | --- | --- | --- | --- |
| 1 | Entra apps (bot, Zava API) and Teams SSO federated credential | a tenant | `Setup-EntraApps.ps1` (azd preprovision) | create app registrations; sign in via `COMPASS_GRAPH_ACCESS_TOKEN` when no window handle |
| 2 | Resources: Log Analytics, App Insights, Key Vault, MCP storage and Function App, Search, bot App Service, Azure Bot + Teams channel + OAuth connection, Foundry account/project/model | step 1 (app ids) | `azd provision` (Bicep) | Owner, or Contributor plus User Access Administrator, on the resource group (the RBAC Administrator role cannot create the custom role) |
| 3 | Second Entra pass: federated credential trusting the bot's managed identity; Teams manifest fragment | step 2 (the identity exists) | `Setup-EntraApps.ps1` (azd postprovision) | as step 1 |
| 4 | Admin consent for Graph, Search, Zava API scopes | steps 1–3 | `Grant-AdminConsent.ps1` | Global Administrator or equivalent, `DelegatedPermissionGrant.ReadWrite.All` |
| 5 | RBAC: bot identity to Foundry (consumer plus impersonation), Search and Foundry identities to each other | step 2 | Bicep `rbac.bicep` | same as step 2 |
| 6 | SharePoint site, three libraries with unique permissions, demo documents | licensed demo users | `Seed-SharePointTestData.ps1` (a site owner plus an Entra app allowed for PnP sign-in) | site owner |
| 7 | Knowledge source, knowledge base, Foundry project connection | steps 2 and 6 | `Setup-KnowledgeBase.ps1` | Search Service Contributor plus project write |
| 8 | Hosted agent versions (retrieval, member) | steps 2 and 5 | `Compass.Tools.PublishAgents` | agent **write** on the project; check *which* identity the SDK picks |
| 9 | Prompt agent version | steps 7 and 8 | `Compass.Bot.Host --publish-knowledge-agent` | as step 8 |
| 10 | Member mapping (object id to member number) on the bot | step 2 | `Set-MemberMapping.ps1` | write app settings on the web app |
| 11 | Deploy code: bot and MCP server | all above | `azd deploy` | deploy rights |
| 12 | Teams package and registration | steps 1–3 | `Build-TeamsPackage.ps1`, then Teams admin center | Teams or Global administrator |

---

## 1. Azure Bot Service
- **What and why:** the channel adapter between Teams and our code. It validates Teams traffic, forwards activities to
  `/api/messages`, and brokers the person's sign-in token (it does not issue the downstream ones). **[repo]** `infra/modules/botService.bicep`
- **Resource:** `Microsoft.BotService/botServices`, kind `azurebot`, SKU F0, `SingleTenant`, location `global`; a Teams channel
  (`MsTeamsChannel`); a separate OAuth connection `teams-sso`. **[live ✓]**
- **Must be configured:** `endpoint` = the web app's `https://…/api/messages`; `msaAppId` = the bot app registration;
  `msaAppTenantId`; the OAuth connection's provider **AAD v2 with Federated Credentials**, `clientId` = bot app, scope
  `api://botid-{appId}/access_as_user`. **[live ✓]** `disableLocalAuth` is true in our Bicep; the credit memo's bot has it false
  (toggling it made no difference to Teams delivery, 18 Oct).
- **Identity:** the bot Entra app, trusted by a federated credential on that app (no secret).
- **Permissions:** it holds none in Azure. Others need none on it.
- **Depends on:** the bot app registration and its identifier URI; the web app (for the endpoint).
- **Gotchas:** one bot per app id (a second fails with "MsaAppId is already in use"); the OAuth connection is separate from the
  bot resource; Teams can show "delivered" while nothing reaches the endpoint (see Teams, below).
- **Status:** created and reachable. **[live ✓, 18 Oct]** A Web Chat message reached the app (three requests, 202); Maya's Teams one-to-one chat worked end to end later that day. Daniel's Teams chat first could not send and later worked (cause **[open]**), and why Teams messages did not arrive earlier is **[open]** (see decisions B7). The Teams channel's `acceptedTerms` is false on the working credit memo bot too.

## 2. Microsoft Teams app (manifest and catalog entry)
- **What and why:** the package that makes the bot installable. It is *not* the bot; it points at the bot and at the app
  registration used for single sign-on. **[repo]** `teams/manifest.json`, `teams/Build-TeamsPackage.ps1`
- **Must be configured:** `bots[0].botId` = the bot app id; `webApplicationInfo.id` = the bot app id and
  `webApplicationInfo.resource` = `api://botid-{appId}` (the build fails if not); `validDomains` includes `token.botframework.com`;
  `permissions` includes `identity`; `developer.name` **32 characters or fewer** (the admin center rejects longer); icons 192×192
  colour and 32×32 outline.
- **Manifest version:** custom engine agents need **1.21 or later** and a `copilotAgents.customEngineAgents` entry plus the
  `copilot` bot scope. **[documented]** Our manifest lacked both until PR #8; Foundry's own exported package has both. **[live ✓]**
  With them, Compass appeared in the Agent 365 registry and Teams listed it as an "Agent".
- **Registration:** upload in the Teams admin center (Manage apps) or the Microsoft 365 admin center (Agents, Add agent). The
  catalog assigns its **own** app id (different from the manifest id); deep links must use the catalog id.
- **Who may upload:** users get only "Submit an app to your org"; an administrator uploads. The "Add agent" button needs an
  active Global Administrator (PIM expiry greys it out) and a fresh sign-in after activation.
- **Admin center behaviour** **[live ✓, 18 Oct]**: a user's own upload can become a submission that an admin must publish; uploading as an
  administrator (Manage apps, Upload new app) published directly. A newly published org app was not listed for a user several minutes
  later, and installing it for named people from the admin center failed with a generic error (cause **[open]**).
- **Status:** uploaded, published and working for Maya and Daniel in a one-to-one chat, a group-chat @mention, and Microsoft 365 Copilot **[live ✓, 18 Oct]** (Copilot and group chat get one status line and one reply, not a stream). One reply was once refused with `403 BotDisabledByAdmin` (and did not recur).

## 3. Entra app registration: `compass-bot-dev` (the bot)
- **What and why:** the bot's identity everywhere: Bot Service `msaAppId`, the Teams SSO resource, and the confidential client
  that performs the on-behalf-of exchanges. **[repo]** `infra/scripts/Setup-EntraApps.ps1`
- **Must be configured:** sign-in audience single tenant; token version 2; identifier URI `api://botid-{appId}`; exposed scope
  `access_as_user`; pre-authorized the Teams, Microsoft 365 and Outlook client ids; web redirect
  `https://token.botframework.com/.auth/web/redirect`; requested delegated permissions Graph `Files.Read.All` and
  `Sites.Read.All`, Zava API `Member.Read`, Search `user_impersonation`. **[live ✓]** (seven pre-authorized clients, three
  resource entries).
- **Credentials (no secret):** two federated credentials, both audience `api://AzureADTokenExchange`: one whose subject is the bot's
  managed identity (`compass-bot-mi-dev`, for OBO), one for the Bot Service token store (`compass-bot-teams-sso-dev`, for Teams
  SSO). **[live ✓]**
- **Permissions:** the three delegated grants below. **Needed from others:** nothing beyond admin consent.
- **Gotchas:** the managed-identity credential is skipped on the first pass because the identity doesn't exist yet; the owner list
  is empty (fine for a sandbox, a gap for production).

## 4. Entra app registration: `zava-credit-union-api-dev` (the member API's audience)
- **What and why:** gives our own API an identity so tokens can be issued *for it* and the MCP server can check that a token
  was meant for it. **[repo]**
- **Configured:** identifier URI `api://zava-credit-union-api-{appId}`; one scope `Member.Read`; token version 2; no redirect
  URIs, no pre-authorized clients (not needed: only the bot obtains tokens for it, through consent). **[live ✓]**

## 5. Admin consent (four grants)
- **What and why:** tenant-wide consent so no employee is prompted. **[repo]** `infra/scripts/Grant-AdminConsent.ps1`
- **Grants on the bot's service principal, all `AllPrincipals`:** Microsoft Graph (`Files.Read.All`, `Sites.Read.All`); Azure AI Search
  (`user_impersonation`); Zava API (`Member.Read`); and the bot's own `access_as_user` (the web front door). **[live ✓]** for the first three; the fourth is read from `Grant-AdminConsent.ps1`
- **Needs:** an active Global Administrator and `DelegatedPermissionGrant.ReadWrite.All`; the script also needs Graph PowerShell
  modules that may not be installed, so the grants have been applied through Graph REST.

## 6. App Service (the bot host)
- **What and why:** runs `Compass.Bot.Host`: the Agents SDK endpoint, Teams SSO, the on-behalf-of exchange, the workflow, and
  (M11) the web front door. The one component that is neither an MCP server nor a Foundry agent. **[repo]** `infra/modules/appService.bicep`
- **Must be right:** Linux plan **B1 or higher with `alwaysOn: true`**. **[live ✓]** The Free plan was observed with ~55 s cold
  starts, and governance automation resized the live plan to Free once; it is now B1 with Always On, and the sandbox is tagged
  `CostControl=Ignore` (effect unverified).
- **Settings that matter** (each is checked by `/health/ready`): `Connections__ServiceConnection__Settings__*` (AuthType
  `FederatedCredentials`, ClientId, FederatedClientId, TenantId, Scopes, and an **authority** (`AuthorityEndpoint`; without it MSAL
  fails with "authority must be in a well-formed URI format"; a literal `<tenant-id>` placeholder left in `appsettings.json` caused exactly that on 18 Oct, and /health/ready now reports it)); `AgentApplication__UserAuthorization__*` (default handler `teamsSso`,
  OAuth connection name `teams-sso`); `Bot__Obo__*`; `Bot__Auth__*` (audience `api://botid-{appId}`); `Bot__Foundry__*` (project
  endpoint, agent names and versions, knowledge-base MCP endpoint); `Bot__Members__ByObjectId__*`;
  `APPLICATIONINSIGHTS_CONNECTION_STRING`; `AZURE_CLIENT_ID` (so `DefaultAzureCredential` picks the user-assigned identity).
- **Gotchas:** Linux App Service rejects hyphens in setting names, so member mappings use the object id without hyphens;
  logging or plan changes restart the app; a restart looks like "no reply" for ~55 s on a cold start.
- **Identity:** user-assigned managed identity `id-bot-…`. **Holds:** Foundry User and the custom impersonation role (both at
  project scope), Key Vault Secrets User. **[live ✓]**

## 7. Azure Functions (the MCP server)
- **What and why:** the member API, exposed as MCP tools. A real server over simulated banking data. **[repo]**
  `infra/modules/functionApp.bicep`, `src/Mcp/*`
- **Resource:** Flex Consumption (FC1), Linux, .NET 10 isolated, its own storage account (shared-key access disabled), user-assigned
  identity `id-mcp-…`. **[live ✓]**
- **Must be right:** `ZavaApi__Auth__Authority`, valid audiences and issuers (both the identifier URI and the app id, because v2
  tokens carry the app id as `aud`); `AzureWebJobsStorage__*` using managed identity; the MCP webhook authorization level
  `Anonymous` (the JWT middleware is the gate). `ZavaApiTokenValidationMiddleware` rejects any token not issued for this API.
- **Holds:** Storage Blob Data Owner, Queue Data Contributor, Table Data Contributor on its storage account. Nothing else. **[live ✓]**
- **Gotchas:** a governance policy forced storage `publicNetworkAccess` off, which broke the first deploy (opt-out tag; private
  endpoints in production); the MCP extension has a reported issue ignoring the anonymous setting on older host versions.

## 8. Microsoft Foundry: account, project, model
- **What and why:** hosts the agents, the knowledge-base connection and the model deployment. **[repo]**
  `infra/modules/foundryProject.bicep`
- **Resource:** `Microsoft.CognitiveServices/accounts` (kind `AIServices`, S0) with a project `proj-compass-dev`, model `gpt-5.6-sol`
  (GlobalStandard); local auth disabled; public network access enabled; system-assigned identities on account and project.
  **[live ✓]** Preview API versions: the `applications` resource hit a schema error, so per-agent application scope is replaced
  by project-scope RBAC (`CREATE_AGENT_APPLICATIONS=false`). **[open]** Restore per-agent scope.
- **Roles:** see section 12.

## 9. Hosted agents (`SharePointRetrievalAgent`, `ZavaApiAgent`)
- **What and why:** your code in Foundry, receiving the person's tokens as forwarded `x-client-*` headers. The retrieval agent
  calls the Copilot Retrieval API with the person's Graph token; the member agent calls the MCP server with the person's Zava API
  token. Both fail closed if their token is missing. **[repo]** `src/Agents/*`
- **Runtime:** source zip, `dotnet_10`, entry `dotnet <assembly>.dll`, Responses protocol 2.0.0, 0.5 CPU / 1 Gi, adapter
  `Azure.AI.AgentServer.Responses` 1.0.0-beta.6. The member agent needs `ZavaApi__McpEndpoint`. **[live ✓]** Both reached
  `active` at version 1.
- **How the bot calls them:** on each agent's own endpoint, `…/agents/{name}/endpoint/protocols/openai/responses?api-version=v1`. The
  project-level `/openai/v1/responses` route returns 400 for a hosted agent. The agents host the Responses protocol, not Invocations.
  **[live ✓, 18 Oct]**
- **Identity and permissions:** no secret and no role of their own; they act with the person's token. The bot needs the roles in
  section 12 to call them.
- **Published by:** `Compass.Tools.PublishAgents` (a standalone tool because the code-deploy API is only in a prerelease SDK that
  can't share a dependency graph with the stable one).
- **Gotcha:** if publishing is refused with 403 "does not have permissions for …/agents/write", check which identity the .NET
  credential chain picked; forcing `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` fixed it.

## 10. Prompt agent (`SharePointKnowledgeAgent`)
- **What and why:** a declarative agent over the knowledge base's MCP endpoint; no container, no code of its own.
  **[repo]** `src/Agents/Compass.Agents.Prompt.SharePointKnowledge`
- **Configured:** model `gpt-5.6-sol`; tool server `knowledge-base`, tool `knowledge_base_retrieve`; project connection
  `compass-knowledge-base`; the Search token as a **structured input** `search_auth_token` bound to
  `x-ms-query-source-authorization`. **[open]** Learn discourages tokens as structured inputs; replace (see decisions A5, A6).
- **Published by:** the Foundry SDK's agent administration client (`--publish-knowledge-agent`). Version 1 active. **[live ✓]**

## 11. Azure AI Search, knowledge base, knowledge source, project connection
- **What and why:** Foundry IQ over live SharePoint. Nothing is indexed. **[repo]** `infra/modules/aiSearch.bicep`,
  `infra/scripts/Setup-KnowledgeBase.ps1`
- **Resource:** Search `basic`, system-assigned identity, local auth disabled, public access enabled. The knowledge source
  (`zava-sharepoint`, remote SharePoint), the knowledge base (`zava-kb`) and the connection are **data-plane objects created by
  script**, not Bicep, over preview API `2026-08-01-preview`. **[live ✓]** created 17 Oct.
- **Connection:** category `RemoteTool`, `ProjectManagedIdentity`, audience `https://search.azure.com/`. The project's identity
  authenticates the tool call; the person's token rides separately.
- **Second connection, for the portal only:** the Foundry Knowledge (Foundry IQ) page lists knowledge bases only for an AI Search resource
  connected to the project, so `zava-foundry-iq-search` (category `CognitiveSearch`, Entra auth; the service has key auth disabled)
  makes `zava-kb` visible there. The agent does not use it. **[live ✓]** created 18 Oct. The page lists `zava-kb` and its source `zava-sharepoint` (Active); do not press Save in the portal (retrieval reasoning effort shows as unselected).
- **Roles:** Foundry project identity has **Search Index Data Reader** on the Search service; the Search service identity has
  **Cognitive Services User** on the Foundry account (so the knowledge base can use the model). **[live ✓]**
- **Needs from people:** a Microsoft 365 Copilot licence (or Retrieval API pay-as-you-go) per caller; the Search service and
  SharePoint in the same Entra tenant. **No Azure role for end users.** People who open the portal's Knowledge page (an operator or
  admin) need **Search Index Data Reader** and **Search Service Contributor** on the service; Daniel and Maya hold none and their
  briefings work. **[live ✓, 18 Oct]**
- **Limits (Learn):** 200 requests per user per hour; 25 results; hybrid retrieval only for `.docx`, `.pptx`, `.pdf`, `.aspx`,
  `.one`; SharePoint only (no OneDrive or connectors); Search runs one remote query at a time per replica.

## 12. Roles and identities in one place
**Custom role:** `Compass Agent Impersonator (dev)`, data action `…/agents/endpoints/UserIdentityImpersonation/action`,
assignable at the Foundry account. Microsoft Learn's hosted-agent permissions page says this action is not included in any built-in role and shows
"Foundry Agent User Identity Impersonation" as a custom role, so the custom role stays. **[documented]**

| Principal | Kind | Role | Scope | Why |
| --- | --- | --- | --- | --- |
| `id-bot-…` | managed identity | Foundry User | project | call agents |
| `id-bot-…` | managed identity | Compass Agent Impersonator | project | send `x-ms-user-identity` |
| `id-bot-…` | managed identity | Key Vault Secrets User | Key Vault | read config (vault is empty today) |
| `id-mcp-…` | managed identity | Storage Blob Data Owner, Queue and Table Data Contributor | MCP storage account | Functions runtime without keys |
| `proj-compass-dev` | project identity | Search Index Data Reader | Search service | the connection calls the knowledge base |
| `srch-compass-dev-…` | Search identity | Cognitive Services User | Foundry account | the knowledge base uses the model |
| Operator (admin) | user | Owner (inherited) | subscription | provisioning and publishing |
| Portal operators (the operator's account) | users | Search Index Data Reader and Search Service Contributor | Search service | browse the Foundry Knowledge page; not needed by end users |
| End users (Daniel, Maya) | users | **none** | **none** | by design |

## 13. Key Vault, Application Insights, Log Analytics
- **Key Vault:** deployed with RBAC authorization and soft delete; holds **nothing** (no secret exists). Only the bot identity has
  a role on it. Public network access is enabled (production: private endpoint and network ACLs).
- **Application Insights (workspace-based):** receives bot and MCP telemetry. Redaction processors strip token-shaped strings
  from logs and span attributes and **must be registered before the exporters**. They do not scrub exception text; the control
  is that no code path puts a token in an exception message. Hosted agents export once an Application Insights resource is connected to the Foundry project
  (done 19 Oct; the connection name is `appi-compass-dev-<suffix>-9782` and `foundryProject.bicep` can create one). Ingestion lags a few
  minutes.

## 14. Microsoft 365 and SharePoint
- **What and why:** the content and the permissions that decide what comes back. Three libraries with unique (non-inherited)
  permissions: Lending Policy (Daniel), Portfolio and Collections (Maya), Member Services FAQ (both). **[repo]**
  `infra/demo-data/README.md`
- **Needs:** licensed users (Teams and Microsoft 365 Copilot) **[live ✓]** both pass `Assert-DemoUsers`; the site and libraries
  exist **[live ✓]** `zava-compass-demo`; documents uploaded as `.docx` (converted in memory) so semantic retrieval works;
  15 minutes to an hour of indexing after seeding.
- **The seed script needs** an Entra app allowed for PnP sign-in. We created a temporary one (`compass-pnp-seed-dev`);
  **[open]** delete it. Installing PnP.PowerShell into a OneDrive-synced module folder triggered data-loss-prevention alerts.

## 15. Agent 365 registry and admin settings
- **What and why:** the tenant's inventory and governance of agents. Foundry agents sync into it automatically; a custom engine
  agent appears once its package declares it. **[live ✓]**
- **Settings checked and not blocking:** Agent and plugin access (all allowed), User access (all users), agent management rules
  (none that affect us), Teams Manage apps (Unblocked, Everyone), Teams custom-app switches (on). Permission policies no longer
  exist; app availability lives in Manage apps.

## 16. azd, Aspire and the build
- `azure.yaml` defines two services (`bot` on App Service, `mcp` on Functions) and two hooks that run `Setup-EntraApps.ps1`
  before and after provisioning. The Aspire AppHost composes the bot and MCP server locally; Foundry agents are not
  Aspire-orchestrated. `infra` has no secrets; azd environment values are not committed.

---

## Gaps this catalog exposes (to fix or to say out loud)
1. ~~Teams manifest lacks `copilotAgents`.~~ Resolved: `teams/manifest.json` declares `copilotAgents` and the `groupChat` and `copilot` scopes.
2. The bot app has **no owner** listed. Fine for a sandbox; assign owners in production.
3. Hosted-agent instance identities have no explicit roles and are not in the repo; confirm what, if anything, they need
   (they have worked without any).
4. Key Vault's role assignment exists for a vault that holds nothing. Remove the role or the vault, or say why it's there.
5. The impersonation role stays custom: Learn says no built-in role includes the action (see §12 and [`IDENTITY-AND-ACCESS.md`](IDENTITY-AND-ACCESS.md)).
6. Search and Key Vault are public-endpoint; production uses private endpoints and network rules.
7. Per-agent Foundry application scope is replaced by project scope until the preview schema is fixed.
8. The temporary Entra app created for the seed script (`compass-pnp-seed-dev`) is not created by the repo; delete it after seeding.
