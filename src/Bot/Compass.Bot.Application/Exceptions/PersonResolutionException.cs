namespace Compass.Bot.Application.Exceptions;

/// <summary>The Teams SSO token couldn't be turned into a person; the turn stops before any downstream call (fail closed).</summary>
public sealed class PersonResolutionException(string message) : Exception(message);
