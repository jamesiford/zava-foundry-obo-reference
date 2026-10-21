namespace Compass.Bot.Infrastructure.Obo;

/// <summary>Supplies the client assertion that proves the bot's identity to Entra without a stored secret.</summary>
public interface IClientAssertionProvider
{
    Task<string> GetAssertionAsync(CancellationToken cancellationToken);
}
