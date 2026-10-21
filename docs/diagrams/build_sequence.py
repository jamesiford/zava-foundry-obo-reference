#!/usr/bin/env python
"""Build the Compass OBO token-flow sequence diagram as one self-contained HTML file.

Same Clawpilot theme and interaction model as the C4 architecture diagram, but this
answers a different question: the architecture shows *structure* (what talks to what,
as whose identity); this shows *time* (the order tokens are obtained and forwarded).

The model below is the source of truth. Edit it and re-run.
"""
import json, pathlib

OUT = pathlib.Path(__file__).with_name("Zava-Compass-OBO-Sequence.html")

MODEL = {
 "title": "Compass \u00b7 how the person's identity reaches SharePoint and member data",
 "subtitle": "The token flow for one turn, in order. Every arrow says what is on the wire.",
 "asOf": "18 October 2026",
 "summary": "Compass's bot holds the person from Teams, performs one on-behalf-of exchange per system, and "
            "hands the tokens to an Agent Framework workflow that calls the agents the question needs, in parallel. Foundry "
            "forwards the tokens \u2014 it never exchanges or refreshes them. SharePoint and the member API decide "
            "what comes back.",
 "lanes": [
  {"id":"P",  "title":"Daniel \u00b7 Maya",    "sub":"Microsoft Teams",            "kind":"person"},
  {"id":"BS", "title":"Azure Bot Service","sub":"channel + token store",       "kind":"azure"},
  {"id":"A",  "title":"Compass bot",      "sub":"Agents SDK \u00b7 trusted backend","kind":"core"},
  {"id":"E",  "title":"Entra ID",         "sub":"issues every token",          "kind":"entra"},
  {"id":"W",  "title":"Agent Framework",  "sub":"fan-out \u00b7 fan-in \u00b7 fail closed","kind":"core"},
  {"id":"F",  "title":"Foundry Agent Service","sub":"forwards, never exchanges","kind":"foundry"},
  {"id":"K",  "title":"Prompt agent",     "sub":"SharePointKnowledgeAgent",    "kind":"foundry"},
  {"id":"S",  "title":"Foundry IQ KB",    "sub":"Azure AI Search",             "kind":"azure"},
  {"id":"R",  "title":"SharePoint",       "sub":"Retrieval API \u00b7 remote source","kind":"m365"},
  {"id":"H",  "title":"Hosted agents",    "sub":"SharePointRetrieval \u00b7 ZavaApi","kind":"foundry"},
  {"id":"M",  "title":"Zava API MCP",     "sub":"Functions \u2192 core banking", "kind":"azure"},
 ],
 "steps": [
  {"n":1,"from":"P","to":"BS","hop":1,"status":"verified",
   "label":"message Activity",
   "detail":{"carries":"The person's Teams session.",
             "mechanism":"Normal Teams sign-in. The person is already authenticated to Microsoft 365.",
             "enforced":"Entra, including any Conditional Access policy on the Teams app.",
             "why":"Nothing here is new. Daniel and Maya each sign in as themselves, in separate browser profiles."}},

  {"n":2,"from":"BS","to":"A","hop":1,"status":"verified",
   "label":"authenticated channel Activity",
   "detail":{"carries":"The Teams activity, with the channel authenticated by Bot Service.",
             "mechanism":"Azure Bot Service validates the channel and passes the activity to the bot.",
             "enforced":"Bot Service, then the bot.",
             "why":"The bot now knows which person is asking, but does not yet hold a token it can spend."}},

  {"n":3,"from":"A","to":"BS","hop":2,"status":"verified",
   "label":"retrieve the person's token \u00b7 SDK authorization handler",
   "detail":{"carries":"A request for the person's token for the bot's own API.",
             "mechanism":"The Microsoft 365 Agents SDK authorization handler asks Bot Service's token store.",
             "enforced":"Bot Service.",
             "why":"Bot Service brokers and stores this token. It does not issue the downstream ones \u2014 Entra does, at step 5."}},

  {"n":4,"from":"BS","to":"A","hop":2,"status":"verified","return":True,
   "label":"exchangeable token for api://botid-{appid}",
   "detail":{"carries":"The person's access token, audience = the bot's own API.",
             "mechanism":"Teams SSO produced this without prompting the person.",
             "enforced":"Entra issued it; Bot Service stored it.",
             "why":"Exchangeable is the important word: this token is the OBO assertion for step 5."}},

  {"n":5,"from":"A","to":"E","hop":3,"status":"verified",
   "label":"MSAL OBO \u00d73 \u00b7 the person's assertion + the bot's federated credential",
   "detail":{"carries":"The person's token as an assertion, plus the bot's client credential. One exchange per audience: Graph, Azure AI Search, Zava API.",
             "mechanism":"MSAL on-behalf-of, as a confidential client. The credential is a federated identity credential on the bot's app registration that trusts the bot's managed identity, so there is no secret to store or rotate.",
             "enforced":"Entra: the admin consent we granted once, and any Conditional Access policy.",
             "why":"Learn's requirement for the whole pattern is exactly this: a trusted application backend that authenticates users and can perform OBO as a confidential client."}},

  {"n":6,"from":"E","to":"A","hop":3,"status":"verified","return":True,
   "label":"Graph, Search and Zava API tokens \u2014 subject is the person",
   "detail":{"carries":"One access token per downstream audience, subject = the person, actor = the bot.",
             "mechanism":"Entra issues each one. There is no second token service anywhere in this design.",
             "enforced":"Entra.",
             "why":"The person is resolved once, here. Nothing after this point re-derives who is asking."}},

  {"n":7,"from":"A","to":"W","hop":4,"status":"verified",
   "label":"run the briefing workflow \u00b7 person + the tokens in scope in hand",
   "detail":{"carries":"The resolved person and the three tokens, as one value passed in.",
             "mechanism":"The bot sends a query through its mediator; the handler starts a Microsoft Agent Framework workflow: start \u2192 the executors in scope \u2192 fan-in barrier \u2192 compose.",
             "enforced":"The bot. A fresh workflow is built for every turn, so nothing can leak between people.",
             "why":"The workflow never calls Entra. It receives the tokens; it does not mint them."}},

  {"n":8,"from":"W","to":"F","hop":5,"status":"verified",
   "label":"Compass's own ai.azure.com token + x-ms-user-identity + x-client-* per system in scope, in parallel",
   "detail":{"carries":"Three distinct things: the bot's own workload token authorizes the call; x-ms-user-identity carries the person's object id (an id, not a token); each x-client-* header carries one of the person's downstream tokens, with no Bearer prefix.",
             "mechanism":"The Foundry SDK v2 Responses client, from a single gateway class that is the only code allowed to set x-ms-user-identity.",
             "enforced":"Foundry RBAC. The bot's managed identity needs the Foundry role plus a custom role holding the UserIdentityImpersonation data action.",
             "why":"That impersonation action is in no built-in role. Without it this call is refused with 403 \u2014 the most common way this pattern fails the first time."}},

  {"n":9,"from":"F","to":"H","hop":6,"status":"verified",
   "label":"x-client-* forwarded to both hosted agents \u00b7 Authorization dropped \u00b7 x-agent-user-id added",
   "detail":{"carries":"The person's downstream tokens, untouched.",
             "mechanism":"The platform forwards the x-client-* headers to the container and drops the caller's Authorization header.",
             "enforced":"Foundry.",
             "why":"Learn is explicit that Foundry doesn't exchange or refresh the token in x-client-*. This is application-managed OBO plus token forwarding, not a token exchange by Foundry. It also means Foundry will not refresh an expiring token \u2014 obtain them per turn."}},

  {"n":10,"from":"H","to":"R","hop":7,"status":"verified",
   "label":"route A \u00b7 the person's delegated Graph token \u2192 Copilot Retrieval API",
   "detail":{"carries":"The person's Graph token, read from x-client-graph-access-token by SharePointRetrievalAgent.",
             "mechanism":"The agent's code calls the Microsoft 365 Copilot Retrieval API as the person.",
             "enforced":"SharePoint itself, at query time.",
             "why":"Needs a Microsoft 365 Copilot licence per calling person. Semantic retrieval covers .docx, .pptx, .pdf, .aspx and .one; other types get lexical matching only."}},

  {"n":11,"from":"R","to":"H","hop":7,"status":"verified","return":True,
   "label":"only what the person may see",
   "detail":{"carries":"Text extracts with their SharePoint source URLs.",
             "mechanism":"The Retrieval API queried as the calling person.",
             "enforced":"SharePoint's own permissions.",
             "why":"Daniel's extracts come from Lending Policy and the FAQ. Maya's come from Portfolio and Collections and the FAQ. Nothing is pruned afterwards in our code, because nothing came back that the person could not already open."}},

  {"n":12,"from":"H","to":"M","hop":8,"status":"verified",
   "label":"ZavaApiAgent \u00b7 x-client-zava-api-token \u00b7 audience = Zava API",
   "detail":{"carries":"The person's token for Zava Credit Union's own API audience.",
             "mechanism":"The agent's code calls the MCP server directly, passing the token explicitly.",
             "enforced":"The MCP server validates audience and issuer before it does anything.",
             "why":"This is why the pattern fixes MCP too. Keep calling MCP servers from code so headers can be passed explicitly."}},

  {"n":13,"from":"M","to":"H","hop":8,"status":"verified","return":True,
   "label":"member record, scoped to the person",
   "detail":{"carries":"The member record for that person, found by Entra object id.",
             "mechanism":"The server resolves the person to a member and reads a simulated core-banking domain.",
             "enforced":"The MCP server. It never forwards the token it received to a different API.",
             "why":"The identity path is real; the banking data is simulated. That no-forwarding rule is the MCP specification's, not ours."}},

  {"n":14,"from":"F","to":"K","hop":9,"status":"verified",
   "label":"route B \u00b7 Search token as a structured input (search_auth_token)",
   "detail":{"carries":"The person's Azure AI Search token, as a field in the request input.",
             "mechanism":"A prompt agent runs inside the Foundry service, so there is no container and no header forwarding. The bot passes the token as structured input instead.",
             "enforced":"Foundry, then the knowledge base.",
             "why":"The two routes differ in plumbing, not principle. The prompt agent was created with the Foundry SDK's agent administration client and wired to the knowledge base's MCP endpoint."}},

  {"n":15,"from":"K","to":"S","hop":10,"status":"verified",
   "label":"x-ms-query-source-authorization = the person's Search token",
   "detail":{"carries":"The person's Search token, as x-ms-query-source-authorization on the knowledge-base call.",
             "mechanism":"The agent's knowledge-base tool call.",
             "enforced":"Azure AI Search.",
             "why":"This header is what makes the knowledge base query as the person instead of as the service."}},

  {"n":16,"from":"S","to":"R","hop":11,"status":"verified",
   "label":"remote SharePoint source \u00b7 queried live as the person",
   "detail":{"carries":"A query made on the person's behalf.",
             "mechanism":"The knowledge base's remote SharePoint knowledge source calls SharePoint with the person's token.",
             "enforced":"SharePoint's own permissions.",
             "why":"No copy of the content and no ACL snapshot, so a revoked permission takes effect immediately. An indexed source would be faster at scale but only as fresh as its last sync."}},

  {"n":17,"from":"R","to":"S","hop":11,"status":"verified","return":True,
   "label":"only what the person may see",
   "detail":{"carries":"Matching passages.",
             "mechanism":"SharePoint's response to the live query.",
             "enforced":"SharePoint.",
             "why":"Same guarantee as route A: the answer can contain only content the person could already open."}},

  {"n":18,"from":"S","to":"K","hop":9,"status":"verified","return":True,
   "label":"grounded passages \u2192 answer with citations",
   "detail":{"carries":"Passages with their sources; the prompt agent composes a cited answer.",
             "mechanism":"The model reads the knowledge base result and answers.",
             "enforced":"Foundry content filtering and the agent's instructions.",
             "why":"The model decides when to call the knowledge base. That is the trade for a prompt agent: less code, less determinism."}},

  {"n":19,"from":"F","to":"W","hop":12,"status":"verified","return":True,
   "label":"the results in scope arrive at the fan-in barrier",
   "detail":{"carries":"One result per branch, or an error.",
             "mechanism":"Each executor completes independently; the barrier waits for every branch in scope.",
             "enforced":"The workflow.",
             "why":"The branches in scope ran in parallel. A branch that fails is not swallowed."}},

  {"n":20,"from":"W","to":"A","hop":12,"status":"verified","return":True,
   "label":"one briefing \u00b7 any failed branch fails the whole turn",
   "detail":{"carries":"A composed briefing, or the original typed exception.",
             "mechanism":"Compose runs only if every branch in scope succeeded; otherwise the workflow rethrows the original exception.",
             "enforced":"The workflow. Fail closed: no partial answer, no silent downgrade to app-only access.",
             "why":"A permissions-sensitive answer that is sometimes missing a source is worse than one that says it cannot answer."}},

  {"n":21,"from":"A","to":"P","hop":12,"status":"verified","return":True,
   "label":"reply in Teams",
   "detail":{"carries":"The briefing, formatted for Teams.",
             "mechanism":"The bot sends the reply on the same conversation.",
             "enforced":"The bot.",
             "why":"Two people asked the same question; two different, correct answers came back, and the bot never needed to know why."}},
 ],
 "notes": [
  {"after":8,"text":"Without the UserIdentityImpersonation role: 403, right here."},
  {"after":9,"text":"Foundry does not refresh x-client-* tokens. Keep the turn inside their lifetime."},
  {"after":11,"text":"No pruning in our code \u2014 SharePoint already decided."},
  {"after":14,"text":"A prompt agent has no headers to forward, so the token rides as input."},
  {"after":18,"text":"The branches in scope ran in parallel. One failure fails the turn."},
 ],
 "ruled": [
  ["Publish to Teams from Foundry",
   "Not established for a no-new-prompt requirement, and published agents support neither streaming nor citations \u2014 both of which Compass uses."],
  ["The Foundry SharePoint tool",
   "Learn is explicit: it doesn't work when the agent is published to Microsoft Teams."],
  ["A toolbox oauth2 connection",
   "Runs Foundry's own per-person authorization when no grant exists. No documented way to hand it the token Compass already holds."],
  ["An indexed SharePoint source",
   "Permissions are as of the last sync, so access is only as fresh as the ingestion. Right at scale; wrong when the answer must reflect who may see what right now."],
 ],
}

