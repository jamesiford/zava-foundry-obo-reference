namespace Compass.Mcp.ZavaApi.Infrastructure.Persistence;

/// <summary>Where the simulated core-banking SQLite file lives.</summary>
public sealed class ZavaApiStoreOptions
{
    public const string SectionName = "ZavaApi:Store";

    /// <summary>
    /// SQLite connection string. Defaults to a file in the temp directory so a fresh checkout runs with no setup;
    /// the database is disposable seed data, not a system of record.
    /// </summary>
    public string ConnectionString { get; set; } =
        $"Data Source={Path.Combine(Path.GetTempPath(), "zava-core-banking.db")}";
}
