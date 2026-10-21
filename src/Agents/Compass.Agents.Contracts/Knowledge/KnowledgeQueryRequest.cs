namespace Compass.Agents.Contracts.Knowledge;

/// <summary>Ask the SharePoint knowledge agent a question to be answered from the Foundry IQ knowledge base.</summary>
public sealed record KnowledgeQueryRequest(string Question);
