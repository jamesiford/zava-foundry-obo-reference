using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Models;
using Compass.Bot.Domain;
using Compass.Shared.Messaging;

namespace Compass.Bot.Application.Queries;

/// <summary>
/// One Teams turn: the person's SSO token and what they asked. Intentionally has no person, member or session
/// parameters — those are derived inside the handler from the token (contract rules 1 and 8).
/// </summary>
/// <param name="SsoToken">The person's token for the bot's own API (Teams SSO or the web front door).</param>
/// <param name="Question">What they asked.</param>
/// <param name="Progress">Optional observer of the turn's progress; <see langword="null"/> is the no-op sink.</param>
public sealed record GetMemberBriefingQuery(AccessToken SsoToken, string Question, IBriefingProgressSink? Progress = null) : IRequest<MemberBriefing>;
