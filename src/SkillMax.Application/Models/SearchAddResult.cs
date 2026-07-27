using SkillMax.Domain.Entities;

namespace SkillMax.Application.Models;

public enum SearchAddOutcome
{
    Added,

    /// <summary>Blank, or too short to search for anything meaningful.</summary>
    TooShort,

    /// <summary>The same search, ignoring case and spacing.</summary>
    AlreadyFollowed,

    /// <summary>YouTube ran the search and there were no shorts in it.</summary>
    NoShorts,

    /// <summary>Couldn't reach YouTube to check.</summary>
    Unreachable,
}

/// <param name="Found">How many shorts the search turned up, when it worked.</param>
public sealed record SearchAddResult(SearchAddOutcome Outcome, SearchTerm? Term = null, int Found = 0)
{
    public bool Ok => Outcome is SearchAddOutcome.Added;

    public string? Message => Outcome switch
    {
        SearchAddOutcome.Added => $"Following “{Term?.Query}” — {Found} short{(Found == 1 ? "" : "s")} to start with.",
        SearchAddOutcome.AlreadyFollowed => $"You already follow “{Term?.Query}”.",
        SearchAddOutcome.TooShort => "Give the search a couple more characters.",
        SearchAddOutcome.NoShorts => "YouTube found no shorts for that search.",
        _ => "Couldn't reach YouTube. Check your connection and try again.",
    };
}
