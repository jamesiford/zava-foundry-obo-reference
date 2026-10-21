namespace Compass.IdentityIsolation.Tests.Support;

/// <summary>
/// One of the two test people. The canary is a unique string that exists only in a document this person may read, so
/// finding it in anyone else's answer proves a cross-person leak without needing to compare whole documents.
/// </summary>
internal sealed record TestPerson(
    string Key,
    string ObjectId,
    string TenantId,
    string DisplayName,
    string MemberNumber,
    string Canary)
{
    public const string CanaryA = "ZAVA-CANARY-LENDING-POLICY-A";

    public const string CanaryB = "ZAVA-CANARY-COLLECTIONS-PLAYBOOK-B";

    public const string TenantId1 = "00000000-0000-0000-0000-0000000000aa";

    public static readonly TestPerson UserA = new("A", "11111111-1111-1111-1111-111111111111", TenantId1, "Daniel Park", "ZCU-100234", CanaryA);

    public static readonly TestPerson UserB = new("B", "22222222-2222-2222-2222-222222222222", TenantId1, "Maya Lin", "ZCU-100871", CanaryB);

    /// <summary>The canary that belongs to the <em>other</em> person.</summary>
    public string ForeignCanary => Key == "A" ? CanaryB : CanaryA;

    public string ForeignName => Key == "A" ? UserB.DisplayName : UserA.DisplayName;
}
