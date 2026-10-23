# The Compass story: one agent, the person's own identity, every system it touches

This is the single narrative for this build — the demo script, the runbook, and the talk track, in one document.
Read it top to bottom and you have the whole story: the simple version first, then the engineering underneath it
one decision at a time, then what production would add that this reference build deliberately does not.

Everything technical below was written at the M9 milestone, and later milestones changed some details; where the code has moved, [`IMPLEMENTATION-NOTES.md`](IMPLEMENTATION-NOTES.md) and [`LIVE-FINDINGS.md`](LIVE-FINDINGS.md) are the record. Where this build
takes a shortcut or stops short of what production needs, it says so explicitly — look for **"In production you
would…"** callouts. Nothing here is aspirational about what *is* built; the gaps are called out, not hidden.

> **Where things stand.** All code is implemented, builds, and is tested. Azure infrastructure has been
> provisioned once into a sandbox tenant (one resource group) to prove the Bicep and
> scripts are real, not just lint-clean — see the [deployment runbook](DEPLOY.md) (section 5) for exactly what has and hasn't been exercised end to end. Steps 1–7 of this script describe what the
> build is *designed* to do; each step says plainly whether it has been run against a live tenant yet.

---

## The one-sentence story

> **Compass answers a loan officer's question using the officer's *own* SharePoint access and the officer's *own*
> member-data access — never a shared "app" identity — across three different retrieval technologies, orchestrated
> by an agentic workflow, and we can prove the identity guarantee holds for two different people, in both call
> orders, with no token ever appearing in a log.**

Everything else in this document is that one sentence, taken apart.

## Cast

| Who | Role in the demo |
| --- | --- |
| **User A — Daniel Park** (`daniel.park@<domain>`, member `ZCU-100234`) | Has access to the **Lending Policy** SharePoint library |
| **User B — Maya Lin** (`maya.lin@<domain>`, member `ZCU-100871`) | Has access to the **Collections Playbook** library, *not* Lending Policy |

(Created by `infra/scripts/Seed-TestUsers.ps1` and `Seed-SharePointTestData.ps1`, test tenant only — never run
against production users.)

---

## Part 1 — The problem, told simply

A credit union builds a Teams assistant ("Compass") so loan officers can ask natural-language questions and get
answers that pull from two places: SharePoint (policy documents) and the credit union's own member-data API. The
naive way to build this is with one shared service identity that reads everything and trusts the application's own
code to filter results before showing them to the person who asked. That is exactly the anti-pattern this whole
build exists to replace: **post-retrieval pruning**, where the app fetches more than the person is allowed to see
and then hides what it decided to hide. It is fragile (one filtering bug is a data leak), it is expensive (fetching
data only to throw most of it away), and it means the application — not SharePoint, not the member-data system —
is the thing deciding who sees what.

The alternative, and the one this build exists to prove end to end, is: **never let the app decide. Let each
system decide, using the real permissions of the real person who asked, every single time.** That requires the
person's own identity to travel, intact, from Teams, through the bot, through an agent platform, all the way to
SharePoint and to the member-data API — without ever being downgraded to a shared app identity along the way, and
without the agent's own prompt or reasoning ever being trusted to carry *who* is asking.

That one requirement — *the person's identity, not the app's, decides every access* — is what On-Behalf-Of (OBO)
token exchange, Entra Agent identity, and every design decision below exist to deliver.

---

## Part 2 — The chain of trust, hop by hop

This is the architecture, told as a sequence of hops, each one naming **what travels on the wire**, **who
obtained it**, and **who checks it**. This is the literal order of operations for one turn.

### Hop 1 — Teams → Azure Bot Service → Compass (Teams SSO)

Daniel is already signed in to Teams with his own Entra account. When he messages Compass, Teams silently exchanges
his Teams session for a token scoped to **Compass's own API** (`api://botid-{appId}/access_as_user`) — this is
Teams single sign-on (SSO): Daniel sees no sign-in prompt, because he already trusts Teams and Teams already trusts
Entra. Azure Bot Service validates the channel and delivers the Activity to `Compass.Bot.Host`.

**What this hop proves:** the bot never asks Daniel to authenticate separately. It inherits his identity from a
channel he already trusts.

### Hop 2 — Compass → Entra: the on-behalf-of exchange (one per downstream system, per turn)

This is the hop that makes everything else possible, so it is worth being precise about. Inside
`GetMemberBriefingQueryHandler`:

1. `TeamsSsoPersonContextResolver` validates Daniel's token and builds a `PersonContext` — his object ID, his name.
   **This happens exactly once per turn.** After this point, nothing downstream re-derives who is asking; every
   agent and every token is handed *this* identity.
