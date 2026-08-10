namespace MoogleAPI.Web.Features.Characters.DailySilhouette;

/// <param name="Date">
/// Which day's puzzle to draw, UTC. Defaults to today. Past dates are allowed — the answers for
/// those are already public through <c>/characters/daily</c> — and future dates are rejected.
/// </param>
/// <param name="MinPopularity">
/// 0–100 notability floor. Must match the series being played: each value picks a different
/// character for the day, and so a different shape.
/// </param>
/// <param name="RequireImage">Only pick characters that have portrait art.</param>
/// <remarks>
/// The same filters as <c>/characters/daily</c>, because they select the puzzle rather than the
/// picture. Anything else would resolve a different answer than the one being guessed.
/// </remarks>
public record DailySilhouetteRequest(
    int? GameId,
    DateOnly? Date,
    int MinPopularity = 0,
    bool RequireImage = false
);
