namespace Compass.Mcp.ZavaApi.Host.Authentication;

/// <summary>
/// The inbound request failed token validation. Distinct from a generic exception so the failure is unmistakably an
/// auth rejection in logs and traces.
/// </summary>
/// <remarks>The message carries the rejection reason only; it must never contain token contents.</remarks>
public sealed class ZavaApiUnauthorizedException(string reason) : Exception(reason);
