using System.Text;

namespace Compass.Agents.Contracts.Tests.Support;

/// <summary>
/// Produces strings that are JWT-shaped (<c>eyJ….….…</c>) without being signed by anything, so tests can assert that
/// a specific token value never shows up in logs, exceptions or responses.
/// </summary>
internal static class FakeTokens
{
    public static string Create(string marker)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{B64("{\"alg\":\"RS256\",\"typ\":\"JWT\"}")}.{B64($"{{\"sub\":\"{marker}\",\"aud\":\"test\"}}")}.{B64("signature-" + marker)}";
    }
}
