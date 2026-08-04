using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MoogleAPI.Web.Infrastructure.Wiki;

public record CharacterDetails(
    string? ImageUrl,
    string? Description,
    string? Role,
    string? Affiliation,
    string? Race,
    string? Hometown,
    string? Abilities,
    string? Job,
    string? Weapon
);

/// <summary>Raw notability signals for a wiki page.</summary>
public record PageSignals(int PageLength, int Backlinks);

/// <summary>
/// Battle stats read from an enemy article's <c>{{infobox enemy stats ...}}</c>. Every value
/// is optional — each game's template names its fields differently and older articles list
/// only a subset.
/// </summary>
public record MonsterStats(
    int? HitPoints,
    int? MagicPoints,
    int? Level,
    int? Experience,
    int? Gil,
    string? Weaknesses,
    string? Absorbs,
    int? Attack,
    int? Defense,
    int? MagicAttack,
    int? MagicDefense,
    int? Speed,
    int? Evasion,
    string? Abilities,
    string? Drops,
    string? Steals
)
{
    public static readonly MonsterStats Empty =
        new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
}

/// <summary>Everything one enemy article yields in a single request.</summary>
public record MonsterDetails(
    string? ImageUrl,
    string? ImageFileName,
    string? Description,
    string? Location,
    string? Type,
    MonsterStats Stats,
    PageSignals? Signals
);

/// <summary>A Triple Triad card parsed from its wiki article.</summary>
public record CardDetails(
    int Top,
    int Left,
    int Right,
    int Bottom,
    string? Element,
    int Level,
    string? CardClass
);

public class WikiClient(HttpClient http, ILogger<WikiClient>? logger = null)
{
    private const string BaseUrl = "https://finalfantasy.fandom.com/api.php";
    private const int MaxDepth = 2;

    public Task<List<WikiMember>> GetCategoryMembersAsync(string category, CancellationToken ct = default)
        => CollectMembersAsync(category, depth: 0, visited: [], ct);

    private async Task<List<WikiMember>> CollectMembersAsync(
        string category, int depth, HashSet<string> visited, CancellationToken ct)
    {
        if (depth > MaxDepth || !visited.Add(category))
            return [];

        var articles = new List<WikiMember>();
        string? continueToken = null;

        do
        {
            var url = $"{BaseUrl}?action=query&list=categorymembers&cmtitle=Category:{Uri.EscapeDataString(category)}&format=json&cmlimit=500&cmtype=page|subcat&cmprop=ids|title|type";
            if (continueToken is not null)
                url += $"&cmcontinue={Uri.EscapeDataString(continueToken)}";

            var response = await GetJsonWithRetryAsync<WikiCategoryResponse>(url, ct);
            var batch = response?.Query?.CategoryMembers ?? [];

            foreach (var member in batch)
            {
                if (member.Ns == 0)
                    articles.Add(member);
            }

            foreach (var subcat in batch.Where(m => m.Ns == 14))
            {
                var subcatName = subcat.Title.StartsWith("Category:")
                    ? subcat.Title["Category:".Length..]
                    : subcat.Title;

                var nested = await CollectMembersAsync(subcatName, depth + 1, visited, ct);
                articles.AddRange(nested);
            }

            continueToken = response?.Continue?.CmContinue;
            await Task.Delay(150, ct);
        }
        while (continueToken is not null);

        return articles;
    }

