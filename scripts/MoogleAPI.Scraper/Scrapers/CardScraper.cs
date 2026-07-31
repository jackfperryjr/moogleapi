using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Models;

namespace MoogleAPI.Scraper.Scrapers;

/// <summary>
/// Scrapes authentic Triple Triad card data. The per-game list page yields card article
/// titles and thumbnail filenames; each card article's infobox carries the corner values
/// (|stats=top&lt;br/&gt;left right&lt;br/&gt;bottom), element, and level.
/// </summary>
public class CardScraper(AppDbContext db, WikiClient wiki, ILogger<CardScraper> logger)
{
    // FFXIV is deliberately absent: it's an online game, and its card articles use a different
    // infobox layout that ParseCard doesn't read, so including it only produced parse failures.
    private static readonly Dictionary<string, string> GameCardLists = new()
    {
        ["Final Fantasy VIII"] = "Final Fantasy VIII Triple Triad cards",
    };

    // [[File:TTGeezard.png|120px|Geezard Card]] — the thumbnail column of the list table.
    private static readonly Regex CardImage =
        new(@"\[\[File:([^\]|]+)\|\d+px\|[^\]]*Card\]\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task ScrapeAsync(bool force = false, CancellationToken ct = default)
    {
        var games = await db.Games.ToListAsync(ct);

        foreach (var game in games)
        {
            if (!GameCardLists.TryGetValue(game.Name, out var listPage)) continue;

            logger.LogInformation("Scraping Triple Triad cards for {Game}...", game.Name);

            var listText = await wiki.GetRawPageAsync(listPage, ct);
            if (listText is null)
            {
                logger.LogWarning("  Could not fetch {Page}.", listPage);
                continue;
            }

            var cards = WikiClient.ParseCardList(listText);
            if (cards.Count == 0)
            {
                logger.LogWarning("  No cards found on {Page} — layout may have changed.", listPage);
                continue;
            }

            logger.LogInformation("  Found {Count} cards", cards.Count);

            var existing = new Dictionary<string, Card>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in await db.Cards.Where(c => c.GameId == game.Id).ToListAsync(ct))
                existing.TryAdd(c.Name, c);

            var imageUrls = await ResolveImagesAsync(listText, cards, ct);

            // Fetch card articles — 2 concurrent, matching the other scrapers' politeness.
            var sem = new SemaphoreSlim(2);
            var detailsMap = new ConcurrentDictionary<string, CardDetails>();

            await Task.WhenAll(cards.Select(async card =>
            {
                if (!force && existing.ContainsKey(card.Name)) return;

                await sem.WaitAsync(ct);
                try
                {
                    var details = await wiki.GetCardDetailsAsync(card.Title, ct);
                    if (details is not null)
                        detailsMap[card.Name] = details;
                    else
                        logger.LogWarning("  ? could not parse stats for {Card}", card.Name);
                }
                finally { sem.Release(); }
            }));

            foreach (var (_, name) in cards)
            {
                if (!detailsMap.TryGetValue(name, out var d)) continue;

                imageUrls.TryGetValue(name, out var imageUrl);

                if (existing.TryGetValue(name, out var card))
                {
                    card.Top = d.Top;
                    card.Left = d.Left;
                    card.Right = d.Right;
                    card.Bottom = d.Bottom;
                    card.Element = d.Element;
                    card.Level = d.Level;
                    card.CardClass = d.CardClass;
                    card.ImageUrl = imageUrl ?? card.ImageUrl;
                    logger.LogInformation("  * refreshed {Card}", name);
                }
                else
                {
                    db.Cards.Add(new Card
                    {
                        Name = name,
                        Top = d.Top,
                        Left = d.Left,
                        Right = d.Right,
                        Bottom = d.Bottom,
                        Element = d.Element,
                        Level = d.Level,
                        CardClass = d.CardClass,
                        ImageUrl = imageUrl,
                        GameId = game.Id
                    });
                    logger.LogInformation("  + {Card}  {T}/{L}/{R}/{B}", name, d.Top, d.Left, d.Right, d.Bottom);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("Cards done.");
    }

    // The list page's image column is positionally aligned with its card column, so the
    // nth File: link belongs to the nth card. Falls back to no image if the counts diverge.
    private async Task<Dictionary<string, string>> ResolveImagesAsync(
        string listText, List<(string Title, string Name)> cards, CancellationToken ct)
    {
        var files = CardImage.Matches(listText).Select(m => m.Groups[1].Value.Trim()).ToList();
        if (files.Count != cards.Count)
        {
            logger.LogWarning(
                "  Image count ({Images}) does not match card count ({Cards}) — skipping card art.",
                files.Count, cards.Count);
            return [];
        }

        var resolved = await wiki.ResolveImageUrlsAsync(files, ct);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < cards.Count; i++)
            if (resolved.TryGetValue(files[i], out var url))
                result[cards[i].Name] = url;

        return result;
    }
}
