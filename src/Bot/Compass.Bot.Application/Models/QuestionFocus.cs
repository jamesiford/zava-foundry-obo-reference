using System.Text.RegularExpressions;

namespace Compass.Bot.Application.Models;

/// <summary>What a question is after: the person's own accounts, shared policy documents, or both.</summary>
public enum QuestionFocus
{
    /// <summary>Their own accounts and balances; documents would only be noise.</summary>
    Accounts,

    /// <summary>Policy, procedure or other shared documents; the person's accounts would only be noise.</summary>
    Policy,

    /// <summary>Both, or unclear: show everything that has content.</summary>
    Both,
}

/// <summary>
/// Decides, from the wording alone, which parts of a briefing a question is about. Deterministic on purpose: it only
/// chooses which already-fetched sections to show, so it can never widen what a person is allowed to see.
/// </summary>
public static partial class QuestionFocusClassifier
{
    public static QuestionFocus Classify(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return QuestionFocus.Both;
        }

        var accounts = OwnAccounts().IsMatch(question);
        var policy = SharedDocuments().IsMatch(question);

        return (accounts, policy) switch
        {
            (true, false) => QuestionFocus.Accounts,
            (false, true) => QuestionFocus.Policy,
            _ => QuestionFocus.Both,
        };
    }

    // "my accounts", "my balance", "what do I owe", or a bare "balances": the person's own data.
    [GeneratedRegex(@"\b(my|mine)\b[^?.!]*\b(accounts?|balances?|loans?|savings|checking|shares?|cards?|statements?|transactions?)\b|\bbalances?\b|\bhow much (do|did|have) i\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OwnAccounts();

    // Words that point at documents everyone shares rather than at one person's money.
    [GeneratedRegex(@"\b(polic(y|ies)|limits?|ratios?|maximum|minimum|cut-?offs?|fees?|rates?|rules?|procedures?|playbook|authority|approv\w*|exceptions?|watchlist|delinquen\w*|hardship|branch(es)?|hours|wires?|atm|certificates?|guidelines?|requirements?|eligib\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SharedDocuments();
}
