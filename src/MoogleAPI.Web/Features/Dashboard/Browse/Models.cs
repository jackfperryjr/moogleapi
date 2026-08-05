namespace MoogleAPI.Web.Features.Dashboard.Browse;

/// <summary>
/// Shared paging/filtering for the three catalogue tabs.
/// </summary>
/// <remarks>
/// Page and size are clamped rather than validated: this is a browse tool for one person, and a
/// stale number in a bookmarked query string should still return a table instead of a 400.
/// </remarks>
public record BrowseRequest(
    string? Search = null,
    int? GameId = null,
    int Page = 1,
    int PageSize = 50)
{
    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => Math.Clamp(PageSize, 1, 200);
    public int Skip => (SafePage - 1) * SafePageSize;
}

public record BrowseResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

// ── Editable field sets ───────────────────────────────────────────────────────
// These records are both halves of the round trip: the browse tables send them out, and the
// editor sends the same shape back to PUT. Keeping them separate from the row wrapper is what
// makes that safe — a row also carries its game's name and year, which are joined in for display
// and are not this row's to change, and a single record would invite the editor to post them
// back and the update to quietly ignore them.

public record CharacterEdit(
    string Name,
    string? Description,
    string? Role,
    string? Affiliation,
    string? Race,
    string? Hometown,
    string? Job,
    string? Weapon,
    string? Abilities,
    bool IsPlayable,
    int Popularity,
    int? WikiPageLength,
    int? WikiBacklinks,
    string? ImageUrl,
    string? ImageSourceUrl,
    string? GeneratedImageUrl,
    string? ImageKind,
    int GameId
);

public record MonsterEdit(
    string Name,
    string? Description,
    string? Category,
    string? Location,
    int? HitPoints,
    int? MagicPoints,
    int? Level,
    int? Experience,
    int? Gil,
    int? Attack,
    int? Defense,
    int? MagicAttack,
    int? MagicDefense,
    int? Speed,
    int? Evasion,
    string? Abilities,
    string? Drops,
    string? Steals,
    string? Weaknesses,
    string? Absorbs,
    int Popularity,
    int? WikiPageLength,
    int? WikiBacklinks,
    string? ImageUrl,
    string? ImageSourceUrl,
    string? GeneratedImageUrl,
    string? ImageKind,
    int GameId
);

/// <remarks>
/// Two images, and no <c>GeneratedImageUrl</c>. The full logo and the square emblem are separate
/// crops rather than two sizes of one picture, so both are stored. Generation, meanwhile, selects
/// monsters and characters only — a generated slot here would be one nothing can ever fill.
/// </remarks>
public record GameEdit(
    string Name,
    int ReleaseYear,
    string Platform,
    string? Description,
    string? ImageUrl,
    string? ThumbnailUrl,
    string? ImageSourceUrl
);

// ── Rows ──────────────────────────────────────────────────────────────────────

/// <param name="GameName">Joined in for the table; change it by moving the row to another game.</param>
public record CharacterRow(int Id, string GameName, int ReleaseYear, CharacterEdit Fields);

public record MonsterRow(int Id, string GameName, int ReleaseYear, MonsterEdit Fields);

/// <param name="CharacterCount">
/// What a delete would have to take with it — the games tab shows these so an empty game can be
/// told apart from one holding two hundred monsters before anything is removed.
/// </param>
public record GameRow(int Id, int CharacterCount, int MonsterCount, int CardCount, GameEdit Fields);
