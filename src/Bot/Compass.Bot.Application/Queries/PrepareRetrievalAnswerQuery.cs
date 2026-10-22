using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;
using Compass.Shared.Messaging;

namespace Compass.Bot.Application.Queries;

/// <summary>
/// Gates this turn's retrieval hits for relevance and, if any remain, asks the answer agent to write the answer from them.
/// Carries the question and the hits only: no person, session or token, so the answer step cannot assert an identity.
/// </summary>
public sealed record PrepareRetrievalAnswerQuery(string Question, RetrievalResult Documents) : IRequest<RetrievalAnswer>;

/// <summary>
/// What route A shows for one turn.
/// </summary>
/// <param name="Documents">The hits that survived the relevance gate, in the order the API returned them.</param>
/// <param name="Summary">The answer agent's reply; <see langword="null"/> when summarising was off, nothing survived, or it failed.</param>
/// <param name="SummaryUnavailable">The answer agent was needed but errored or timed out, so the extracts are shown instead.</param>
public sealed record RetrievalAnswer(RetrievalResult Documents, KnowledgeQueryResult? Summary, bool SummaryUnavailable);
