# Implementation notes: where the build differs from the dev spec, and known limits

The numbered items below record deliberate differences from [`Zava-Compass-OBO-DevSpec.md`](Zava-Compass-OBO-DevSpec.md), and limits of the build. `D#` refers to a decision in the dev spec and `M#` to its milestones (M0 to M11); the dev spec lists them.

1. **MediatR is not used** (D10): v13+ requires a commercial licence. A hand-rolled `ISender` / `IRequestHandler<,>` /
   pipeline-behavior mediator lives in the new `Compass.Shared.Messaging` project. The MCP `ValidationBehavior` and validators
   stay in `Compass.Mcp.ZavaApi.Application`; the bot has none.
2. **Foundry container protocol (resolved at M10a).** The hosted agents now run on the real `Azure.AI.AgentServer.Responses` adapter
   (`ResponsesServer.Run<THandler>`, `:8088`, `POST /responses`, `GET /readiness`) through thin handlers in `Compass.Agents.Hosting`.
   The platform reports a handler failure as HTTP 200 with `status:"failed"` and only `error.code` + `error.message`, so the
   typed agent error rides in those two fields (claims are packed into the message) and the bot gateway unpacks them.
   **The bot calls a hosted agent on its own endpoint** (`/agents/{name}/endpoint/protocols/openai/responses?api-version=v1`), not the
   project-level `/openai/v1/responses` route, which Foundry rejects for hosted agents with a 400. The stable SDK (2.0.0) can only
   target the project-level route, so `HostedAgentEndpointPolicy` rewrites the URI per call; prompt agents keep the project-level
   route. The agents host the Responses protocol, not Invocations ([`docs/LIVE-FINDINGS.md`](LIVE-FINDINGS.md) §1).
3. **Naming/placement:** `IZavaApiMcpClient` (spec name); `CompassHeaders` lives in `Compass.Shared.Identity.Tokens` and gains a
   `SessionId` header (`x-compass-session-id`) that agents ignore; agent unit tests live in `Compass.Agents.Contracts.Tests`.
4. **Simplifications:** `RetrievalHit` is reduced to the fields Compass uses; the member number comes from configuration
   (`Bot:Members:ByObjectId`, `IMemberNumberResolver`); the session ID is a per-person SHA-256-derived value.
5. **MSAL OBO wrapper** (`MsalOnBehalfOfClient`) creates a fresh `ConfidentialClientApplication` per exchange behind the
   `IOnBehalfOfClient` seam. The seam is tested; the MSAL wrapper itself is untested without a tenant.
6. **Teams SSO token validation** uses the shared `JwtAccessTokenValidator` configured under `Bot:Auth`; the Azure Bot's inbound
   channel token is validated separately (`BotChannelAuthentication`), and the handler runs in a per-turn DI scope.
7. **Redaction limits:** the redaction processors cover log body/message/attributes and span tags/name — **not** exception text
   (see [`docs/OBSERVABILITY.md`](OBSERVABILITY.md)). The bot host also exports to Application Insights, which spec §13
   required but the scaffold lacked. Hosted agents export once an Application Insights resource is connected to the Foundry project
   (see [`OBSERVABILITY.md`](OBSERVABILITY.md)).
8. **Identity-isolation live variants** use ROPC tokens straight to the agents and skip the bot's MSAL OBO exchange (which needs
   the managed identity and can only run in Azure). Cases 3–6 run against fakes only.
9. **Infra:** the impersonation permission is a *data action* in a custom role (`Compass Agent Impersonator (<env>)`); there is
   no "Foundry Agent Consumer" built-in role, so Foundry User is assigned at per-agent scope; hosted-agent versions are
   data-plane only, so `createAgentDeployments` / `agentVersions` are parameters;    the Teams SSO OAuth connection is provisioned
      by `botOAuthConnection.bicep` (M10d). Extras beyond the spec: `botService.bicep`, `Compass.Infra.Common.ps1`.
