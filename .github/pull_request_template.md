## What changed and why

## How I checked it

- [ ] `dotnet build Compass.slnx` succeeds
- [ ] `dotnet test Compass.slnx` passes (three live isolation tests are skipped without a tenant; that is expected)
- [ ] I did not weaken the identity-isolation tests
- [ ] No tenant id, subscription id, object id, domain, user name, endpoint, token or secret is in this change
- [ ] Docs are updated, and any new claim is tagged observed, documented or open

## What I could not run

For example, anything that needs an Azure or Entra tenant, Teams, or a preview service.