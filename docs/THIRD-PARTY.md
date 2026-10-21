# Third-party material

## Vendored in the repository

| Item | Where | Version | Licence |
| --- | --- | --- | --- |
| `@azure/msal-browser` (browser bundle) | `src/Bot/Compass.Bot.Host/wwwroot/lib/msal-browser/` | 4.30.0 | MIT, see the `LICENSE` file beside it. Provenance and the SHA256 of the bundle are in that folder's `README.md` |

The bundle is checked in rather than loaded from a CDN so the web front door's Content-Security-Policy can stay
`script-src 'self'`.

## Packages restored at build time

NuGet packages are not stored in the repository. They are restored when you build, under their own licences. To list what
this build uses:

```powershell
dotnet list Compass.slnx package --include-transitive
```

The build deliberately avoids MediatR: its current versions are commercial, so the mediator is a small class in
`src/Shared/Compass.Shared.Messaging`.

## Diagrams

The architecture diagrams under `docs/diagrams/` use Microsoft Azure, Microsoft Entra and Microsoft Fabric architecture icons,
used as Microsoft permits for architecture diagrams and documentation (the credit is also shown in
`Zava-Compass-OBO-Architecture.html`). If you reuse the diagrams, follow Microsoft's terms for those icons.
The two persona avatars (`docs/diagrams/icons/avatar-*.png`) are illustrations of the fictional personas.

## Microsoft services and documentation

This repository describes and calls Microsoft services (Microsoft Foundry, Microsoft Entra, Microsoft Teams, Azure Bot Service,
Azure Functions, Azure AI Search, Microsoft 365 Copilot Retrieval API) and cites Microsoft Learn throughout. Those services and
their documentation are governed by Microsoft's own terms. Several are in preview and can change; see the status section of the
[README](../README.md).
