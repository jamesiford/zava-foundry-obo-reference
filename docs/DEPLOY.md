# Deploying to your own tenant

How to stand this reference build up in your own Azure subscription and Microsoft Entra tenant, step by step, with what each step does, how to check it worked, and what to do when it does not. Read [`IDENTITY-AND-ACCESS.md`](IDENTITY-AND-ACCESS.md) for the identity model and the exact roles each step needs, and [`TROUBLESHOOTING.md`](TROUBLESHOOTING.md) when something fails.

**What you will have at the end:** a Teams bot (and a web page) called Compass, running on Azure App Service; an MCP server on Azure Functions; three Foundry agents; a Foundry IQ knowledge base; two demo people who get different answers to the same question because SharePoint, not the application, decides. **This is a reference build, not a production deployment** (see the status section of the [README](../README.md)).

Tags used below: **exercised** means the step was run against a sandbox tenant while building this repository (the evidence is in [`LIVE-FINDINGS.md`](LIVE-FINDINGS.md)); **not exercised** means it is documented from the code and has not been run. Steps 1 to 8 were also run again on 21 Oct 2026 from a clean clone of the public export into a new environment, following this document; the gaps that run found are fixed here and listed in [`TROUBLESHOOTING.md`](TROUBLESHOOTING.md). Step 9 (the Teams upload) was **not exercised** in that run.

## 1. Prerequisites

### 1.1 Tools

Run the checker first. It is read-only:

```powershell
./infra/scripts/Test-Prerequisites.ps1 -Mode Deploy
```

| Tool | Needed for |
| --- | --- |
| PowerShell 7 (`pwsh`) | Every script, and the azd hooks |
| .NET 10 SDK or later | Building, the publish tool, publishing the prompt agent |
| Azure CLI (`az`) | azd, `Set-MemberMapping.ps1`, `Setup-KnowledgeBase.ps1` |
| Azure Developer CLI (`azd`) | `azd provision` and `azd deploy` |
| `Microsoft.Graph.Authentication`, `.Applications`, `.Identity.SignIns`, `.Users` | The Entra scripts |
| `PnP.PowerShell` | Only `Seed-SharePointTestData.ps1`. Install it **outside any OneDrive-synced folder**: its help files in a synced module path triggered data-loss-prevention alerts |
| `Az.Accounts`, `Az.Resources` | Only the optional `New-FoundryImpersonationRole.ps1` fallback |
| Docker, Azure Functions Core Tools | Only to run the Aspire AppHost locally |

### 1.2 Who needs which role

The person running the deployment needs all of the following, or someone who has them must run the matching step. Full detail, derived from the scripts and Bicep, is in [`IDENTITY-AND-ACCESS.md`](IDENTITY-AND-ACCESS.md).

