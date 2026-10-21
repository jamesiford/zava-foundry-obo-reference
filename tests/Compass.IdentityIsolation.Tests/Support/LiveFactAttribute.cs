namespace Compass.IdentityIsolation.Tests.Support;

/// <summary>
/// A fact that runs only when a real test tenant has been set up (<c>COMPASS_TEST_TENANT_CONFIGURED=1</c>) and is
/// reported as skipped — never as passed — otherwise. xunit v2 has no runtime skip, so the skip reason is set at
/// discovery time.
/// </summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public const string GateVariable = "COMPASS_TEST_TENANT_CONFIGURED";

    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(GateVariable) is not ("1" or "true"))
        {
            Skip = $"Live test: needs a configured test tenant. Set {GateVariable}=1 plus the COMPASS_TEST_* variables (see docs/OBSERVABILITY.md and infra/README.md).";
        }
    }
}