    // Fetches section-0 wikitext and returns the parsed intro as plain text.
    public async Task<string?> GetDescriptionAsync(string title, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}?action=query&titles={Uri.EscapeDataString(title)}&prop=revisions&rvprop=content&rvsection=0&format=json";
        var response = await GetJsonWithRetryAsync<WikiDetailsResponse>(url, ct);
        var wikitext = response?.Query?.Pages?.Values.FirstOrDefault()?.Revisions?.FirstOrDefault()?.Content;
        await Task.Delay(150, ct);
        return wikitext is null ? null : ParseIntroText(wikitext);
    }

    // Fetches thumbnail + infobox fields + intro description in one request.
    public async Task<CharacterDetails> GetCharacterDetailsAsync(string title, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}?action=query&titles={Uri.EscapeDataString(title)}&prop=pageimages|revisions&pithumbsize=400&rvprop=content&rvsection=0&format=json";
        var response = await GetJsonWithRetryAsync<WikiDetailsResponse>(url, ct);
        var page = response?.Query?.Pages?.Values.FirstOrDefault();

        var imageUrl = page?.Thumbnail?.Source;
        var wikitext = page?.Revisions?.FirstOrDefault()?.Content;

        string? description = null, role = null, affiliation = null, race = null, hometown = null,
            abilities = null, job = null, weapon = null;
        if (wikitext is not null)
        {
            description = ParseIntroText(wikitext);
            role = ParseInfoboxField(wikitext, "occupation");
            // Battle class and armament. Both are read for the same reason: they say what a
            // character does in a fight, where "occupation" says what they do for a living —
            // Aerith's is "Florist" and Tifa's "Bar hostess", which describes neither of them
            // holding a weapon.
            job = ParseCharacterField(wikitext, "job", "class");
            weapon = ParseCharacterField(wikitext, "weapon", "weapons");
            affiliation = ParseInfoboxField(wikitext, "affiliation");
            race = ParseInfoboxField(wikitext, "race") ?? ParseInfoboxField(wikitext, "species");
            hometown = ParseInfoboxField(wikitext, "home")
                       ?? ParseInfoboxField(wikitext, "hometown")
                       ?? ParseInfoboxField(wikitext, "birthplace");
            // A character's signature commands: "Trance/Revert", "Blk Mag, Focus". Games with
            // several releases list one field per release ("ffviir abilities"), so all are read.
            abilities = ParseCharacterFieldList(wikitext, "abilities", "ability", "limit break", "special ability");
        }

        await Task.Delay(150, ct);
        return new CharacterDetails(
            imageUrl, description, role, affiliation, race, hometown, abilities, job, weapon);
    }

    /// <summary>
    /// The names the wiki lists as playable in one game, read from its character navbox.
    /// </summary>
    /// <remarks>
    /// A navbox is a flat list of <c>| N group =</c> / <c>| N list =</c> pairs, and the groups
    /// are what make it usable: the wiki has already sorted each game's cast into "Playable",
    /// "Non-playable", "Villains" and so on by hand. Only the playable groups are read.
    /// <para>
    /// Two shapes have to be handled. Final Fantasy X and XII put nothing in the "Playable"
    /// group itself and hang the names off nested subgroups (<c>| 1.1 group = Main</c>), so a
    /// child counts when its parent is playable. And the label is not always the word: III
    /// splits its cast into "Famicon playable" and "Remake playable", and XV calls the party
    /// "Main party".
    /// </para>
    /// </remarks>
    public async Task<List<string>> GetPlayableRosterAsync(string navboxTitle, CancellationToken ct = default)
    {
        var wikitext = await GetRawPageAsync(navboxTitle, ct);
        return wikitext is null ? [] : ParsePlayableRoster(wikitext);
    }

    private static readonly Regex NavboxField =
        new(@"^\|\s*([\d.]+)\s*(group|list)\s*=\s*(.*)$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex WikiLink =
        new(@"\[\[([^\]|]+)(?:\|([^\]]+))?\]\]", RegexOptions.Compiled);

    // "Playable", "Temporary playable", "Remake playable", "Main party".
    private static readonly Regex PlayableGroup =
        new(@"\bplayable\b|^\s*main party\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Checked separately rather than as a lookahead on the pattern above, so it disqualifies a
    // label wherever the word appears instead of only at the start.
    private static readonly Regex NonPlayableGroup =
        new(@"\bnon[- ]?playable\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool IsPlayableGroup(string label) =>
        PlayableGroup.IsMatch(label) && !NonPlayableGroup.IsMatch(label);

    // Subgroups of a playable group. "Guests" and "AI members" are deliberately excluded:
    // Final Fantasy XII files a Garif Hunter and a Rabanastre Watch under them, which are
    // escorts the player never controls.
    private static readonly Regex PlayableSubgroup =
        new(@"^(main|temporary)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static List<string> ParsePlayableRoster(string wikitext)
    {
        var groups = new Dictionary<string, string>();
        var lists = new Dictionary<string, string>();

        foreach (Match match in NavboxField.Matches(wikitext))
        {
            var target = match.Groups[2].Value.Equals("group", StringComparison.OrdinalIgnoreCase) ? groups : lists;
            target[match.Groups[1].Value] = match.Groups[3].Value.Trim();
        }

        var names = new List<string>();

        foreach (var (key, list) in lists)
        {
            var label = groups.GetValueOrDefault(key, "");

            var isPlayable = IsPlayableGroup(label);
            if (!isPlayable && key.Contains('.'))
            {
                var parent = groups.GetValueOrDefault(key[..key.IndexOf('.')], "");
                isPlayable = IsPlayableGroup(parent) && PlayableSubgroup.IsMatch(label);
            }

            if (!isPlayable) continue;

            foreach (Match link in WikiLink.Matches(list))
            {
                // "[[Cait Sith (Final Fantasy VII)|Cait Sith]]" — the display text is the name
                // as the character scraper stored it, so it wins over the article title.
                var name = (link.Groups[2].Success ? link.Groups[2].Value : link.Groups[1].Value).Trim();
                name = TrailingParenthetical.Replace(name, "").Trim();

                if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
            }
        }

        return names;
    }

    private static readonly Regex TrailingParenthetical =
        new(@"\s*\([^)]*\)\s*$", RegexOptions.Compiled);

    // Fandom lacks the PageViewInfo extension that Wikimedia wikis expose, so notability
    // is inferred from article size and how many other articles link here. Both discriminate
    // sharply: marquee characters run 100k+ bytes with 500+ backlinks, walk-on NPCs ~40 and 0.
    public async Task<PageSignals?> GetPageSignalsAsync(string title, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}?action=query&titles={Uri.EscapeDataString(title)}&prop=info|linkshere&lhlimit=500&lhnamespace=0&format=json";
        var response = await GetJsonWithRetryAsync<WikiDetailsResponse>(url, ct);
        var page = response?.Query?.Pages?.Values.FirstOrDefault();

        await Task.Delay(150, ct);
        if (page is null) return null;

        return new PageSignals(page.Length ?? 0, page.LinksHere?.Count ?? 0);
    }

    /// <summary>
    /// Pages directly in a category, without descending into subcategories. Boss categories
    /// nest by remake ("Bosses in Final Fantasy VII Remake"), and those entries belong to a
    /// different game, so the recursive walk used for enemy listings would mislabel them.
    /// </summary>
    public async Task<List<string>> GetCategoryPageTitlesAsync(string category, CancellationToken ct = default)
    {
        var titles = new List<string>();
        string? continueToken = null;

        do
        {
            var url = $"{BaseUrl}?action=query&list=categorymembers&cmtitle=Category:{Uri.EscapeDataString(category)}&format=json&cmlimit=500&cmtype=page&cmprop=title";
            if (continueToken is not null)
                url += $"&cmcontinue={Uri.EscapeDataString(continueToken)}";

            var response = await GetJsonWithRetryAsync<WikiCategoryResponse>(url, ct);
            titles.AddRange((response?.Query?.CategoryMembers ?? []).Select(m => m.Title));

            continueToken = response?.Continue?.CmContinue;
            await Task.Delay(150, ct);
        }
        while (continueToken is not null);

        return titles;
    }

    /// <summary>
    /// One request per enemy: thumbnail, full wikitext (the stats infobox lives in a later
    /// section, so this can't use rvsection=0), and the notability signals. Bundled because
    /// the enemy categories run to thousands of pages per game.
    /// </summary>
    public async Task<MonsterDetails> GetMonsterDetailsAsync(string title, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}?action=query&titles={Uri.EscapeDataString(title)}&prop=pageimages|revisions|info|linkshere&pithumbsize=400&rvprop=content&rvslots=main&lhlimit=500&lhnamespace=0&format=json";
        var response = await GetJsonWithRetryAsync<WikiDetailsResponse>(url, ct);
        var page = response?.Query?.Pages?.Values.FirstOrDefault();

        await Task.Delay(150, ct);
        if (page is null)
            return new MonsterDetails(null, null, null, null, null, MonsterStats.Empty, null);

        var wikitext = page.Revisions?.FirstOrDefault()?.Content;
        var signals = new PageSignals(page.Length ?? 0, page.LinksHere?.Count ?? 0);

        if (wikitext is null)
            return new MonsterDetails(page.Thumbnail?.Source, null, null, null, null, MonsterStats.Empty, signals);

        return new MonsterDetails(
            ImageUrl: page.Thumbnail?.Source,
            ImageFileName: ParseImageFileName(wikitext),
            Description: ParseIntroText(wikitext),
            Location: ParseInfoboxField(wikitext, "location"),
            Type: ParseInfoboxField(wikitext, "type"),
            Stats: ParseMonsterStats(wikitext),
            Signals: signals
        );
    }

    // Full wikitext of a page, all sections.
    public async Task<string?> GetRawPageAsync(string title, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}?action=query&titles={Uri.EscapeDataString(title)}&prop=revisions&rvprop=content&rvslots=main&format=json";
        var response = await GetJsonWithRetryAsync<WikiDetailsResponse>(url, ct);
        await Task.Delay(150, ct);
        return response?.Query?.Pages?.Values.FirstOrDefault()?.Revisions?.FirstOrDefault()?.Content;
    }

    // Reads the {{LA|Page title|Display}} entries off a "<Game> Triple Triad cards" list page.
    public static List<(string Title, string Name)> ParseCardList(string wikitext)
    {
        return LinkedArticle.Matches(wikitext)
            .Select(m => (Title: m.Groups[1].Value.Trim(), Name: m.Groups[2].Value.Trim()))
            .Where(x => x.Title.Length > 0 && x.Name.Length > 0)
            .GroupBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    // Parses one card article's infobox: |stats=top<br/>left right<br/>bottom, |element=, |type=
    public async Task<CardDetails?> GetCardDetailsAsync(string title, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}?action=query&titles={Uri.EscapeDataString(title)}&prop=revisions&rvprop=content&rvslots=main&rvsection=0&format=json";
        var response = await GetJsonWithRetryAsync<WikiDetailsResponse>(url, ct);
        var wikitext = response?.Query?.Pages?.Values.FirstOrDefault()?.Revisions?.FirstOrDefault()?.Content;

        await Task.Delay(150, ct);
        return wikitext is null ? null : ParseCard(wikitext);
    }

    /// <summary>
    /// Reads a card's corner values, element, level, and class out of its infobox.
    /// Returns null when the stats field is absent or malformed.
    /// </summary>
    public static CardDetails? ParseCard(string wikitext)
    {
        var statsRaw = RawInfoboxField(wikitext, "stats");
        if (statsRaw is null) return null;

        // "A<br/>9 4<br/>6" → 10, 9, 4, 6. Drop <ref> footnotes first so their
        // digits don't get read as corner values.
        statsRaw = Regex.Replace(statsRaw, @"<ref\b[^>]*/?>.*?</ref>", " ", RegexOptions.Singleline);
        statsRaw = Regex.Replace(statsRaw, @"<[^>]+>", " ");

        var values = CardValue.Matches(statsRaw)
            .Select(m => m.Value)
            .Select(v => v == "A" ? 10 : int.Parse(v))
            .ToList();
        if (values.Count != 4) return null;

        var typeRaw = RawInfoboxField(wikitext, "type") ?? "";
        var levelMatch = Regex.Match(typeRaw, @"Level\s+(\d+)", RegexOptions.IgnoreCase);
        var classMatch = Regex.Match(typeRaw, @"\b(Monster|Boss|GF|Player)\s+Card\b", RegexOptions.IgnoreCase);

        var element = ParseInfoboxField(wikitext, "element");
        if (element is not null && element.Equals("None", StringComparison.OrdinalIgnoreCase))
            element = null;

        return new CardDetails(
            Top: values[0],
            Left: values[1],
            Right: values[2],
            Bottom: values[3],
            Element: element,
            Level: levelMatch.Success ? int.Parse(levelMatch.Groups[1].Value) : 0,
            CardClass: classMatch.Success ? classMatch.Groups[1].Value : null
        );
    }

    /// <summary>
    /// Reads HP/MP/level/EXP/gil and elemental affinities out of an enemy's stats infobox.
    /// Articles that tabulate several versions or forms of an enemy (FFIV's Easy Type, FFXII's
    /// level bands, bosses with multiple phases) repeat every field, prefixed with a section
    /// number; only the first block is read, so the values describe the enemy's first form.
    /// </summary>
    public static MonsterStats ParseMonsterStats(string wikitext)
    {
        var (weaknesses, absorbs) = ParseElementalAffinities(wikitext);

        return new MonsterStats(
            HitPoints: ParseStatNumber(wikitext, "hp", "hp min"),
            MagicPoints: ParseStatNumber(wikitext, "mp", "mp min"),
            Level: ParseStatNumber(wikitext, "level", "lv", "level min"),
            Experience: ParseStatNumber(wikitext, "exp", "exp min", "experience"),
            Gil: ParseStatNumber(wikitext, "gil"),
            Weaknesses: weaknesses,
            Absorbs: absorbs,
            Attack: ParseStatNumber(wikitext, "attack", "attack power", "strength", "str"),
            Defense: ParseStatNumber(wikitext, "defense", "defence"),
            MagicAttack: ParseStatNumber(wikitext, "magic", "magic atk", "magick power", "magic power"),
            MagicDefense: ParseStatNumber(wikitext, "magic defense", "magic def", "magick resist", "magic defence"),
            Speed: ParseStatNumber(wikitext, "speed", "agility", "dexterity"),
            Evasion: ParseStatNumber(wikitext, "evasion", "evade"),
            Abilities: ParseFieldList(wikitext, AbilityFields),
            Drops: ParseFieldList(wikitext, DropFields),
            Steals: ParseFieldList(wikitext, StealFields)
        );
    }

    // What the enemy does in battle. The FFX pair "weapon abilities" / "armor abilities" is
    // deliberately absent: those are the abilities the *player* can customize onto gear using
    // this enemy's drops, not moves the enemy has — and since StatPrefix admits only version
    // numbers and platforms, "weapon"/"armor" can never sneak in as a prefix either.
    private static readonly string[] AbilityFields =
        ["abilities", "special attack", "other abilities", "technicks", "magicks", "attacks"];

    private static readonly string[] DropFields =
        ["drop 1", "drop 2", "drop", "common drop", "rare drop", "item dropped"];

    private static readonly string[] StealFields =
        ["steal 1", "steal 2", "steal", "common steal", "rare steal"];

    /// <summary>
    /// Collects every value across a set of related fields into one comma-separated list.
    /// Articles repeat these per platform and per version ("snes rage", "gba rage") and
    /// repeat entries within a single field — FFVII lists Bodyblow three times because the
    /// enemy's AI rolls it three ways — so values are de-duplicated, case-insensitively,
    /// in the order the article presents them.
    /// </summary>
    internal static string? ParseFieldList(string wikitext, params string[] fieldNames) =>
        ParseFieldList(wikitext, StatPrefix, fieldNames);

    // Character infoboxes name an ability field per release — "ffviir abilities",
    // "ffviir2 abilities" — so the closed platform list that guards the enemy stat fields is
    // too strict here. Character infoboxes have no equivalent of "bribe gil" to guard against.
    private const string ReleasePrefix = @"(?:[a-z0-9]+\s+){0,2}";

    internal static string? ParseCharacterFieldList(string wikitext, params string[] fieldNames) =>
        ParseFieldList(wikitext, ReleasePrefix, fieldNames);

    /// <summary>
    /// A single character infobox value, tolerating the per-release prefix.
    /// </summary>
    /// <remarks>
    /// Games differ on whether the field is prefixed at all: Final Fantasy VI and IX write
    /// <c>|weapon=</c>, while Final Fantasy VII writes <c>|ffvii weapon=</c> and
    /// <c>|ffviir weapon=</c> for the remake. Reading only the bare form is why the first pass
    /// left every Final Fantasy VII character with no weapon, and so no battle role.
    /// <para>
    /// The prefix cannot be allowed to swallow a qualifier, though — every one of these
    /// articles also carries <c>|ultimate weapon=</c>, naming a specific late-game item rather
    /// than the class of arms the character uses. Matching it would call Cloud's weapon "Ultima
    /// Weapon" instead of "Broadswords".
    /// </para>
    /// </remarks>
    internal static string? ParseCharacterField(string wikitext, params string[] fieldNames)
    {
        foreach (var field in fieldNames)
        {
            foreach (Match match in Regex.Matches(wikitext,
                         $@"^\|\s*((?:[a-z0-9]+\s+){{0,2}}){Regex.Escape(field)}\s*=\s*(.+)$",
                         RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                if (QualifiedField.IsMatch(match.Groups[1].Value)) continue;

                var cleaned = CleanFieldValue(match.Groups[2].Value);
                if (cleaned is not null) return cleaned;
            }
        }

        return null;
    }

    // Words that turn the field into a different question. A release tag is anything else.
    private static readonly Regex QualifiedField =
        new(@"\b(ultimate|starting|initial|default|optional|alternate)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string? ParseFieldList(string wikitext, string prefixPattern, string[] fieldNames)
    {
        var seen = new List<string>();

        foreach (var field in fieldNames)
        {
            foreach (Match match in Regex.Matches(wikitext,
                         $@"^\|\s*{prefixPattern}{Regex.Escape(field)}\s*=\s*(.+)$",
                         RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                var cleaned = CleanFieldValue(match.Groups[1].Value);
                if (cleaned is null) continue;

                foreach (var entry in cleaned.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    // "Blaze (Level 1 = Attack x 1.5)" keeps its parenthetical; an entry that is
                    // *only* a note ("None", "N/A") carries nothing worth storing.
                    if (entry.Length < 2 || IsPlaceholder(entry)) continue;
                    if (!seen.Contains(entry, StringComparer.OrdinalIgnoreCase))
                        seen.Add(entry);
                }
            }
        }

        return seen.Count == 0 ? null : string.Join(", ", seen);
    }

    private static bool IsPlaceholder(string entry) =>
        entry.Equals("None", StringComparison.OrdinalIgnoreCase) ||
        entry.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
        entry.Equals("Nothing", StringComparison.OrdinalIgnoreCase) ||
        entry.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        entry.All(c => !char.IsLetterOrDigit(c));

    // Stat lines carry a section number ("| 1 hp = 55") or a platform ("| snes hp = 55") when
    // the article covers more than one version, and the two can stack ("| 1 max hp ="). Anything
    // else in front of the field name means it's a different stat ("| bribe gil = 17,000" is
    // what an FFX enemy costs to bribe), so the prefix list stays closed.
    private const string StatPrefix = @"(?:(?:\d+|snes|nes|ps|psx|psp|gba|ios|android|pc|pr|3d|mobile|max)\s+){0,2}";

    private static int? ParseStatNumber(string wikitext, params string[] fieldNames)
    {
        foreach (var field in fieldNames)
        {
            var match = Regex.Match(wikitext,
                $@"^\|\s*{StatPrefix}{Regex.Escape(field)}\s*=\s*([\d,]+)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);

            if (match.Success && int.TryParse(match.Groups[1].Value.Replace(",", ""), out var value))
                return value;
        }

        return null;
    }

    // Every game names its elements differently, and status ailments share the same field
    // syntax, so only these field names are read as elemental — "poison" and "darkness" are
    // deliberately absent because they're statuses in about as many games as they're elements.
    private static readonly (string Field, string Element)[] ElementFields =
    [
        ("fire", "Fire"),
        ("ice", "Ice"), ("blizzard", "Ice"),
        ("thunder", "Thunder"), ("lightning", "Thunder"), ("bolt", "Thunder"),
        ("water", "Water"),
        ("earth", "Earth"),
        ("wind", "Wind"), ("air", "Wind"),
        ("holy", "Holy"), ("pearl", "Holy"), ("light", "Holy"),
        ("dark", "Dark"), ("shadow", "Dark"),
        ("gravity", "Gravity"),
    ];

    /// <summary>
    /// Buckets the infobox's elemental fields into "takes extra damage" and "heals from".
    /// Everything else an affinity field can say — Immune, Halve, Nullify, a resistance
    /// percentage at or below 100 — is not interesting enough to store.
    /// </summary>
    internal static (string? Weaknesses, string? Absorbs) ParseElementalAffinities(string wikitext)
    {
        var weak = new List<string>();
        var absorb = new List<string>();

        foreach (var (field, element) in ElementFields)
        {
            var match = Regex.Match(wikitext,
                $@"^\|\s*{StatPrefix}{Regex.Escape(field)}\s*=\s*(.+)$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (!match.Success) continue;

            var value = match.Groups[1].Value.Trim();

            if (IsWeakness(value) && !weak.Contains(element))
                weak.Add(element);
            else if (IsAbsorption(value) && !absorb.Contains(element))
                absorb.Add(element);
        }

        return (Join(weak), Join(absorb));

        static string? Join(List<string> elements) =>
            elements.Count == 0 ? null : string.Join(", ", elements);
    }

    // Not every game words its affinities: FFXV gives a damage multiplier ("| ice = 300%") and
    // FFVIII a bare percentage where 100 is neutral, over 100 takes extra damage, and a negative
    // value heals ("| water = 290", "| fire = -100").
    private static readonly Regex DamageMultiplier = new(@"^(-?\d+)\s*%?\s*$", RegexOptions.Compiled);

    private static bool IsWeakness(string value) =>
        value.Contains("weak", StringComparison.OrdinalIgnoreCase) ||
        DamageTaken(value) > 100;

    private static bool IsAbsorption(string value) =>
        value.Contains("absorb", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("drain", StringComparison.OrdinalIgnoreCase) ||
        DamageTaken(value) < 0;

    private static int? DamageTaken(string value)
    {
        var match = DamageMultiplier.Match(value);
        return match.Success && int.TryParse(match.Groups[1].Value, out var percentage)
            ? percentage
            : null;
    }

    /// <summary>
    /// The image filename from an infobox, used as a fallback when the page has no
    /// MediaWiki thumbnail — sprite-era enemies usually put every version in a
    /// <c>&lt;gallery&gt;</c>, and those don't get picked up as a page image.
    /// </summary>
    internal static string? ParseImageFileName(string wikitext)
    {
        var match = Regex.Match(wikitext, @"^\|\s*image\s*=\s*(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (!match.Success) return null;

        var value = match.Groups[1].Value.Trim();

        // "<gallery>" opens a block whose entries are on the following lines:
        //   BombFF6.PNG|SNES/PS/GBA/PR
        if (value.StartsWith("<gallery", StringComparison.OrdinalIgnoreCase))
        {
            var rest = wikitext[(match.Index + match.Length)..];
            var entry = Regex.Match(rest, @"^\s*([^\r\n|<\[\]]+)", RegexOptions.Multiline);
            value = entry.Success ? entry.Groups[1].Value.Trim() : "";
        }

        // Unwrap "[[File:Bomb.png|150px]]" down to the bare filename.
        value = Regex.Replace(value, @"^\[\[\s*(?:File|Image)\s*:\s*", "", RegexOptions.IgnoreCase);
        value = value.Split('|')[0].Replace("]", "").Trim();

        return ImageFileName.IsMatch(value) ? value : null;
    }

    private static readonly Regex ImageFileName =
        new(@"\.(?:png|jpg|jpeg|gif|webp)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Resolves "TTGeezard.png" → a full CDN URL. Batched: up to 50 titles per request.
    public async Task<Dictionary<string, string>> ResolveImageUrlsAsync(
        IEnumerable<string> fileNames, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var batch in fileNames.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(50))
        {
            var titles = string.Join("|", batch.Select(f => "File:" + f));
            var url = $"{BaseUrl}?action=query&titles={Uri.EscapeDataString(titles)}&prop=imageinfo&iiprop=url&format=json";
            var response = await GetJsonWithRetryAsync<WikiDetailsResponse>(url, ct);

            var pages = response?.Query?.Pages?.Values.ToList() ?? [];
            foreach (var page in pages)
            {
                var src = page.ImageInfo?.FirstOrDefault()?.Url;
                if (src is null) continue;

                var name = page.Title.StartsWith("File:", StringComparison.OrdinalIgnoreCase)
                    ? page.Title["File:".Length..]
                    : page.Title;
                result[name] = src;
            }

            await Task.Delay(150, ct);
        }

        return result;
    }

    private static readonly Regex LinkedArticle =
        new(@"\{\{LA\|([^|}]+)\|([^|}]+)\}\}", RegexOptions.Compiled);

    private static readonly Regex CardValue =
        new(@"\b(?:10|[1-9]|A)\b", RegexOptions.Compiled);

    // Infobox value with markup left intact — callers that need the raw form (stats, type).
    private static string? RawInfoboxField(string wikitext, string fieldName)
    {
        var match = Regex.Match(wikitext,
            $@"^\|\s*{Regex.Escape(fieldName)}\s*=\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Retries on 5xx / 429, then gives up on that one page rather than failing the run.
    /// A scrape walks tens of thousands of articles over hours: letting a single throttled
    /// request throw kills the whole job, and the pages it already wrote are the only thing
    /// that survives. Anything abandoned here is simply refetched by the next run, because
    /// the row keeps whatever nulls made it a candidate for enrichment.
    /// </summary>
    private async Task<T?> GetJsonWithRetryAsync<T>(string url, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                return await http.GetFromJsonAsync<T>(url, ct);
            }
            catch (HttpRequestException ex) when (ShouldRetry(ex))
            {
                if (attempt == 3)
                {
                    logger?.LogWarning("Giving up on {Url} after 4 attempts ({Status}).", url, ex.StatusCode);
                    break;
                }

                await Task.Delay(BackoffFor(ex, attempt), ct);
            }
        }

        return default;
    }

    // Being rate limited means the whole crawl is going too fast, not that one request was
    // unlucky, so 429 backs off far harder than a transient server error: 10s, 30s, 90s.
    private static TimeSpan BackoffFor(HttpRequestException ex, int attempt) =>
        (int?)ex.StatusCode == 429
            ? TimeSpan.FromSeconds(10 * Math.Pow(3, attempt))
            : TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));

    private static bool ShouldRetry(HttpRequestException ex) =>
        ex.StatusCode is { } code && ((int)code >= 500 || (int)code == 429);

    internal static string? ParseIntroText(string wikitext)
    {
        // Redirect pages have no prose; their section-0 text is just "#REDIRECT [[...]]"
        if (Regex.IsMatch(wikitext, @"^\s*#REDIRECT", RegexOptions.IgnoreCase))
            return null;

        var pos = 0;

        // Skip past all top-level {{ }} blocks at the start (infobox, hatnotes, navboxes, etc.)
        while (pos < wikitext.Length)
        {
            while (pos < wikitext.Length && char.IsWhiteSpace(wikitext[pos])) pos++;

            if (pos + 1 >= wikitext.Length || wikitext[pos] != '{' || wikitext[pos + 1] != '{')
                break;

            var depth = 0;
            while (pos < wikitext.Length)
            {
                if (pos + 1 < wikitext.Length && wikitext[pos] == '{' && wikitext[pos + 1] == '{')
                { depth++; pos += 2; }
                else if (pos + 1 < wikitext.Length && wikitext[pos] == '}' && wikitext[pos + 1] == '}')
                { depth--; pos += 2; if (depth == 0) break; }
                else pos++;
            }
        }

        if (pos >= wikitext.Length) return null;

        var remaining = wikitext[pos..];
        var headingMatch = Regex.Match(remaining, @"^==", RegexOptions.Multiline);
        var intro = headingMatch.Success ? remaining[..headingMatch.Index] : remaining;

        // Strip File/Image links entirely (multi-segment: [[File:x.jpg|right|150px|caption]])
        intro = Regex.Replace(intro, @"\[\[(?:File|Image):[^\]]*\]\]", "", RegexOptions.IgnoreCase);
        // [[Link|Display]] → Display, [[Link]] → Link
        intro = Regex.Replace(intro, @"\[\[(?:[^\]|]+\|)?([^\]|]+)\]\]", "$1");
        intro = Regex.Replace(intro, @"\[\[([^\]|]+)", "$1");
        intro = intro.Replace("[[", "").Replace("]]", "");
        // Strip templates
        for (var i = 0; i < 5 && intro.Contains("{{"); i++)
            intro = Regex.Replace(intro, @"\{\{[^{}]*\}\}", "");
        intro = intro.Replace("{{", "").Replace("}}", "");
        // Strip refs and HTML
        intro = Regex.Replace(intro, @"<ref\b[^>]*/?>.*?</ref>", "", RegexOptions.Singleline);
        intro = Regex.Replace(intro, @"<ref\b[^>]*/?>", "");
        intro = Regex.Replace(intro, @"<[^>]+>", "");
        // Strip bold/italic markers
        intro = Regex.Replace(intro, @"'{2,}", "");
        // Strip hatnote lines (:prefixed), redirect lines, and bullet lines
        intro = Regex.Replace(intro, @"^[:*#].*$", "", RegexOptions.Multiline);
        // Collapse whitespace, including the gaps left where markup was removed
        intro = Regex.Replace(intro, @"\n+", " ");
        intro = Regex.Replace(intro, @"\s{2,}", " ");
        // An inline template ahead of the first sentence leaves its trailing punctuation
        // behind: ". Ruby Dragon, also known as Claret Dragon, is a recurring enemy…"
        intro = Regex.Replace(intro, @"^[\s.,;:]+", "");
        // …and stripping one out mid-sentence leaves a space in front of the punctuation.
        intro = Regex.Replace(intro, @"\s+([,.;:])", "$1").Trim();

        if (string.IsNullOrWhiteSpace(intro)) return null;

        // Take first 2 sentences
        var sentenceEnds = Regex.Matches(intro, @"[.!?](?=\s|$)");
        if (sentenceEnds.Count >= 2)
            intro = intro[..(sentenceEnds[1].Index + 1)].Trim();
        else if (sentenceEnds.Count == 1)
            intro = intro[..(sentenceEnds[0].Index + 1)].Trim();

        return string.IsNullOrWhiteSpace(intro) ? null : intro;
    }

    internal static string? ParseInfoboxField(string wikitext, string fieldName)
    {
        // Capture full line so wikilinks like [[Foo|Bar]] aren't truncated at the |.
        var match = Regex.Match(wikitext,
            $@"^\|\s*{Regex.Escape(fieldName)}\s*=\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        return match.Success ? CleanFieldValue(match.Groups[1].Value) : null;
    }

    /// <summary>
    /// Strips an infobox value down to plain text: templates, file embeds, wikilinks, refs,
    /// HTML, and bold/italic markers all go, and a &lt;br&gt; becomes a comma so multi-value
    /// fields stay readable.
    /// </summary>
    private static string? CleanFieldValue(string rawValue)
    {
        var value = rawValue.Trim();

        // Strip leading list/bullet markers
        value = Regex.Replace(value, @"^[*#:;]+\s*", "");

        // Strip templates iteratively (innermost first handles nesting)
        for (var i = 0; i < 6 && value.Contains("{{"); i++)
            value = Regex.Replace(value, @"\{\{[^{}]*\}\}", "");
        value = value.Replace("{{", "").Replace("}}", "");

        // Drop [[File:…]] / [[Image:…]] embeds outright — they render as icons, not text, so
        // unwrapping them below would leave the filename sitting in the value ("Fire" becomes
        // "File:Tripletriad-fire.png Fire").
        value = Regex.Replace(value, @"\[\[\s*(?:File|Image)\s*:[^\]]*\]\]", " ", RegexOptions.IgnoreCase);

        // [[Link|Display]] → Display, [[Link]] → Link (including unclosed links cut at EOL)
        value = Regex.Replace(value, @"\[\[(?:[^\]|]+\|)?([^\]|]+)\]\]", "$1");
        value = Regex.Replace(value, @"\[\[(?:[^\]|]+\|)?([^\]|]+)", "$1");
        value = value.Replace("[[", "").Replace("]]", "");

        // Strip single-bracket content: [external links] and [editorial placeholder notes]
        value = Regex.Replace(value, @"\[[^\]]*\]", "");

        // Strip leaked field assignments appended on the same infobox line: |fieldname=...
        // Field names may contain spaces ("|japanese voice actor ="), so the name class has to
        // admit them — matching only [a-zA-Z_]+ left those assignments in the stored value.
        // The space after the pipe is not optional in practice — articles write "| gba drop ="
        // far more often than "|gba drop =". Without allowing it this rule never fired, and an
        // FFII enemy's abilities came through as "| gba drop = Wing Sword, Ice Shield": its
        // equipment drops, presented to a battle as moves.
        value = Regex.Replace(value, @"\s*\|\s*[a-zA-Z_][a-zA-Z_ ]*\s*=.*", "").Trim();

        // Strip refs and HTML tags
        value = Regex.Replace(value, @"<ref\b[^>]*/?>.*?</ref>", "", RegexOptions.Singleline);
        value = Regex.Replace(value, @"<ref\b[^>]*/?>", "");
        // <br> separates multiple values in an infobox field. Dropping it like any other tag
        // fuses them together ("First Shield of RosariaMarquess of Rosaria"), so it has to
        // become a real separator before the generic tag strip runs.
        value = Regex.Replace(value, @"<\s*br\s*/?\s*>", ", ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"<[^>]+>", "");
        // Bold/italic
        value = Regex.Replace(value, @"'{2,}", "");
        // Collapse gaps left by stripped markup — a double space is the same signature
        // DataRepair treats as a damaged value.
        value = Regex.Replace(value, @"\s{2,}", " ");
        // Tidy separators left by removed segments: ", ," and a dangling leading comma.
        value = Regex.Replace(value, @"(,\s*){2,}", ", ");
        value = Regex.Replace(value, @"^\s*,\s*", "");
        // Clean trailing punctuation
        value = Regex.Replace(value, @"\s*[,;]\s*$", "").Trim();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
