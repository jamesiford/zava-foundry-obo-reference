# Which way to SharePoint, and why

How the two built SharePoint retrieval routes compare with the alternatives, and when you would choose each. `SharePointRetrievalAgent` (the Copilot Retrieval API, the default recommendation) and `SharePointKnowledgeAgent` (a Foundry IQ knowledge base, a prompt agent) are built and run side by side. The route is a deployment setting (`SHAREPOINT_ROUTE`: `KnowledgeBase`, `Retrieval` or `Both`).

*(Reproduced from dev spec §7.)* Two SharePoint retrieval paths are built and run side by side: `SharePointRetrievalAgent` (brief §5.1, the recommended
default) and `SharePointKnowledgeAgent` (brief §5.2, M9a); the others are documented here so the choice is reviewable.

| Option (brief §) | How it enforces permissions | Why **not** chosen here | When you *would* choose it |
| --- | --- | --- | --- |
| **5.1 Copilot Retrieval API (built)** | SharePoint's own permissions, live, via Microsoft 365's index — no pruning, no re-sync lag | — | Default whenever the callers hold (or the tenant has) Microsoft 365 Copilot licensing and GA/production posture is wanted over preview |
| **5.2 Foundry IQ knowledge base, remote SharePoint source (built, M9a)** | Same live SharePoint permission check, routed via Azure AI Search with the person's **Search-scope** token | — built so the two routes can be compared; stays preview and costs an extra hop plus a Search service | **One retrieval call across several knowledge sources**, and preview status is acceptable |
| 5.3 Foundry IQ knowledge base, indexed SharePoint source | Azure AI Search checks **ingested ACL snapshots**, not SharePoint live — changes apply after the next sync | The wrong shape for a demo about live permission exclusion (a revoked user still sees content until re-sync); preview; needs a Search index + indexer | Content the Retrieval API can't reach, where some permission staleness is accepted |
| 5.4 Azure AI Search index + ACLs/filters | Search checks ingested ACLs (preview, same staleness), or **your own code** builds a group filter (GA, but correctness is yours) | No existing SharePoint-backed index to migrate off post-retrieval pruning | You already have such an index and pruning today — the realistic **interim step** |
| 5.5 Foundry SharePoint tool | Needs the person's own Foundry identity; every caller needs a Foundry User license | Ruled out by the brief for Teams-published agents | Only direct-to-Foundry clients outside Teams |
| 5.6 Knowledge base via MCP | Same knowledge-base permission model as 5.2/5.3, exposed over MCP | No documented per-request token injection for hosted agents; no benefit here | A **prompt agent** that binds a knowledge base declaratively via `structured_inputs` |

A phase-2 extension could add 5.3/5.4 side by side (dev spec §7); it is intentionally not part of this build.
