# Security policy

## This is a reference build, not a production system

This repository demonstrates an identity pattern (every downstream call runs as the signed-in person) and the engineering
around it. It is **not** a hardened production service. The documentation lists what a production deployment would add
(network isolation, an API gateway, content filters, SIEM integration, resilience): see
[`docs/DEMO-SCRIPT.md`](docs/DEMO-SCRIPT.md) Part 5 and [`docs/SERVICE-CATALOG.md`](docs/SERVICE-CATALOG.md) "Gaps". Do not
deploy it as-is for real member or customer data.

## Reporting a vulnerability

Please report suspected vulnerabilities **privately**, not in a public issue or pull request.

Use GitHub's private vulnerability reporting: open the repository's **Security** tab and choose **Report a vulnerability**.
Include what you found, where (file and line, or the step in the runbook), and how to reproduce it.

This is a maintainer-run sample, so there is no service-level agreement: reports are acknowledged and fixed on a
best-effort basis.

## What to report

- A code path that could let one person's token, data or permissions reach another person.
- A token, secret or tenant identifier committed to the repository or its history.
- A configuration in the Bicep or scripts that grants broader access than the documentation says it does.
- A dependency with a known vulnerability that this repository could reasonably avoid. (The build currently reports an
  advisory for a transitive `MessagePack` package that comes from the Aspire Azure Functions integration; it is visible
  in the build output and tracked, not suppressed.)

## Handling secrets

No secret is required to build, test or deploy this repository: credentials are federated (a managed identity is trusted
by an app registration) and the scripts print no tokens. If you find otherwise, please report it.
