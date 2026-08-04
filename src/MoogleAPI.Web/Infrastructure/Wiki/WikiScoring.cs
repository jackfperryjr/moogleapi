using System.Text.RegularExpressions;

namespace MoogleAPI.Web.Infrastructure.Wiki;

/// <summary>
/// The two judgements the bulk scraper made about a wiki page, kept because the dashboard's
/// single-page import has to make the same ones: how notable the article is, and whether it
/// describes one creature at all.
/// </summary>
public static partial class WikiScoring
{
    // Article size and inbound links both span orders of magnitude, so score on a log
    // scale and blend. Bounds are calibrated against observed extremes: ~40 bytes / 0 links
    // for a walk-on NPC, ~120k bytes / 500+ links for a series lead.
    private const double MinLogLength = 1.5;   // ~32 bytes
    private const double MaxLogLength = 5.0;   // ~100k bytes
    private const int BacklinkCap = 500;   // matches the API's lhlimit

    /// <summary>
    /// Notability, 0–100 — the floor the games filter their answer pools by.
    /// </summary>
    public static int ScorePopularity(PageSignals? signals)
    {
        if (signals is null) return 0;

        var lengthScore = Normalize(Math.Log10(signals.PageLength + 1), MinLogLength, MaxLogLength);
        var linkScore = Math.Log10(Math.Min(signals.Backlinks, BacklinkCap) + 1) / Math.Log10(BacklinkCap + 1);

        return (int)Math.Round(Math.Clamp(lengthScore * 0.6 + linkScore * 0.4, 0, 1) * 100);
    }

    private static double Normalize(double value, double min, double max) =>
        Math.Clamp((value - min) / (max - min), 0, 1);

    /// <summary>
    /// The enemy categories also hold the reference pages that describe a game's enemies
    /// collectively, rather than being an enemy. Those have to be excluded by name: they are
    /// the longest, most heavily linked articles in the category, so they score a perfect
    /// notability rating and would sit at the very top of the pool a game draws its answers
    /// from — "Final Fantasy VII enemy abilities" outranking Gilgamesh.
    /// </summary>
    /// <remarks>
    /// Nothing bulk-imports from those categories any more, but importing one page at a time
    /// makes it easier to land on such a page, not harder — it is the sort of title that turns
    /// up in a search. So the check survives as a warning on the import preview rather than as
    /// a filter applied behind your back.
    /// </remarks>
    public static bool IsMetaArticle(string title) => MetaArticle().IsMatch(title);

    [GeneratedRegex(
        @"^enem(y|ies)$" +
        @"|\b(enem(y|ies)\s+(abilit(y|ies)|actions?|formations|stats|data|types?|famil(y|ies))|enemies|characters|bestiary|list of)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex MetaArticle();
}
