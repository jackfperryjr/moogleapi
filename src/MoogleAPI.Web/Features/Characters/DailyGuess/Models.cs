using System.Text.Json.Serialization;

namespace MoogleAPI.Web.Features.Characters.DailyGuess;

[JsonConverter(typeof(JsonStringEnumConverter<AttributeMatch>))]
public enum AttributeMatch
{
    Incorrect,
    Correct,
    /// <summary>The answer's value is higher than the guess (numeric attributes only).</summary>
    Higher,
    /// <summary>The answer's value is lower than the guess (numeric attributes only).</summary>
    Lower
}

/// <param name="GuessNumber">
/// Which attempt this is, 1-based. Only used to decide whether a losing player has run out of
/// attempts and should see the answer.
/// </param>
public record DailyGuessRequest(
    int GuessId,
    int GuessNumber = 1,
    DateOnly? Date = null,
    int? GameId = null,
    int MinPopularity = 0,
    bool RequireImage = false
);

public record GuessedCharacter(
    int Id,
    string Name,
    string? ImageUrl,
    string GameName,
    int ReleaseYear,
    string? Race,
    string? Hometown,
    string? Role,
    string? Affiliation
);

/// <summary>Per-attribute verdict. The answer itself is never implied by these alone.</summary>
public record GuessComparison(
    AttributeMatch GameName,
    AttributeMatch ReleaseYear,
    AttributeMatch Race,
    AttributeMatch Hometown,
    AttributeMatch Role,
    AttributeMatch Affiliation
);

public record RevealedAnswer(
    int Id,
    string Name,
    string? ImageUrl,
    string GameName,
    int ReleaseYear
);

/// <param name="Answer">
/// Null while the game is still in play. Populated only once the player has solved it or used
/// their final attempt — that is the whole point of scoring guesses server-side.
/// </param>
public record DailyGuessResponse(
    DateOnly Date,
    bool Correct,
    int GuessNumber,
    int GuessesAllowed,
    GuessedCharacter Guess,
    GuessComparison Comparison,
    RevealedAnswer? Answer
);