| Step | Needs |
| --- | --- |
| `azd provision` (Bicep, role assignments) | Owner, or Contributor plus User Access Administrator, on the resource group |
| Creating the Entra app registrations (`Setup-EntraApps.ps1`, run by an azd hook) | An Entra role that can create applications, for example Application Administrator |
| Tenant-wide admin consent (`Grant-AdminConsent.ps1`) | A Global Administrator or Privileged Role Administrator (the script says so if you are not one) |
| Publishing the hosted agents | The Foundry Project Manager role on the Foundry project |
| Creating the SharePoint site and seeding it | Site owner on the site |
| Publishing the Teams app | A Teams administrator (a user's own upload can become a submission an admin must publish) |
| End users | **No Azure role at all.** A Microsoft 365 Copilot licence is needed for the Retrieval API route and the knowledge-base route |

### 1.3 The two demo people

`Assert-DemoUsers.ps1` expects **two existing users** in your tenant, `daniel.park@<your-domain>` and `maya.lin@<your-domain>`, each with a Teams service plan and a Microsoft 365 Copilot service plan. **No script creates users, groups or passwords.** To use different people, change the user principal names and member numbers in `Get-CompassDemoUser` in `infra/scripts/Compass.Infra.Common.ps1`. You also need a SharePoint site you can create libraries in.

### 1.4 Quota, region and cost

- **Model quota.** `foundryProject.bicep` deploys a model (parameters `foundryModelName`, `foundryModelVersion`, `foundryModelDeploymentName`; defaults `gpt-5.6-sol`, `2026-07-09`) with the `GlobalStandard` SKU and a capacity of 50. You need that quota in your region, or change the parameters. Pick a region where Foundry projects, hosted agents and Azure AI Search are available.
- **Billable resources** (SKUs from the Bicep): App Service plan (`botPlanSku`, default B1, Always On), Function App on Flex Consumption (FC1), Azure AI Search (SKU `basic`), the Foundry account with the model deployment, Application Insights and Log Analytics (PerGB2018), Key Vault (standard), Azure Bot (F0). Use the [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/) for current prices; this repository states none.
- **Cleanup** is in section 4. Resources keep billing until you remove them.

## 2. What is automated and what is not

| # | Step | Automated by | Needs a human |
| --- | --- | --- | --- |
| 1 | Create the azd environment | You (`azd env new`) | Yes |
| 2 | Entra app registrations, scopes, SPA redirect URIs | azd `preprovision` hook running `Setup-EntraApps.ps1` | An Entra role (1.2) |
| 3 | Azure resources, custom role, RBAC, Azure Bot, Teams channel, bot OAuth connection | Bicep via `azd provision` | Azure roles (1.2) |
| 4 | Federated identity credential (managed identity trusted by the bot app) | azd `postprovision` hook, the second run of `Setup-EntraApps.ps1` | No |
| 5 | Tenant-wide admin consent | `Grant-AdminConsent.ps1`, or the hook when `RUN_ADMIN_CONSENT=true` | A tenant admin |
| 6 | Publish the two hosted agents | `tools/Compass.Tools.PublishAgents` (a command, not an azd hook) | Foundry Project Manager |
| 7 | Tell the bot which agent versions to call | `azd env set` plus `azd provision`, or `az webapp config appsettings set` | Yes |
| 8 | Knowledge base, knowledge source, project connections | `Setup-KnowledgeBase.ps1` (data-plane REST) | Yes |
| 9 | Publish the prompt agents | `dotnet run --project src/Bot/Compass.Bot.Host -- --publish-knowledge-agent`; also `-- --publish-answer-agent` for the route A summary | Yes |
| 10 | SharePoint site | A short script (step 7) or the SharePoint admin center | Yes |
| 11 | Seed the libraries and their permissions | `Seed-SharePointTestData.ps1` (needs PnP.PowerShell and an Entra app for its sign-in, step 7) | Yes, then check in the browser |
| 12 | Map each person to a member number | `Set-MemberMapping.ps1` | Yes |
| 13 | Deploy the bot and the MCP server code | `azd deploy` | No |
| 14 | Build the Teams package and publish it | `teams/Build-TeamsPackage.ps1`, then **manual** upload | A Teams administrator |

## 3. Steps

All commands run from the repository root unless a `cd infra` says otherwise.

### Step 1: create the azd environment (exercised)

```powershell
cd infra
azd auth login
azd env new dev                      # the name must be dev or test
azd env set AZURE_LOCATION <region>
azd env set AZURE_RESOURCE_GROUP <resource-group>
```

Create the resource group first if it does not exist (`az group create --name <resource-group> --location <region>`).

**If you run this from a shell with no window** (an agent, CI, a remote session), the `preprovision` hook fails with `A window handle must be configured`. Supply a Graph token from your signed-in Azure CLI account first, and again whenever it expires or after you activate an admin role:

```powershell
$env:COMPASS_GRAPH_ACCESS_TOKEN = (az account get-access-token --resource-type ms-graph --query accessToken -o tsv)
```

The Azure CLI account must hold the Entra roles in section 1.2. Several later steps use the same token. Optional switches: `SHAREPOINT_ROUTE` (`KnowledgeBase`, the default, `Retrieval` or `Both`) and `FRONT_DOOR_ENABLED` (`true`, the default). Set `SANDBOX_POLICY_OPT_OUT=true` **only** in a disposable tenant whose governance policy blocks the Function deployment (see [`infra/README.md`](../infra/README.md)).

### Step 2: provision (exercised)

```powershell
azd provision
```

The `preprovision` hook signs you in to Microsoft Graph interactively and creates the app registrations. After Bicep, the `postprovision` hook adds the federated credential and writes `infra/.generated/teams-webApplicationInfo.json`. `CREATE_AGENT_APPLICATIONS` defaults to `false` (per-agent Foundry applications are blocked by a preview API issue; RBAC is at project scope).

If `azd provision` is interrupted after Azure has started deploying, the deployment still finishes in the portal but azd never records the outputs or runs the `postprovision` hook. Run `azd provision` again: it reports no changes, stores the outputs and runs the hook.

**Check:** `azd env get-values` lists `BOT_APP_ID`, `ZAVA_API_APP_ID`, `SERVICE_BOT_ENDPOINT`, `SERVICE_MCP_ENDPOINT` and `FOUNDRY_PROJECT_ENDPOINT`. On the bot app registration, the federated credential's issuer is `https://login.microsoftonline.com/<tenant-id>/v2.0`, its subject is the bot managed identity's principal id and its audience is `api://AzureADTokenExchange`.

### Step 3: admin consent (exercised)

A tenant administrator runs:

```powershell
./scripts/Grant-AdminConsent.ps1 -Environment dev
```

Or set `RUN_ADMIN_CONSENT=true` before provisioning if the person running `azd` is an administrator. **Check:** no consent prompt appears when the web page signs in.

### Step 4: publish the hosted agents (exercised)

Preview the plan (offline, nothing is built or uploaded), then publish. Pin the credential to your `az` account first: the tool uses `DefaultAzureCredential`, which can pick a different signed-in identity (Visual Studio, for example) and then fail with a 403 that names an object id you do not recognise.

```powershell
$env:AZURE_TOKEN_CREDENTIALS = 'AzureCliCredential'
```

Then preview and publish:

```powershell
dotnet run --project ../tools/Compass.Tools.PublishAgents -- --what-if
dotnet run --project ../tools/Compass.Tools.PublishAgents -- `
    --project-endpoint $(azd env get-value FOUNDRY_PROJECT_ENDPOINT) `
    --mcp-endpoint $(azd env get-value SERVICE_MCP_ENDPOINT)
```

This runs `dotnet publish` for linux-x64, creates a new agent version from the zip (runtime `dotnet_10`, Responses protocol 2.0.0) and polls until it is `active`. Publish **after** provisioning, because the MCP endpoint is baked into the ZavaApi agent version, and publish again if that host name changes. It needs the Foundry Project Manager role. The tool writes `infra/.generated/agent-versions.json`.

**Optional: Microsoft Agent 365 observability (exercised in dev on 22 Oct 2026, up to the Agent 365 side).** Foundry may already export your hosted agents' traces to Agent 365 without any switch from you: in dev, a version published without a flag did. `--agent365` writes `FOUNDRY_AGENT365_TRACING_ENABLED=true` into both agents' definitions explicitly (Foundry accepted it), and our own `invoke_agent` and `execute_tool` spans were then seen in Application Insights. Application Insights keeps working either way. With the flag on, the hosted agent fails at startup, naming the missing variable, unless `FOUNDRY_HOSTING_ENVIRONMENT`, `FOUNDRY_AGENT_INSTANCE_CLIENT_ID` and `FOUNDRY_AGENT_TENANT_ID` are present (the agents started in dev, so they were). Your tenant needs an Agent 365 licence and the agent instance registered (or the `Agent365.Observability.OtelWrite` role with admin consent). Our spans carry no token, message content or person identifier; the platform adds a `user.id`, which in dev was the bot's managed identity, not a person. Whether Agent 365 shows the runs, and whether setting the variable to `false` stops the export, were not checked. See [DECISIONS B6a](DECISIONS-AND-LESSONS.md).

### Step 5: tell the bot which versions to call (exercised)

```powershell
azd env set SHAREPOINT_RETRIEVAL_AGENT_VERSION <n>
azd env set ZAVA_API_AGENT_VERSION <n>
azd provision
```

For a quick iteration you can instead set `Bot__Foundry__<Agent>__Version` app settings on the bot App Service with `az webapp config appsettings set`; do the durable form before the next provision or the settings are blanked. Restart the web app after changing them.

### Step 6: the knowledge base (exercised)

```powershell
./scripts/Setup-KnowledgeBase.ps1 -WhatIf `
    -SearchServiceName $(azd env get-value AZURE_SEARCH_NAME) `
    -FoundryProjectResourceId "$(azd env get-value FOUNDRY_ACCOUNT_ID)/projects/$(azd env get-value FOUNDRY_PROJECT_NAME)" `
    -FoundryAccountName $(azd env get-value FOUNDRY_ACCOUNT_NAME) `
    -ModelDeployment <model-deployment-name>
```

Drop `-WhatIf` to apply. It creates the remote SharePoint knowledge source, the knowledge base, the project connection the prompt agent uses, and an AI Search connection that makes the knowledge base visible on the Foundry portal's Knowledge page (data-plane preview API `2026-08-01-preview`). Then publish the prompt agent, pointing the bot configuration at your project and knowledge base:

```powershell
cd ..
$env:Bot__Foundry__ProjectEndpoint = '<FOUNDRY_PROJECT_ENDPOINT>'
$env:Bot__Foundry__KnowledgeAgent__KnowledgeBaseMcpEndpoint = '<knowledge base MCP URI printed by Setup-KnowledgeBase.ps1>'
$env:AZURE_TOKEN_CREDENTIALS = 'AzureCliCredential'     # use the signed-in az account
dotnet run --project src/Bot/Compass.Bot.Host -- --publish-knowledge-agent
```

**Route A answer agent (only if you use `SHAREPOINT_ROUTE=Retrieval` or `Both` with the summary on).** Publish the second prompt agent the same way. It has no tools and needs no knowledge base, only the project endpoint and the model deployment:

```powershell
$env:Bot__Foundry__AnswerAgent__ModelDeployment = '<model-deployment-name>'
dotnet run --project src/Bot/Compass.Bot.Host -- --publish-answer-agent
```

Bicep (`azd provision`) sets `Bot__Foundry__AnswerAgent__AgentName` and `__ModelDeployment` on the bot. For a quick test without `azd provision`, set them on the web app with `az webapp config appsettings set`, together with the route and the two retrieval settings. The route, the gate and the summary are controlled by these settings (each is checked by name in `/health/ready`, never by value):

| Setting (azd variable) | Default | Meaning |
| --- | --- | --- |
| `Bot__SharePoint__Route` (`SHAREPOINT_ROUTE`) | `KnowledgeBase` | `Retrieval` selects route A |
| `Bot__SharePoint__Retrieval__Summarise` (`RETRIEVAL_SUMMARISE`) | `true` | Route A writes a short cited answer from the extracts; `false` lists the extracts as before |
| `Bot__SharePoint__Retrieval__MinRelevance` (`RETRIEVAL_MIN_RELEVANCE`) | `0` | Drops hits scoring below this (0 to 1). `0` cuts nothing. **[observed]** the 22 Oct scores did not separate relevant from not-relevant, so leave it at `0` until you have measured your own documents (decisions A6a) |
| `Bot__Foundry__AnswerAgent__AgentName`, `__AgentVersion`, `__ModelDeployment` | `SharePointAnswerAgent`, latest, project model | Which answer agent version the bot calls |

**Privacy, open.** **[observed, 22 Oct]** Foundry platform tracing records the answer agent's input and output text (the extracts, the question and the answer) in the Application Insights resource connected to the project (`gen_ai.input.messages`, `gen_ai.output.messages`). **[documented]** Learn describes tracing as collecting prompts, responses and tool calls for all agents in a connected project; the controls it names are disconnecting Application Insights, limiting who holds **Log Analytics Reader** (and **Privileged Monitoring Data Reader** where the tables are protected), retention policies and redaction. We found no per-agent switch for content recording and changed nothing. Decide this before using real documents.

### Step 7: demo data (exercised)

1. **Create a SharePoint site you own.** Either create a team site in the SharePoint admin center, or create a Microsoft 365 group (its site is created for you, usually within a minute). The group form, run as the person who will own the site:

   ```powershell
   $body = @{ displayName = 'Compass Demo'; mailNickname = 'compass-demo'; mailEnabled = $true; securityEnabled = $false; groupTypes = @('Unified'); visibility = 'Private' } | ConvertTo-Json -Compress
   Set-Content $env:TEMP\grp.json $body
   $group = az rest --method post --url https://graph.microsoft.com/v1.0/groups --headers 'Content-Type=application/json' --body "@$env:TEMP\grp.json" -o json | ConvertFrom-Json
   az rest --method get --url "https://graph.microsoft.com/v1.0/groups/$($group.id)/sites/root" --query webUrl -o tsv
   ```

   The last line prints the site URL (retry after 20 seconds if it is not ready). Keep that account as the site owner: the seed script runs as it.
2. Confirm the two people: `./infra/scripts/Assert-DemoUsers.ps1 -TenantDomain <your-domain>` (read-only).
3. **Create an Entra app for PnP sign-in.** Current `PnP.PowerShell` will not sign in without your own app registration. Create a single-tenant **public client** app with the redirect URIs `http://localhost` and `https://login.microsoftonline.com/common/oauth2/nativeclient`, the delegated permission **SharePoint `AllSites.FullControl`**, and grant tenant-wide admin consent. (PnP also ships `Register-PnPEntraIDAppForInteractiveLogin`, which does this; it was not exercised here.) Note its application (client) id, and delete the app in cleanup. This is separate from the two apps `Setup-EntraApps.ps1` creates.
4. **Install `PnP.PowerShell` outside any OneDrive-synced folder** (see 1.1), for example `Save-Module PnP.PowerShell -Path C:\tools\psmodules`, then add that folder to `$env:PSModulePath` for the session.
5. Seed: `./infra/scripts/Seed-SharePointTestData.ps1 -SiteUrl <site url> -TenantDomain <your-domain> -PnPClientId <client id> -DeviceLogin`. With `-DeviceLogin` it prints a code and a sign-in page; sign in as the site owner. A script run from a shell with no window needs `-DeviceLogin`.
6. **Open each library in the browser as the person it belongs to.** Expected: *Lending Policy* has two documents and only Daniel can open it; *Portfolio and Collections* has two and only Maya can open it; *Member Services FAQ* has one and both can. (An older copy of the script printed "Skipped (exists)" for files it had not uploaded; that is fixed, but check anyway. [`infra/demo-data/README.md`](../infra/demo-data/README.md) says how to upload by hand.)
7. Map people to member numbers: `./infra/scripts/Set-MemberMapping.ps1 -TenantDomain <your-domain> -ResourceGroup <resource-group> -WebAppName <bot app name>`. The bot's `/health/ready` returns 503 listing `Bot:Members:ByObjectId` until this has run; restart the web app afterwards.
8. Allow about 30 minutes before the first question: SharePoint has to index the files.
### Step 8: deploy the code (exercised)

```powershell
cd infra
azd deploy
```

**Check:** `GET https://<bot-host>/health/ready` returns 200. A 503 lists the **names** of missing settings, never their values. Re-run `azd provision` (or `Setup-EntraApps.ps1 -BotEndpoint <SERVICE_BOT_ENDPOINT>`) so the deployed origin is a registered SPA redirect URI for the web page.

### Step 9: Teams (documented; not exercised in the clean-clone run)

```powershell
./teams/Build-TeamsPackage.ps1
```

This writes `teams/dist/Compass-Teams-App.zip` (the bot app id and the SSO settings come from the azd environment). **A tenant administrator publishes it. Do not have an end user upload it with "Upload a custom app" in Teams:** a user's own upload can become a submission that an admin must still approve, and it is not offered to anyone else.

1. Sign in to the [Teams admin center](https://admin.teams.microsoft.com) as a Teams Administrator or Global Administrator.
2. Go to **Teams apps**, then **Manage apps**.
3. Choose **Upload new app** (it can also appear under **Actions**), and select `teams/dist/Compass-Teams-App.zip`. An admin upload publishes straight to the organisation's app catalogue.
4. Open the app in the list and check that its status is **Allowed**.
5. Make it available to the two people: under **Teams apps**, **Permission policies** (or **Setup policies**, to pin it), allow the app for the policy they use, or assign a policy that does. Policy changes can take a few hours to reach users.
6. As each person, open Teams, find **Compass** under **Apps** (**Built for your org**) and add it.

The manifest declares `personal`, `groupChat` and `copilot` scopes. To change the package, bump `version` in `teams/manifest.json`, rebuild, and upload it as a new version of the existing app (in **Manage apps**, open the app and choose **Update**; the manifest `id` must not change). If a person sees "You cannot send messages to this bot", see [`TROUBLESHOOTING.md`](TROUBLESHOOTING.md#teams).
### Step 10: verify (exercised)

1. Open the bot's root URL, sign in as each person, and ask the control question ("What is the cutoff time for outgoing domestic wires?") and a policy question only one of them may read ([`infra/demo-data/README.md`](../infra/demo-data/README.md) lists the questions and expected answers).
2. Each person must see only their own accounts and only the documents they may open.
3. In Teams, send the same questions in a one-to-one chat.
4. Offline, with no cloud: `dotnet test tests/Compass.IdentityIsolation.Tests`.

## 4. Cleanup (exercised on 21 Oct 2026)

`azd down` removes the Azure resources. Foundry and Cognitive Services accounts, Key Vaults and Log Analytics workspaces are soft-deleted, so add `--purge` to release their names:

```powershell
cd infra
azd down --purge --force
```

**It deletes the whole resource group, including one you created yourself before step 1.** Do not point `AZURE_RESOURCE_GROUP` at a group that holds anything you want to keep. In the clean-clone run this took about three minutes, and afterwards the group, the soft-deleted Foundry account and the soft-deleted Key Vault were all gone.

`azd down` does **not** remove anything in Entra, SharePoint or Teams. In the clean-clone run these were left behind, and all of them need deleting (a Cloud Application Administrator or higher can do it; the consent grants go with the app registrations):

| Left behind | Name | How it was found |
| --- | --- | --- |
| App registration | `compass-bot-<env>` | Created by `Setup-EntraApps.ps1` |
| App registration | `zava-credit-union-api-<env>` | Created by `Setup-EntraApps.ps1` |
| App registrations (one per agent) | `aif-<env>-<suffix>-proj-compass-<env>-<Agent>-<hash>-AgentIdentityBlueprint` | Created by Foundry when an agent is published; three in the run (two hosted, one prompt) |
| Service principals (one per agent) | `aif-<env>-<suffix>-proj-compass-<env>-<Agent>-AgentIdentity` | Created by Foundry; two remained after the blueprint apps were deleted |
| App registration | the one you made for PnP sign-in (step 7) | You created it |
| Microsoft 365 group and its SharePoint site | the group you made in step 7 | You created it (skip if you used an existing site) |
| Teams app | Compass | Remove it in the Teams admin center, **Manage apps** |

Delete by object id, after checking the display names, so that you never remove another environment's apps. For example:

```powershell
$token = az account get-access-token --resource-type ms-graph --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $token" }
$filter = "startswith(displayName,'aif-compass-<env>')"
Invoke-RestMethod -Headers $headers -Uri "https://graph.microsoft.com/v1.0/applications?`$filter=$filter&`$select=id,displayName"
Invoke-RestMethod -Method Delete -Headers $headers -Uri "https://graph.microsoft.com/v1.0/applications/<object-id>"
# Deleted objects stay recoverable for 30 days and keep their names; remove them for good:
Invoke-RestMethod -Method Delete -Headers $headers -Uri "https://graph.microsoft.com/v1.0/directory/deletedItems/<object-id>"
```

Use `Invoke-RestMethod` rather than `az rest` with a `$filter`: the `az` `.cmd` shim on Windows breaks the quoting.
## 5. What was not exercised

- The GitHub Actions `deploy-dev` job (a GitHub federated credential for azd and repository variables; the job is disabled).
- The live variants of the isolation tests (they need password-based sign-in for test users; the offline variants run in CI).
- A real claims step-up demand from Conditional Access.
- Step 9, the Teams upload by an administrator (documented from the Teams admin center, not run in the clean-clone test).
- Per-agent Foundry application scope and per-agent RBAC (blocked by a preview API issue).
- The route A answer agent (`--publish-answer-agent`) was exercised in dev on 22 Oct 2026 (12 questions). **Not checked:** a per-agent role scope for it, a way to switch off Foundry content recording for it (it is recorded in Application Insights; see step 6), and any tenant other than dev.
- Agent 365 export (`--agent365`). Exercised in dev on 22 Oct 2026 as far as Application Insights shows: our spans appeared, posts returned 200, no `eyJ` text. **Not checked:** that the runs appear in Agent 365, whether setting the variable to `false` stops the export, and whether the idle `observabilityService` stream then stops.