10. **Aspire:** only `Aspire.Hosting.Azure.Functions` (13.6.1) is used; `Aspire.Hosting.Azure.AppService` was not needed locally.
    The MCP host now references `Compass.ServiceDefaults`.
11. **M9 — knowledge-base agent, Foundry SDK, MAF workflow.** Packages actually used: `Azure.AI.Projects` 2.0.1,
    `Azure.AI.Projects.Agents` 2.0.0, `Azure.AI.Extensions.OpenAI` 2.0.0 (stable; the beta `Azure.AI.Agents` is not used) and
    `Microsoft.Agents.AI` / `Microsoft.Agents.AI.Workflows` 1.25.0 (the preview-only `Microsoft.Agents.AI.Foundry` is not used).
    The Foundry client plumbing (`FoundryResponsesClientFactory`) lives in `Compass.Agents.Prompt.SharePointKnowledge` and is shared
    with the bot gateway. The Foundry SDK rewrites 401 bodies into the OpenAI error shape, so the hosted-agent adapter carries the
    claims challenge in `error.param` (`ResponsesWire`). The knowledge source, knowledge base and Foundry connection are
    data-plane scripts (`Setup-KnowledgeBase.ps1`), not Bicep. The orchestration workflow is built per turn, and person
    resolution and OBO exchanges stay in the query handler, outside the workflow, so the identity contract is untouched.
12. **M11 — streaming, route switch, web front door.**
    - *SharePoint route:* `Bot:SharePoint:Route` (`SHAREPOINT_ROUTE`) = `KnowledgeBase` (default) | `Retrieval` | `Both`; a route that
      is off is never invoked and its token is never requested. Only an exact route name is accepted.
    - *Per-turn scope:* inside that ceiling, a deterministic classifier (`QuestionFocusClassifier`) reads the question and narrows
      the turn to accounts, policy or both. An accounts question never starts the SharePoint branches or requests the Search and
      Graph tokens; a policy question never calls the Zava API and needs no linked member record; an unclear question keeps
      everything the route allows. The scope can only remove branches, never add one. The composer then shows exactly what ran
      and drops a knowledge answer that is blank or "I don't know".
    - *Citations:* the knowledge agent's inline marker only ever names its source "source", so sources are read from the Responses
      `url_citation` annotations (title from the file name, a working SharePoint link) and the markers are stripped from the answer.
    - *Teams streaming:* the workflow reports progress; Teams shows informative updates (rate-limited to 1/s) and then the answer and
      citations in **one** stream per turn, ended before anything else is sent. A follow-up after a failed end carries `replyToId`.
      Group/channel chats get a typing indicator and a single reply instead of a stream. Which ordering rules the Agents SDK
      enforces itself, and which the bot approximates, is listed in the M11 PR description.
    - *Web front door:* the bot host also serves `GET /` (sign-in page) and `POST /api/briefing` (Server-Sent Events). The page is a
      public client (auth-code + PKCE, no secret) of the same app registration; the bearer is validated and exchanged by the same
      code as Teams, and the body can carry only a question — identity is never taken from it. Turn it off with
      `FRONT_DOOR_ENABLED=false` (`Bot:FrontDoor:Enabled`). msal-browser **4.30.0** (LTS, MIT) is vendored, not loaded from a CDN, so
      the CSP can stay `script-src 'self'`; the page is covered by static tests (no inline script, no `innerHTML`).
      Needs the SPA redirect URIs (`Setup-EntraApps.ps1 -BotEndpoint`, and `-LocalPort` for a developer machine only; azd passes it
      for the `dev` environment alone) and the bot-self consent in `Grant-AdminConsent.ps1`. A step-up demand raised inside a
      branch arrives as an `error` event on the stream, and the page retries once with the decoded claims, as it does for a 401.
    - *Configuration pitfall:* a template placeholder left in the Agents SDK `AuthorityEndpoint` setting makes every bot reply fail
      with an MSAL "authority must be well-formed" error; the readiness check (`/health/ready`) now reports it.