HTML = r"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>__TITLE__ · sequence</title>
<script>
  (() => {
    const q = new URLSearchParams(location.search);
    const t = q.get("scoutTheme") ||
      (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
    document.documentElement.setAttribute("data-theme", t);
  })();
</script>
<style>
:root{
  color-scheme:light;
  --cp-bg:#f7f4ef; --cp-bg-elevated:#fcfbf8; --cp-surface:#ffffff; --cp-surface-soft:#f5f5f5;
  --cp-border:#dedede; --cp-border-strong:#919191; --cp-text:#242424; --cp-text-muted:#5c5c5c;
  --cp-text-soft:#6f6f6f; --cp-accent:#b11f4b; --cp-accent-soft:rgba(177,31,75,.08);
  --cp-accent-fg:#fff; --cp-link:#0078d4; --cp-shadow:0 18px 48px rgba(0,0,0,.12);
  --lane-person:#0f6cbd; --lane-azure:#0078d4; --lane-entra:#8661c5; --lane-foundry:#b11f4b;
  --lane-m365:#0f7b0f; --lane-core:#1a1a1a;
  --wire:#0f6cbd; --wire-gated:#8a6d00; --note:#fff8e5; --note-bd:#c77700;
}
html[data-theme="dark"]{
  color-scheme:dark;
  --cp-bg:#3d3b3a; --cp-bg-elevated:#343231; --cp-surface:#292929; --cp-surface-soft:#2e2e2e;
  --cp-border:#474747; --cp-border-strong:#5f5f5f; --cp-text:#dedede; --cp-text-muted:#919191;
  --cp-text-soft:#b0b0b0; --cp-accent:#fd8ea1; --cp-accent-soft:rgba(253,142,161,.14);
  --cp-accent-fg:#1a1a1a; --cp-link:#4da6ff; --cp-shadow:0 18px 48px rgba(0,0,0,.32);
  --lane-person:#4da6ff; --lane-azure:#4da6ff; --lane-entra:#b69cf0; --lane-foundry:#fd8ea1;
  --lane-m365:#5ec75e; --lane-core:#dedede;
  --wire:#4da6ff; --wire-gated:#e0b400; --note:#3a3420; --note-bd:#c77700;
}
*{box-sizing:border-box}
html,body{margin:0;height:100%}
body{background:var(--cp-bg);color:var(--cp-text);
  font-family:"Segoe UI",Aptos,Calibri,-apple-system,sans-serif;font-size:14px;
  display:flex;flex-direction:column;overflow:hidden}
code,.mono{font-family:Consolas,"Courier New",monospace}
button{font:inherit;color:inherit}
header{display:flex;flex-wrap:wrap;align-items:center;gap:8px 16px;padding:10px 16px;
  background:var(--cp-bg-elevated);border-bottom:1px solid var(--cp-border)}
header h1{font-size:16px;font-weight:600;margin:0 8px 0 0}
header h1 small{font-weight:400;color:var(--cp-text-muted);margin-left:8px}
.seg{border:1px solid var(--cp-border);background:var(--cp-surface);border-radius:.625rem;
  padding:4px 10px;cursor:pointer;font-size:13px}
.seg:hover{border-color:var(--cp-border-strong)}
.seg.on{background:var(--cp-accent);color:var(--cp-accent-fg);border-color:var(--cp-accent)}
main{flex:1;display:flex;min-height:0}
#viewport{flex:1;overflow:auto;padding:14px}
aside{width:390px;border-left:1px solid var(--cp-border);background:var(--cp-bg-elevated);
  overflow:auto;padding:16px 18px 28px}
aside h2{font-size:17px;margin:0 0 6px}
aside h3{font-size:11px;letter-spacing:.06em;text-transform:uppercase;color:var(--cp-text-muted);
  margin:16px 0 4px}
aside p{margin:0 0 8px;line-height:1.5}
.sub{color:var(--cp-text-muted)}
.pill{display:inline-block;border-radius:999px;padding:2px 9px;font-size:11px;font-weight:600;
  border:1px solid var(--cp-border);background:var(--cp-surface)}
.pill.gated{border-color:var(--wire-gated);color:var(--wire-gated)}
.pill.built,.pill.verified{border-color:var(--wire);color:var(--wire)}
.steplist{list-style:none;margin:0;padding:0}
.steplist button{display:block;width:100%;text-align:left;background:none;border:0;
  padding:6px 8px;border-radius:8px;cursor:pointer;line-height:1.35;font-size:13px}
.steplist button:hover{background:var(--cp-accent-soft)}
.steplist button.on{background:var(--cp-accent-soft);font-weight:600}
.num{display:inline-flex;align-items:center;justify-content:center;width:19px;height:19px;
  border-radius:50%;background:var(--wire);color:#fff;font-size:11px;font-weight:700;margin-right:7px}
.num.gated{background:var(--wire-gated)}
svg{display:block}
.lane-line{stroke:var(--cp-border);stroke-width:1.5;stroke-dasharray:3 4}
.lane-box{fill:var(--cp-surface);stroke:var(--cp-border);stroke-width:1}
.lane-accent{stroke-width:3.5}
.lane-t{font-size:13px;font-weight:600;fill:var(--cp-text)}
.lane-s{font-size:11px;fill:var(--cp-text-muted)}
.wire{stroke:var(--wire);stroke-width:2;fill:none;marker-end:url(#ar)}
.wire.gated{stroke:var(--wire-gated);stroke-dasharray:6 4;marker-end:url(#arg)}
.wire.ret{stroke-dasharray:5 4;opacity:.78;marker-end:url(#arr)}
.wire.ret.gated{marker-end:url(#arrg)}
.msg{font-size:12px;fill:var(--cp-text)}
.msg-hit{fill:transparent;cursor:pointer}
g.step:hover .wire{stroke-width:3.5}
g.step:hover .msg{font-weight:600}
g.step.dim{opacity:.22}
g.step.sel .wire{stroke-width:4}
g.step.sel .msg{font-weight:700}
.sn{font-size:11px;font-weight:700;fill:#fff}
.sn-bg{fill:var(--wire)}
.sn-bg.gated{fill:var(--wire-gated)}
.note-bg{fill:var(--note);stroke:var(--note-bd);stroke-width:1;rx:6}
.note-t{font-size:11.5px;fill:var(--cp-text)}
footer{border-top:1px solid var(--cp-border);background:var(--cp-bg-elevated);
  padding:10px 16px;display:flex;gap:28px;flex-wrap:wrap;font-size:12px}
footer b{font-size:11px;letter-spacing:.06em;text-transform:uppercase;color:var(--cp-text-muted);
  display:block;margin-bottom:4px}
.key{display:flex;align-items:center;gap:7px;margin:2px 0}
.kl{width:22px;height:0;border-top:2.5px solid var(--wire);display:inline-block}
.kl.gated{border-top-style:dashed;border-top-color:var(--wire-gated)}
.kl.ret{border-top-style:dashed;opacity:.8}
html[data-chrome="0"] header,html[data-chrome="0"] aside{display:none}
html[data-chrome="0"] body{overflow:visible}
html[data-chrome="0"] #viewport{overflow:visible}
</style>
</head>
<body>
<header>
  <h1>__TITLE__<small>__SUBTITLE__</small></h1>
  <div style="margin-left:auto;display:flex;gap:6px">
    <button class="seg" id="prev">&#8592; Prev</button>
    <button class="seg" id="next">Next &#8594;</button>
    <button class="seg" id="clear">Show all</button>
    <button class="seg" id="theme">Theme</button>
  </div>
</header>
<main>
  <div id="viewport"><svg id="svg"></svg></div>
  <aside id="side"></aside>
</main>
<footer>
  <div>
    <b>What the arrows mean</b>
    <div class="key"><span class="kl"></span> Carries the person's identity &#183; proved live in the sandbox</div>
    <div class="key"><span class="kl gated"></span> Carries the person's identity &#183; built, waiting on its live proof</div>
    <div class="key"><span class="kl ret"></span> A response coming back</div>
  </div>
  <div>
    <b>The one idea</b>
    <div style="max-width:560px;line-height:1.5">__SUMMARY__</div>
  </div>
  <div>
    <b>As of __ASOF__</b>
    <div style="max-width:300px;line-height:1.5;color:var(--cp-text-muted)">
      Click any arrow for what is on the wire, who checks it, and why it matters.
    </div>
  </div>
</footer>
<script>
const M = __MODEL__;
const q = new URLSearchParams(location.search);
if (q.get("chrome") === "0") document.documentElement.setAttribute("data-chrome","0");

const LANE_W = 178, LEFT = 34, TOP = 30, HEAD_H = 62;
const FIRST = TOP + HEAD_H + 54, GAP = 64, NOTE_H = 30;

const laneX = {}, kindVar = {person:"--lane-person",azure:"--lane-azure",entra:"--lane-entra",
  foundry:"--lane-foundry",m365:"--lane-m365",core:"--lane-core"};
M.lanes.forEach((l,i) => laneX[l.id] = LEFT + i*LANE_W + LANE_W/2);

// y position per step, leaving room for notes
let y = FIRST; const stepY = {}; const noteAfter = {};
M.notes.forEach(n => noteAfter[n.after] = n.text);
M.steps.forEach(s => { stepY[s.n] = y; y += GAP + (noteAfter[s.n] ? NOTE_H : 0); });
const W = LEFT*2 + M.lanes.length*LANE_W, H = y + 28;

const svg = document.getElementById("svg");
svg.setAttribute("viewBox", `0 0 ${W} ${H}`);
svg.setAttribute("width", W); svg.setAttribute("height", H);

const NS = "http://www.w3.org/2000/svg";
const el = (n,a={}) => { const e = document.createElementNS(NS,n);
  for (const k in a) e.setAttribute(k,a[k]); return e; };

function marker(id, cls){
  const m = el("marker",{id, viewBox:"0 0 10 10", refX:"9", refY:"5",
    markerWidth:"7", markerHeight:"7", orient:"auto-start-reverse"});
  const p = el("path",{d:"M 0 0 L 10 5 L 0 10 z"});
  p.setAttribute("fill", cls === "g" ? "var(--wire-gated)" : "var(--wire)");
  if (cls === "r" || cls === "rg") p.setAttribute("opacity",".8");
  m.appendChild(p); return m;
}
const defs = el("defs");
defs.append(marker("ar",""), marker("arg","g"), marker("arr","r"), marker("arrg","rg"));
svg.appendChild(defs);

// lifelines
M.lanes.forEach(l => {
  const x = laneX[l.id];
  svg.appendChild(el("line",{x1:x,y1:TOP+HEAD_H,x2:x,y2:H-16,class:"lane-line"}));
});
// lane heads
M.lanes.forEach(l => {
  const x = laneX[l.id], bw = LANE_W-22;
  const g = el("g");
  g.appendChild(el("rect",{x:x-bw/2,y:TOP,width:bw,height:HEAD_H,rx:8,class:"lane-box"}));
  const ac = el("line",{x1:x-bw/2,y1:TOP+3,x2:x-bw/2,y2:TOP+HEAD_H-3,class:"lane-accent"});
  ac.setAttribute("stroke",`var(${kindVar[l.kind]})`);
  g.appendChild(ac);
  const t = el("text",{x:x,y:TOP+26,"text-anchor":"middle",class:"lane-t"}); t.textContent = l.title;
  const s = el("text",{x:x,y:TOP+44,"text-anchor":"middle",class:"lane-s"}); s.textContent = l.sub;
  g.append(t,s); svg.appendChild(g);
});

// steps
const groups = {};
M.steps.forEach(s => {
  const x1 = laneX[s.from], x2 = laneX[s.to], yy = stepY[s.n];
  const dir = x2 > x1 ? 1 : -1, pad = 7;
  const g = el("g",{class:"step"}); g.dataset.n = s.n;
  const cls = "wire" + (s.status === "gated" ? " gated" : "") + (s.return ? " ret" : "");
  g.appendChild(el("line",{x1:x1+pad*dir,y1:yy,x2:x2-pad*dir,y2:yy,class:cls}));

  const mx = (x1+x2)/2;
  const t = el("text",{x:mx,y:yy-9,"text-anchor":"middle",class:"msg"});
  t.textContent = s.label; g.appendChild(t);

  const bx = x1 + 16*dir;
  const c = el("circle",{cx:bx,cy:yy,r:9.5,class:"sn-bg"+(s.status==="gated"?" gated":"")});
  const n = el("text",{x:bx,y:yy+4,"text-anchor":"middle",class:"sn"}); n.textContent = s.n;
  g.append(c,n);

  g.appendChild(el("rect",{x:Math.min(x1,x2),y:yy-24,width:Math.abs(x2-x1),height:34,class:"msg-hit"}));
  g.addEventListener("click", () => select(s.n));
  svg.appendChild(g); groups[s.n] = g;

  if (noteAfter[s.n]) {
    const ny = yy + 26, nw = 520, nx = Math.max(LEFT+8, mx - nw/2);
    const ng = el("g");
    ng.appendChild(el("rect",{x:nx,y:ny,width:nw,height:24,class:"note-bg"}));
    const nt = el("text",{x:nx+12,y:ny+16,class:"note-t"}); nt.textContent = noteAfter[s.n];
    ng.append(nt); svg.appendChild(ng);
  }
});

const side = document.getElementById("side");
let sel = null;

function overview(){
  const rows = M.steps.map(s =>
    `<li><button data-n="${s.n}" class="${sel===s.n?'on':''}">
       <span class="num ${s.status==='gated'?'gated':''}">${s.n}</span>${esc(s.label)}</button></li>`).join("");
  const ruled = M.ruled.map(([t,d]) =>
    `<p><b>${esc(t)}</b><br><span class="sub">${esc(d)}</span></p>`).join("");
  side.innerHTML =
    `<h2>The flow, in order</h2>
     <p class="sub">${esc(M.summary)}</p>
     <h3>Steps</h3><ul class="steplist">${rows}</ul>
     <h3>Ruled out, and why</h3>${ruled}`;
  side.querySelectorAll("button[data-n]").forEach(b =>
    b.addEventListener("click", () => select(+b.dataset.n)));
}
function esc(s){ return String(s).replace(/[&<>]/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;"}[c])); }

function select(n){
  sel = (sel === n) ? null : n;
  Object.values(groups).forEach(g => g.classList.remove("dim","sel"));
  if (sel == null) { overview(); return; }
  Object.entries(groups).forEach(([k,g]) => {
    if (+k === sel) g.classList.add("sel"); else g.classList.add("dim");
  });
  const s = M.steps.find(x => x.n === sel), d = s.detail || {};
  const from = M.lanes.find(l => l.id === s.from), to = M.lanes.find(l => l.id === s.to);
  const part = (h,v) => v ? `<h3>${h}</h3><p>${esc(v)}</p>` : "";
  side.innerHTML =
    `<h2><span class="num ${s.status==='gated'?'gated':''}">${s.n}</span>${esc(from.title)} &#8594; ${esc(to.title)}</h2>
     <p><span class="pill ${s.status}">${s.status === "gated" ? "Built &#183; waiting on its live proof" : (s.status === "verified" ? "Proved live in the sandbox" : "Built and tested")}</span></p>
     <p style="margin-top:10px"><b>${esc(s.label)}</b></p>
     ${part("On the wire", d.carries)}
     ${part("How", d.mechanism)}
     ${part("Who checks it", d.enforced)}
     ${part("Why it matters", d.why)}
     <p style="margin-top:18px"><button class="seg" id="back">&#8592; All steps</button></p>`;
  const b = document.getElementById("back");
  if (b) b.addEventListener("click", () => { sel = null;
    Object.values(groups).forEach(g => g.classList.remove("dim","sel")); overview(); });
}

document.getElementById("next").addEventListener("click", () => {
  const i = sel == null ? 0 : M.steps.findIndex(s => s.n === sel) + 1;
  if (i < M.steps.length) select(M.steps[i].n);
});
document.getElementById("prev").addEventListener("click", () => {
  const i = sel == null ? M.steps.length-1 : M.steps.findIndex(s => s.n === sel) - 1;
  if (i >= 0) select(M.steps[i].n);
});
document.getElementById("clear").addEventListener("click", () => {
  sel = null; Object.values(groups).forEach(g => g.classList.remove("dim","sel")); overview();
});
document.getElementById("theme").addEventListener("click", () => {
  const h = document.documentElement;
  h.setAttribute("data-theme", h.getAttribute("data-theme") === "dark" ? "light" : "dark");
});
document.addEventListener("keydown", e => {
  if (e.key === "ArrowRight") document.getElementById("next").click();
  if (e.key === "ArrowLeft") document.getElementById("prev").click();
  if (e.key === "Escape") document.getElementById("clear").click();
});

const qs = q.get("step");
if (qs) select(+qs); else overview();
</script>
</body>
</html>"""

html = (HTML
        .replace("__MODEL__", json.dumps(MODEL, ensure_ascii=False))
        .replace("__TITLE__", MODEL["title"])
        .replace("__SUBTITLE__", MODEL["subtitle"])
        .replace("__SUMMARY__", MODEL["summary"])
        .replace("__ASOF__", MODEL["asOf"]))
OUT.write_text(html, encoding="utf-8")
print("wrote", OUT.name, len(html)//1024, "KB")
print("lanes", len(MODEL["lanes"]), "steps", len(MODEL["steps"]), "notes", len(MODEL["notes"]))
