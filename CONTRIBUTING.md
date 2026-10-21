# Contributing

Thanks for taking an interest. This is a **reference build** that exists to show one pattern end to end and to record, with
evidence, what worked and what did not. Contributions that keep it accurate and easy to follow are the most useful:
corrections, clearer docs, fixes for steps that no longer work, and updates when a preview service changes.

## Before you start

- For anything bigger than a small fix, open an issue first so we can agree on scope. This repository intentionally stays
  small; it is not trying to become a framework.
- Read [`docs/DECISIONS-AND-LESSONS.md`](docs/DECISIONS-AND-LESSONS.md) to see why things are shaped the way they are. A change
  that reverses a recorded decision should say why, and add evidence.
- **Never** put a tenant id, subscription id, object id, domain, user principal name, endpoint, resource-name suffix, token or
  secret in a change, a screenshot or an issue. Use `<placeholders>`.

## Build and test

You need the .NET 10 SDK or later.

```powershell
dotnet build Compass.slnx
dotnet test Compass.slnx
```

All tests must pass. Three `Live_*` isolation tests are skipped unless a real tenant is configured; that is expected.
CI (`.github/workflows/ci.yml`) runs the same two commands with no secrets.

To check the tools and modules you need for the deployment scripts, run `infra/scripts/Test-Prerequisites.ps1`.

## Conventions

- `.editorconfig` is authoritative: UTF-8, CRLF, four-space indent (two for JSON, YAML and Bicep), file-scoped namespaces
  (an error). Nullable reference types are enabled and a nullable warning is a review blocker.
- One class per agent. Dependencies point inward (Domain, Application, Infrastructure, Host).
- PowerShell scripts target PowerShell 7, support `-WhatIf` where they change anything, print no secrets, and avoid `Write-Host`.
- Tests accompany behaviour changes. The identity-isolation suite is the contract: a change must not weaken it.
- Documentation tags every claim **observed**, **documented** or **open**. Do not present an open item as working, and do not
  state a number or a default you have not checked.

## Pull requests

1. Fork, branch from `main`, make a focused change.
2. Run the build and tests locally.
3. Open a pull request using the template. Describe what changed, why, and how you checked it.
4. If you changed behaviour, say what you ran and what you could not run (for example, anything that needs a tenant).

By contributing you agree that your contribution is licensed under the repository's [MIT license](LICENSE).

## Conduct

Be kind and assume good faith. See [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md).
