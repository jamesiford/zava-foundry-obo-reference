namespace Compass.Bot.Infrastructure.Obo;

/// <summary>
/// The single MSAL call the bot makes, behind a seam. MSAL's request builders can't be faked, so the broker's logic
/// (scope selection, error mapping) is tested against this interface and the thin MSAL wrapper is proven live.
/// </summary>
public interface IOnBehalfOfClient
{
    /// <returns>The raw access token for <paramref name="scopes"/>, issued for the person behind <paramref name="userAssertion"/>.</returns>
    Task<string> AcquireAccessTokenAsync(IReadOnlyCollection<string> scopes, string userAssertion, CancellationToken cancellationToken);
}
