# Documentation index

Start with the [root README](../README.md). This index lists every document, grouped by what you want to do.

## Stand it up

| Document | Purpose |
| --- | --- |
| [`DEPLOY.md`](DEPLOY.md) | Deployment runbook: prerequisites, roles, quota, automation matrix, ten steps with verification, cleanup, and what was not exercised |
| [`IDENTITY-AND-ACCESS.md`](IDENTITY-AND-ACCESS.md) | Every principal, app registration, permission, consent grant, token and role, and who needs what to deploy |
| [`TROUBLESHOOTING.md`](TROUBLESHOOTING.md) | Symptom, cause and fix, each linked to its evidence |
| [`LOCAL-DEVELOPMENT.md`](LOCAL-DEVELOPMENT.md) | Build, test and run without Azure; run a hosted agent locally |
| [`../infra/README.md`](../infra/README.md) | The Bicep modules and scripts, and the order to run them |

## Understand it

| Document | Purpose |
| --- | --- |
| [`DEMO-SCRIPT.md`](DEMO-SCRIPT.md) | The story and the identity chain, hop by hop |
| [`SERVICE-CATALOG.md`](SERVICE-CATALOG.md) | Each service: what it does, its settings, identity, and known gaps |
| [`SHAREPOINT-ROUTES.md`](SHAREPOINT-ROUTES.md) | The SharePoint retrieval options and why the Retrieval API was chosen |
| [`OBSERVABILITY.md`](OBSERVABILITY.md) | What is instrumented and how token redaction is checked |
| [`diagrams/Zava-Compass-OBO-Architecture.html`](diagrams/Zava-Compass-OBO-Architecture.html) | C4 architecture diagrams (levels 1 to 3) |
| [`diagrams/Zava-Compass-OBO-Sequence.html`](diagrams/Zava-Compass-OBO-Sequence.html) | The sequence diagram |

## Evidence and history

| Document | Purpose |
| --- | --- |
| [`LIVE-FINDINGS.md`](LIVE-FINDINGS.md) | What the live runs proved and broke; each finding is tagged observed, documented or open |
| [`DECISIONS-AND-LESSONS.md`](DECISIONS-AND-LESSONS.md) | Why choices were made and what failed first |
| [`IMPLEMENTATION-NOTES.md`](IMPLEMENTATION-NOTES.md) | Where the build differs from the dev spec, and known limits |
| [`Zava-Compass-OBO-Teams-to-SharePoint.md`](Zava-Compass-OBO-Teams-to-SharePoint.md) | The research brief with Microsoft Learn citations |
| [`Zava-Compass-OBO-DevSpec.md`](Zava-Compass-OBO-DevSpec.md) | The original development spec (historical) |
| [`THIRD-PARTY.md`](THIRD-PARTY.md) | Third-party material and licences |

## Evidence tags

**observed**: run and seen in this build. **documented**: from Microsoft Learn or a package's documentation, not run here. **open**: not known.
