namespace MoogleAPI.Web.Infrastructure.Battle;

/// <summary>
/// Move lists for one build, kept so each combatant is only parsed once.
/// </summary>
/// <remarks>
/// Move lists are derived by regex from each monster's abilities, and vetting a fight compares
/// the player against every candidate in the game — hundreds of monsters, twice each. Building
/// them once per run keeps that off the hot path.
/// </remarks>
public sealed class MoveCache
{
    private readonly Dictionary<int, IReadOnlyList<Move>> _moves = [];

    public IReadOnlyList<Move> For(Fighter fighter)
    {
        if (_moves.TryGetValue(fighter.Id, out var cached)) return cached;

        var built = MoveBuilder.Build(fighter.Abilities);
        _moves[fighter.Id] = built;
        return built;
    }

    /// <summary>
    /// Pins a combatant's moves to a list built elsewhere. The player's character does not get
    /// its moves from <see cref="MoveBuilder"/> alone — it also carries an archetype kit — and
    /// without this the vetting arithmetic would rate the matchup on a weaker move list than
    /// the one the player is actually handed.
    /// </summary>
    public void Set(Fighter fighter, IReadOnlyList<Move> moves) => _moves[fighter.Id] = moves;
}
