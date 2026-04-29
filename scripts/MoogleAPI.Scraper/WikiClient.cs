using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MoogleAPI.Scraper.Models;

namespace MoogleAPI.Scraper;

public record CharacterDetails(
    string? ImageUrl,
    string? Description,
    string? Role,
    string? Affiliation,
    string? Race,
    string? Hometown
);

public class WikiClient(HttpClient http)
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

        string? description = null, role = null, affiliation = null, race = null, hometown = null;
        if (wikitext is not null)
        {
            description = ParseIntroText(wikitext);
            role        = ParseInfoboxField(wikitext, "occupation");
            affiliation = ParseInfoboxField(wikitext, "affiliation");
            race        = ParseInfoboxField(wikitext, "race") ?? ParseInfoboxField(wikitext, "species");
            hometown    = ParseInfoboxField(wikitext, "home")
                       ?? ParseInfoboxField(wikitext, "hometown")
                       ?? ParseInfoboxField(wikitext, "birthplace");
        }

        await Task.Delay(150, ct);
        return new CharacterDetails(imageUrl, description, role, affiliation, race, hometown);
    }

    // Retries on 5xx / 429 with exponential backoff.
    private async Task<T?> GetJsonWithRetryAsync<T>(string url, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                return await http.GetFromJsonAsync<T>(url, ct);
            }
            catch (HttpRequestException ex) when (attempt < 3 && ShouldRetry(ex))
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct);
            }
        }
        return default;
    }

    private static bool ShouldRetry(HttpRequestException ex) =>
        ex.StatusCode is { } code && ((int)code >= 500 || (int)code == 429);

    private static string? ParseIntroText(string wikitext)
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
        // Collapse whitespace
        intro = Regex.Replace(intro, @"\n+", " ").Trim();

        if (string.IsNullOrWhiteSpace(intro)) return null;

        // Take first 2 sentences
        var sentenceEnds = Regex.Matches(intro, @"[.!?](?=\s|$)");
        if (sentenceEnds.Count >= 2)
            intro = intro[..(sentenceEnds[1].Index + 1)].Trim();
        else if (sentenceEnds.Count == 1)
            intro = intro[..(sentenceEnds[0].Index + 1)].Trim();

        return string.IsNullOrWhiteSpace(intro) ? null : intro;
    }

    private static string? ParseInfoboxField(string wikitext, string fieldName)
    {
        // Capture full line so wikilinks like [[Foo|Bar]] aren't truncated at the |.
        var match = Regex.Match(wikitext,
            $@"^\|\s*{Regex.Escape(fieldName)}\s*=\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (!match.Success) return null;

        var value = match.Groups[1].Value.Trim();

        // Strip leading list/bullet markers
        value = Regex.Replace(value, @"^[*#:;]+\s*", "");

        // Strip templates iteratively (innermost first handles nesting)
        for (var i = 0; i < 6 && value.Contains("{{"); i++)
            value = Regex.Replace(value, @"\{\{[^{}]*\}\}", "");
        value = value.Replace("{{", "").Replace("}}", "");

        // [[Link|Display]] → Display, [[Link]] → Link (including unclosed links cut at EOL)
        value = Regex.Replace(value, @"\[\[(?:[^\]|]+\|)?([^\]|]+)\]\]", "$1");
        value = Regex.Replace(value, @"\[\[(?:[^\]|]+\|)?([^\]|]+)", "$1");
        value = value.Replace("[[", "").Replace("]]", "");

        // Strip single-bracket content: [external links] and [editorial placeholder notes]
        value = Regex.Replace(value, @"\[[^\]]*\]", "");

        // Strip leaked field assignments appended on the same infobox line: |fieldname=...
        value = Regex.Replace(value, @"\s*\|[a-zA-Z_]+\s*=.*", "").Trim();

        // Strip refs and HTML tags
        value = Regex.Replace(value, @"<ref\b[^>]*/?>.*?</ref>", "", RegexOptions.Singleline);
        value = Regex.Replace(value, @"<ref\b[^>]*/?>", "");
        value = Regex.Replace(value, @"<[^>]+>", "");
        // Bold/italic
        value = Regex.Replace(value, @"'{2,}", "");
        // Clean trailing punctuation
        value = Regex.Replace(value, @"\s*[,;]\s*$", "").Trim();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