2. `SessionId.Mint` derives a session ID unique to Daniel. Compass never accepts a client-supplied session,
   conversation, or response ID — accepting one would let one person's turn be stitched into another's.
3. For **each** downstream system Compass needs — Microsoft Graph, Azure AI Search, and the credit union's own
   Zava API — `OboTokenBroker` performs one MSAL **on-behalf-of (OBO)** token exchange:
   `AcquireTokenOnBehalfOf(scopes, maria's-own-assertion)`. This is the literal mechanism of OBO: Compass presents
   *Daniel's own token* to Entra as proof of who it's acting for, and gets back a *new* token, scoped to exactly one
   downstream audience, that still says "Daniel," not "Compass."
4. Compass's own credential for this exchange is a **federated identity credential trusting its managed
   identity** — not a client secret. There is no secret anywhere in this chain to leak, rotate, or accidentally
   commit. This is the modern, secretless shape of workload identity in Entra.

**The result of this hop: three tokens, three different audiences, one person.** A Graph token, a Search token, and
a Zava-API token — each one usable against exactly one system, each one saying "this is Daniel," none of them
interchangeable with the others.

**In production you would…** also handle **step-up authentication and Conditional Access claims challenges**
correctly here — this build does (`TokenAcquisitionException` carries the MSAL claims challenge back to the bot,
which the brief's identity contract requires), but a production rollout would also want **Continuous Access
Evaluation (CAE)** monitoring, token lifetime tuning matched to expected turn duration, and alerting on abnormal
OBO failure rates (a spike could mean a Conditional Access policy change broke sign-in for a whole population, not
just one person).

### Hop 3 — Compass → Foundry: asserting the person without ever being the person

Compass now calls **Azure AI Foundry** — specifically the Foundry Agent Service, through the **Foundry SDK v2**'s
Responses client (`Azure.AI.Projects` / `Azure.AI.Projects.Agents`), from `FoundryHostedAgentGateway`. This single
class is the *only* place in the whole solution that is allowed to assert a person's identity to Foundry, and that
concentration is deliberate — it's what makes "only the bot may assert a person" a one-file code review, not an
audit of the whole codebase.

The call carries:

- **`Authorization`** — Compass's *own* token (`https://ai.azure.com/.default`), proving Compass itself is allowed
  to talk to this Foundry project. This is Compass's workload identity, not Daniel's.
- **`x-ms-user-identity`** — Daniel's object ID. Not a token — an assertion of *who the downstream call is for*.
  Foundry only honors this header from a caller holding the **impersonation role** (below), which is the control
  that stops any other caller from asserting an arbitrary person.
- **One `x-client-*` header per downstream token** — `x-client-graph-access-token`, `x-client-search-token`,
  `x-client-zava-api-token` — each carrying exactly the token minted for that one audience in Hop 2, with **no
  `Bearer` prefix** (these aren't `Authorization` headers; they're forwarded material the agent will use itself).

This is **Entra Agent ID's on-behalf-of pattern for hosted agents**: Foundry forwards these headers to the
agent's container; it does **not** re-exchange or refresh them. The identity that reaches SharePoint, Search, and
the Zava API three hops from now is still, unambiguously, Daniel's.

**Governance enforced right here:** Compass's managed identity holds exactly two roles on the Foundry project —
**Foundry User** (lets it invoke agents at all) and a **custom role, "Compass Agent Impersonator,"** holding the
one data action that actually matters:
`Microsoft.CognitiveServices/accounts/AIServices/agents/endpoints/UserIdentityImpersonation/action`. Without that
specific data action, Foundry rejects `x-ms-user-identity` outright with a 403 — there is no way to assert a
person without holding this narrowly-scoped permission.

**In production you would…** scope that role assignment *per agent*, not project-wide — this build's own Bicep
(`rbac.bicep`) is written to do exactly that (`perAgentScope=true`), but live provisioning against the current
preview API surface for per-agent application resources hit a schema mismatch (documented in
`infra/modules/foundryAgents.bicep` and `infra/README.md`), so the sandbox deployment fell back to project-scoped
assignment. A production rollout should revisit per-agent scoping before go-live: it is the difference between "if
this credential is compromised, every agent in the project is exposed" and "...only one agent is."

### Hop 4 — Inside Foundry: three different ways to answer, one orchestrator

This is where the story stops being a straight line and becomes a fan-out. For a question that is about both a person's
accounts and the shared policies, Compass doesn't call one agent — it calls **three**, and it needs every answer it
asked for (or none) before it replies. This is handled by a **Microsoft Agent Framework (MAF) workflow**, not
hand-rolled `Task.WhenAll`, and the reason that distinction matters is in Part 3. Which of the three actually run is
decided **per turn** (Hop 5), so a question that only needs accounts, or only needs a policy, runs only that branch.
For now, the three branches:

#### Branch A — `SharePointRetrievalAgent` (hosted agent → Microsoft 365 Copilot Retrieval API)

A **hosted agent**: Foundry runs it as a container, and the bot calls it on **its own endpoint**
(`…/agents/{name}/endpoint/protocols/openai/responses`), speaking the Responses protocol. The project-level
`/openai/v1/responses` route is for prompt agents; Foundry rejects a hosted agent there with a 400, which is the first thing
the live run broke (see [`LIVE-FINDINGS.md`](LIVE-FINDINGS.md) §1).
It reads Daniel's Graph token from `x-client-graph-access-token` and calls
`POST https://graph.microsoft.com/v1.0/copilot/retrieval` with it — **directly, as Daniel**. SharePoint, via
Microsoft 365's own index, decides what comes back. The agent does **no filtering of its own**. If the retrieval
API returns Lending Policy for Daniel and nothing for Maya, that is SharePoint's permission model working exactly
as designed — not a line of Compass code deciding anything. (Afterwards the bot can drop weak matches and write a short
answer from the extracts; see [`DECISIONS-AND-LESSONS.md`](DECISIONS-AND-LESSONS.md) A6a. That is about relevance, never visibility.)

#### Branch B — `SharePointKnowledgeAgent` (prompt agent → Foundry IQ knowledge base → Azure AI Search → SharePoint)

A **second, independent route to the same SharePoint content** — deliberately built side by side with Branch A so
the two can be compared. This is a Foundry **prompt agent** (declarative: instructions + model + one tool, no
container, no code to deploy) whose only tool is a **Foundry IQ knowledge base** with a *remote* SharePoint
knowledge source, reached over MCP.

The person's identity reaches this one differently, because a prompt agent has no `x-ms-user-identity` concept —
there's no hosted container to forward a header *to*. Instead, Daniel's **Search-scope** OBO token
(`https://search.azure.com/.default`) is sent as a **structured input** on the agent run
(`search_auth_token`), and the agent's declared tool binding maps that structured input onto the knowledge base
MCP call's `x-ms-query-source-authorization` header (see `SharePointKnowledgeAgentDefinition` — the header name
and the `{{search_auth_token}}` placeholder are literally in the agent's own definition, authored with the Foundry
SDK v2's agent-version API, `AgentAdministrationClient.CreateAgentVersionAsync`). Azure AI Search then queries
SharePoint live, as Daniel, through that token.

**The teaching point:** two structurally different mechanisms — a direct Graph call with a forwarded bearer token,
versus a declarative tool binding with a per-request structured input — both land on the same guarantee:
SharePoint's own permissions decide, live, no ingested-ACL staleness, no app-side filtering. Maya is excluded
from Lending Policy on *both* routes, because both routes ask SharePoint, not Compass.

**Why have both, when one would demo the point?** Because the honest engineering answer to "which SharePoint
retrieval approach should we use" is "it depends," and showing both side by side is how you earn the right to make
that recommendation instead of asserting it. The Retrieval API (Branch A) is GA, simpler, and the default
recommendation; the Foundry IQ knowledge-base route (Branch B) is how you'd federate SharePoint alongside other
non-SharePoint knowledge sources in one retrieval call — useful, but still preview, with an extra hop and an Azure
AI Search bill. The [design-rationale table](SHAREPOINT-ROUTES.md) walks every option in
the brief (including the ones *not* built — indexed knowledge sources, Search + ACLs/filters, the Foundry
SharePoint tool) and says exactly when you'd choose each.

#### Branch C — `ZavaApiAgent` (hosted agent → MCP → the credit union's own API)

The third branch is structurally different again: it doesn't talk to a Microsoft service at all. It's the credit
union's **own** data — member profile, accounts, transactions — reached over the **Model Context Protocol (MCP)**,
which in this build is a real Azure Functions app (`Compass.Mcp.ZavaApi.Host`), not a Microsoft-hosted service.

The agent forwards Daniel's Zava-API token (from `x-client-zava-api-token`) to the MCP server **unchanged** — this
is MCP's own rule, "never pass a token you received through to a different API," enforced here because the agent
never re-mints or re-scopes it. The Function validates that token's **audience and issuer** before any tool runs
(`ZavaApiTokenValidationMiddleware`) — a token minted for Graph is rejected here on audience mismatch, which is the
concrete, testable proof that MCP's rule actually holds in this codebase, not just in the design doc.

This is also the one branch where the downstream system is **simulated**: `IMemberAccountRepository` is a seeded
SQLite store standing in for the credit union's real core-banking system, because that system doesn't exist for
this reference build. The MCP server's *token handling* is real; what's behind it is a stand-in, built as a clean
port/adapter boundary specifically so a real core-banking adapter is a drop-in replacement later.

### Hop 5 — The orchestrator: scope the turn, fan out, fan in, fail closed

Before any token is exchanged, the handler decides **which branches this turn needs**. There are two gates, and both can
only *remove* work:

1. **The deployment's route** (`SHAREPOINT_ROUTE`): which SharePoint routes this environment is allowed to use at all.
2. **The question's focus** (per turn): a small deterministic classifier (`QuestionFocusClassifier`) reads the wording and
   narrows the turn to *accounts*, *policy*, or *both*. `BriefingRoutePolicy.ScopeFor(focus)` returns a `BriefingScope`. An
   accounts question ("what are my balances?") never starts the SharePoint branches and never requests the Search or Graph
   token; a policy question ("what is the maximum debt-to-income ratio?") never calls the Zava API and needs no linked member
   record; an unclear question keeps everything the route allows. The scope can never add a branch the route forbids.

Observed live as Daniel: an accounts question ran only "Reading your accounts"; a policy question ran only the knowledge base;
a question that mentioned both ran every branch.

The branches in scope then run **concurrently**, inside a Microsoft Agent Framework `Workflow`
(`Compass.Bot.Orchestration`): one `start` node, a fan-out edge to the agent executors in scope, a fan-in **barrier**
edge that only releases once all of them have reported, into one `compose` node that builds the final answer. A branch that is
out of scope is never *built*, so it cannot run and its token is never needed. This is genuine MAF graph construction
(`WorkflowBuilder`, `AddFanOutEdge`, `AddFanInBarrierEdge`) — not a hand-rolled `Task.WhenAll` dressed up to look like one.

Why does the *orchestration mechanism* matter, separately from the identity story? Two reasons, both tested:

1. **It has to stay fail-closed under a different execution model.** MAF reports a failing branch as a workflow
   *event*, not a thrown exception — left alone, a missing token or a claims challenge in one branch would surface
   as a generic workflow error, and the bot would lose the specific, typed signal it needs to tell Daniel
   "please sign in again" instead of a generic failure. `WorkflowBriefingOrchestrator` explicitly rethrows the
   *original* typed exception and abandons the run on the first failure — proven by a test that fails one branch
   while the other two succeed normally, and asserts the turn still produces **no briefing at all**. A half-built
   answer, assembled from two systems that said yes and one that never got asked, is exactly the kind of
   inconsistency this architecture exists to prevent.
2. **Concurrency has to survive the rewrite.** A test (`GatedGateway`) literally blocks each of the three agent
   calls until all three are in flight simultaneously, proving the fan-out is real parallelism, not accidental
   serialization hiding behind a workflow diagram.

Identity-wise, the orchestrator changes **nothing**: the person is still resolved once, before the workflow starts; the
OBO exchanges still happen once per audience *in scope*, not once per workflow node; and no state the workflow builds is
shared across people (a fresh `Workflow` is built per turn).

**In production you would…** likely also add workflow-level timeouts and partial-result policies for
*non-identity* failures (a transient 503 from one downstream, versus an auth failure) — this build intentionally
treats every failure as fail-closed-the-whole-turn, which is the conservative and correct default for an identity
demo, but a production credit-union assistant might reasonably want a UX difference between "I couldn't reach the
policy library right now, here's your account info" and "please sign in again." That's a product decision this
reference build deliberately didn't make, so as not to blur the identity story.

### Hop 6 — The answer comes back, and so does the proof

`BriefingComposeExecutor` only yields a result once every branch in scope has reported, combining what ran — the account
summary, the SharePoint documents, the knowledge-base answer — into one `MemberBriefing`. The composer shows **exactly what
ran**: an accounts question has no document section, a policy question has no account block, and a knowledge answer that is
blank or "I don't know" is dropped rather than printed next to an answer nobody asked for. When the knowledge base does
answer, its sources are the real documents (read from the Responses `url_citation` annotations, shown by file name with a
working SharePoint link) — the model's own inline marker only ever names its source "source", so those markers are stripped.

Daniel asks the policy question and gets "43 percent, exceptions need written VP approval", citing *Consumer Lending Policy*.
Maya, asking the identical question, simply doesn't see it — on either SharePoint route. No code decided that. Different
systems, checking different tokens, independently agreed.

---

## Part 3 — The decisions, and why each one was made

This build required real engineering choices at almost every layer. Here they are, with the reasoning, so this
reads as decisions made for reasons — not a list of packages.

### Why Microsoft Agent Framework (MAF), not a hand-written fan-out?

A hand-rolled `Task.WhenAll` would have worked for *this* demo. MAF earns its place for what comes after the demo:
a declared `Workflow` graph is inspectable, versionable, and extensible (add a fourth branch, add a conditional
edge, add a human-approval gate) without hand-editing concurrency code. It also forced an honest answer to a hard
question — "what does fail-closed mean when failures are events, not exceptions" — that a naive `Task.WhenAll`
rewrite would have let slide. The cast of this story *is* fundamentally an agentic workflow (three independent
agents) rather than a single linear pipeline, so using the framework built for multi-agent orchestration, instead
of ad hoc concurrency primitives, is the right tool for the actual shape of the problem.

The per-turn scope (Hop 5) is a concrete example of that extensibility, and of a design choice worth stating out loud. MAF
could route inside the graph with conditional edges, but tokens are exchanged *before* the workflow runs, so a branch skipped
by an edge would still have had its token requested. Deciding the scope first, and building only the branches in it, keeps
the least-privilege property the route switch already had: a branch that was never built cannot run, and a token that was
never requested cannot be misused.

### Why the Foundry SDK v2, not raw HTTP against the Responses endpoint?

The identity contract — `x-ms-user-identity`, the `x-client-*` headers, no `Bearer` prefix — is exactly the same
whether you hand-roll the HTTP call or use the SDK. What the SDK buys you is everything *around* that contract:
agent addressing by name/version (`AgentReference`) instead of hand-built URLs, typed Responses-protocol objects
instead of hand-parsed JSON, and — critically for the knowledge agent — the **agent-version authoring API**
(`AgentAdministrationClient.CreateAgentVersionAsync`), which is how a prompt agent's declarative definition
(instructions, model, tool bindings, structured inputs) actually gets published to a Foundry project as an
immutable version. That's not an identity requirement; it's what makes the Foundry-native parts of this build look
like Foundry-native code instead of a hand-rolled REST client that happens to work today.

### Why hosted agents for two branches and a prompt agent for the third?

This wasn't an arbitrary mix — it followed from what each branch needed to do. A **hosted agent** is a container:
full code, any logic, any client library, in exchange for you owning the deployment. A **prompt agent** is
declarative: instructions, a model, and tool bindings — no container, no code to ship — in exchange for a narrower
set of things it's allowed to do. `SharePointRetrievalAgent` and `ZavaApiAgent` both needed custom logic (real
Graph/MCP client calls, typed error mapping, fail-closed checks) that only a hosted agent's own code can express.
`SharePointKnowledgeAgent` needed none of that — its entire job is "bind a knowledge base tool and pass through one
token" — which a declarative prompt agent does natively, including the structured-input OBO pattern that doesn't
exist for hosted-agent headers at all. Using a hosted agent for the knowledge route would have been possible but
pointless; using a prompt agent for the MCP route would have been impossible (prompt agents don't run your code).

### Why MCP for the credit union's own API, specifically?

Two reasons. First, it's the pattern Microsoft ships for connecting Foundry agents to arbitrary backend systems
without hand-rolling a Foundry tool for each one — the credit union's own engineers can expose *any* internal API
as an MCP server and get agent connectivity "for free." Second, and more important for this story: MCP's own
specification states a server must never forward a token it received to a different downstream API. That rule is
what makes the member-data branch's audience/issuer validation a *protocol requirement*, not a nice-to-have — this
build enforces it with real middleware and a real failing test (a Graph-scoped token is rejected against the
Zava API's audience), which is the most concrete proof in the whole repo that "a token for one system never
reaches another" isn't just a slogan.

### Why Teams SSO specifically, and not a different sign-in flow?

Loan officers are already signed in to Teams all day — any additional sign-in step is friction a credit union
would reasonably refuse to ship. Teams SSO means Daniel's very first message to Compass already carries his
identity, with zero extra prompts, because Teams and Entra already trust each other. Every downstream OBO exchange
in this build is only possible *because* hop 1 delivers a real, validated Entra identity for free.

### Graph, and where a pattern like Work IQ fits conceptually

The Microsoft 365 Copilot Retrieval API that `SharePointRetrievalAgent` calls **is** a Microsoft Graph endpoint —
`graph.microsoft.com/v1.0/copilot/retrieval` — called with the person's own delegated Graph token. This is the
same category of access pattern used by Microsoft's own M365-grounded agent tooling (the kind of experience a
product like Work IQ provides): delegated Graph permissions, the signed-in person's own token, SharePoint's
permission model enforced by Microsoft 365's index rather than by application code. Work IQ itself is not a
component of this build — it's mentioned here because it's a useful, concrete reference point for *the same
pattern this build proves*, not something this repo calls into.

### Why no secrets, anywhere, ever?

Every identity in this chain — Compass's own workload identity, the agents, the Function App — authenticates with
a **federated identity credential trusting a managed identity**, never a client secret. This isn't a stylistic
preference; it's the single biggest operational-risk reduction available in this architecture. A leaked secret is
a standing, reusable credential until someone notices and rotates it. A federated credential has nothing to leak —
the trust relationship lives in Entra, scoped to a specific managed identity, with no bearer material to steal from
a config file, a log, or a compromised box.

---

## Part 4 — Security and governance: what's enforced today, proven by tests

This isn't a claim — every row below is backed by a real, currently-passing test.

| Requirement | How it's enforced | Proven by |
| --- | --- | --- |
| The person is resolved **once** per turn, never re-derived from a prompt or tool argument | `PersonContext` built once in `GetMemberBriefingQueryHandler`, passed to every branch | `Compass.Bot.Application.Tests` |
| **One** OBO exchange per downstream audience per turn — not one per agent, even with three agent branches | `AcquireDownstreamTokenCommand` called once for Graph, Search, and Zava API each; tokens handed to the workflow, not re-acquired per node | `Compass.Bot.Application.Tests`, `BriefingWorkflowTests` |
| Only the bot may assert a person to Foundry | `x-ms-user-identity` is set in exactly one class (`FoundryHostedAgentGateway`), gated by the custom impersonation role | Architecture review + RBAC |
| The store decides — no post-retrieval pruning, anywhere | Both SharePoint agents return results unfiltered; the MCP agent forwards the token unchanged. The rule protects **visibility** (no code decides what a person may see), not relevance: the bot may drop low-scoring or unhelpful hits afterwards, never add one | Code review + `TokenLeakTests`; `RetrievalRelevanceGateTests` |
| Fail closed — missing/expired token never falls back to app-only | Every agent throws a typed exception before any downstream call if its token is missing | Per-agent fail-closed tests (all three agents) |
| A claims challenge survives the whole chain, intact | `TokenAcquisitionException` carries MSAL's `Claims` string through the orchestrator unchanged | `BriefingWorkflowTests` (claims-challenge case) |
| Two people, both call orders, identical per-person results | The isolation suite runs User A then B and B then A against both SharePoint routes and the MCP route | `Compass.IdentityIsolation.Tests` (cases 1–2) |
| No token ever reaches a log, a trace, or an exception message | `TokenRedactingLogProcessor` / `TokenRedactingActivityProcessor`, registered **before** any exporter | `RedactionPipelineTests`, `TokenLeakTests` (case 3) |
| Per-person session isolation — no client-supplied session ID, ever | `SessionId.Mint` derives a fresh, per-person ID; the bot rejects any supplied one | `Compass.Bot.Application.Tests` |
| A token minted for one system is rejected by another | Audience/issuer validation in the MCP host | `Compass.Mcp.ZavaApi.Tests` |

The redaction story deserves one more sentence because it's easy to undersell: redaction is a **backstop**, not
the control. The actual control is that no code path in this repo ever puts a token anywhere except a `x-client-*`
header, used once. Redaction exists for the day a library, a debugger `ToString()`, or a copy-paste logs one
anyway — see [`OBSERVABILITY.md`](OBSERVABILITY.md) for the exact mechanics and the one honest gap it has today
(exception *text* isn't scrubbed by the processor itself; it's covered instead by "no code path puts a token in an
exception message," which is tested, not merely asserted).

---

## Part 5 — Where this build stops, and what production would add

The instruction behind this document was explicit: **don't change what's built — say what you'd add.** Below is
every meaningful gap between this reference build and a production rollout, organized by the kind of risk it
represents. Nothing in this section describes code that exists; all of it is "in production you would…"

### Network isolation: everything here is public-endpoint

Every Azure resource in this build — the Function App, the App Service, the Foundry account, Azure AI Search, Key
Vault — is deployed with public network access **enabled** (`publicNetworkAccess: 'Enabled'`/`'enabled'` is the
literal default in `foundryProject.bicep` and `aiSearch.bicep`; no VNet, subnet, or private endpoint resource
exists anywhere in `infra/modules/`). That's a deliberate simplification for a reference build meant to be
provisioned quickly into a disposable sandbox tenant — not a production posture.

**In production you would…**

- Put the bot's App Service, the MCP Function App, Azure AI Search, the Foundry account, and Key Vault behind
  **private endpoints** in a dedicated VNet, with `publicNetworkAccess` disabled on each resource and DNS zones
  wired for private resolution.
- Use **VNet integration** (regional VNet integration for App Service/Functions) so outbound calls from the bot and
  MCP host to Entra, Graph, and the Foundry project traverse the VNet rather than the public internet.
- For Foundry specifically, use **Foundry's own network-injection support for agent scenarios** (the
  `ScenarioType.agent` network-injection mode that exists in the current Foundry account schema) so the hosted
  agents' own egress is constrained, not just the bot's.
- Add **Private Link** for the MCP Function App's own inbound traffic if it's ever called from outside the VNet
  boundary the bot runs in.

### No API gateway in front of Foundry or MCP

The bot calls the Foundry project and the agents call the MCP server directly — no **Azure API Management (APIM)**
or dedicated **AI Gateway** layer sits in between.

**In production you would…**

- Front the MCP server (and potentially the Foundry project endpoint) with **APIM** to get centralized rate
  limiting, request/response logging independent of the application's own telemetry, subscription-key or
  mTLS-based client identification for *which deployment of Compass* is calling (useful once there's more than one
  environment hitting a shared backend), and a policy-enforcement point for things like payload size limits.
- Consider an **AI Gateway** pattern (APIM's AI gateway capabilities, or a dedicated Azure AI Gateway–style
  component) specifically in front of the Foundry calls to get centralized token-per-minute/cost governance across
  multiple agents and multiple callers — something a single bot calling two agents doesn't yet need, but a
  multi-team Foundry project with many callers absolutely would.
- This also becomes the natural place to enforce **mutual TLS** between the bot and the MCP server, instead of
  relying solely on bearer-token audience validation at the Function.

### Responsible AI: no content-safety or policy layer configured

This build's Foundry account and agent definitions configure **no content filters, no Responsible AI (RAI)
policies, and no Agent Control Specification (ACS) bindings** — the current Foundry account/model schema supports
rich RAI configuration (content-filter formats, Rego-based ACS policies, intervention points for input/output/
tool-call moderation), none of which this build turns on.

**In production you would…**

- Configure **content filtering** on the model deployment (`gpt-5.6-sol` here) for both prompt and completion, at
  a severity threshold appropriate for a regulated financial-services assistant.
- Define explicit **RAI/ACS policies** for what the knowledge-base prompt agent is and isn't allowed to say,
  particularly given its instructions already ask it to answer only from retrieved sources — a policy layer makes
  that a verifiable control, not just a prompt instruction a model could ignore under adversarial input.
- Add **prompt-injection detection** specifically for the knowledge-base agent, since it ingests third-party
  document content (SharePoint) as part of its grounding — exactly the kind of surface prompt-injection policies
  exist to cover.
- Treat this as a **governance sign-off gate**, not an engineering afterthought: a credit union's compliance
  function should review and approve the RAI configuration before this agent is exposed to real loan officers, the
  same way they'd review any other member-facing decisioning tool.

### Secrets and configuration: no centralized secret rotation posture

This build has *no secrets to rotate* (federated credentials only), which is a real strength — but Key Vault
itself is deployed with no network restriction (`networkAcls` isn't set; it's reachable from any network by
default, subject only to RBAC on who can read a secret). There's also no separation between "configuration" and
"secret" at the infra layer beyond what Key Vault happens to hold.

**In production you would…**

- Restrict Key Vault's network access (private endpoint plus `networkAcls.defaultAction = Deny`) even though
  nothing in it is a long-lived bearer credential today — defense in depth for whatever *does* end up there over
  time (the MCP system key, if you choose that auth mode over anonymous webhook auth — see `infra/README.md`'s
  "MCP auth" step).
- Add **Key Vault diagnostic settings** feeding the same Log Analytics workspace as the rest of the telemetry, so
  "who read which secret, when" is part of the same audit trail as "who called which agent, when."

### Observability: no cross-service correlation, no SIEM integration

Per [`OBSERVABILITY.md`](OBSERVABILITY.md)'s own honest accounting: hosted agents export nothing until an Application Insights
resource is connected to the Foundry project (connected 19 Oct; the hosting library then exports on its own), there's no
custom per-turn correlation ID spanning bot → Foundry → MCP (the standard trace id does carry across), and there are no
saved Kusto queries or dashboards — "search for `eyJ`" is a manual step today, not an alert.

**In production you would…**

- Wire the hosted agents into Application Insights too (or into Foundry's own observability integration, if/when
  that covers hosted-agent containers), so a security reviewer isn't limited to bot-side and MCP-side telemetry.
- Add a genuine per-turn correlation ID that survives the Foundry hop (today each host's W3C trace context is
  independent once a call crosses into the agent container), so one turn's full path is one query, not three.
- Feed Application Insights into **Microsoft Sentinel** (or the credit union's existing SIEM) with saved detections
  for the things this build currently checks only in tests: a JWT-shaped string anywhere in
  `traces`/`dependencies`/`exceptions`, an agent call with no corresponding OBO exchange in the same turn, or an
  unusual rate of `TokenAcquisitionException`/claims-challenge events (a leading indicator of a Conditional Access
  policy change breaking sign-in at scale).
- Author actual **alert rules**, not just the Kusto query this doc hands a reviewer today.

### Resilience and operational hardening not addressed

- **No retry/circuit-breaker policy** is configured for the bot's calls to Foundry or the agents' calls to
  Graph/Search/MCP — a transient 503 today surfaces the same as a real failure. Production would add a resilience
  policy (e.g., `Microsoft.Extensions.Resilience`) distinguishing transient from terminal failures, without
  weakening the fail-closed identity guarantee for genuine auth failures.
- **No autoscaling policy review**: App Service and the Function plan use defaults suited to a demo, not tuned
  capacity planning for expected loan-officer concurrency.
- **No disaster-recovery/multi-region story**: everything is single-region (North Central US in this sandbox run); a
  production credit-union assistant would need a documented RTO/RPO and likely a secondary region for the bot and
  Function App at minimum.

### Summary: what to tell a security/governance reviewer

If asked "is this secure enough to put in front of real members' data," the honest answer this document supports
is: **the identity chain — the hardest and most security-critical part — is real, tested, and sound.** The
remaining gaps are the standard production-hardening checklist for *any* Azure workload (network isolation, a
gateway layer, content-safety policy, SIEM integration, resilience) layered on top of an identity foundation that
is already correct. That's the right order to have built this in: get the hard, novel, security-critical part
right and proven first; the rest is well-trodden Azure landing-zone practice that any production rollout would
apply as a matter of course.

---

## Part 6 — Running the proof yourself

### What runs today, with no cloud access at all

```powershell
dotnet test tests/Compass.IdentityIsolation.Tests --logger "console;verbosity=normal"
```

This exercises the real bot application code and both real agent hosts, against an in-process fake Entra (only
Entra, SharePoint, and the Zava API are faked — everything else is the genuine code path). It proves, for two
people in both orders:

- Each person sees only their own documents and their own member data, regardless of call order.
- No JWT-shaped string, and none of the exact token values the fake Entra minted, appear anywhere in captured
  logs, exceptions, or HTTP response bodies.
- A missing token produces no downstream call at all; an expired token is never reused; a claims challenge
  survives end to end.

The `Live_*` variants of these same tests show as **skipped** until a real test tenant is configured — they prove
the *code's* behavior unconditionally; only a live run proves the *tenant's* configuration is also correct.

### What needs the live handoff first

Steps 1–7 of this story — a real loan officer asking a real question in Teams and getting a real answer from real
SharePoint and a real (simulated-backend) member API — require completing the
[deployment runbook](DEPLOY.md): provisioning (done once, into
`<resource-group>`), admin consent, Teams SSO configuration, the knowledge-base setup script, test
data seeding, and a deployment. Do not present the live Teams steps as working until that checklist is complete
for the tenant you're demoing against.

---

## Q&A cheat sheet

- **"Why not Foundry's SharePoint tool?"** It needs every caller to hold a Foundry identity/license and is
  explicitly documented as not working when the agent is published to Teams — ruled out in the brief, not an
  oversight here.
- **"Does the model ever see a token?"** No. Every token travels only in an `x-client-*` header or a structured
  input to the agent's tool binding; none of it ever enters a prompt or a tool argument the model reasons over.
- **"What if I revoke someone's SharePoint access mid-session?"** Both SharePoint routes in this build query
  live — the next call reflects the revocation immediately. There's no ingested ACL copy to go stale, because this
  build deliberately didn't build the indexed-knowledge-source option (see the [SharePoint routes comparison](SHAREPOINT-ROUTES.md) for
  exactly where that staleness risk *would* live, and when you'd accept it anyway).
- **"Is the hosted-agent container contract production-real?"** It's a careful approximation of Foundry's
  hosted-agent protocol (Responses envelope over `POST /responses`/`GET /readiness`), built against the documented
  contract, not against a verified live container — flagged honestly in [IMPLEMENTATION-NOTES.md](IMPLEMENTATION-NOTES.md).
- **"Is this secure enough for production as-is?"** The identity chain is real and tested. Network isolation, an
  API gateway, Responsible AI policy, and SIEM integration are not built — see Part 5. That's the honest, complete
  answer, not a sales pitch.
