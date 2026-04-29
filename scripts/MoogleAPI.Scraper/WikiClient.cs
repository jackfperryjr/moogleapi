using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MoogleAPI.Scraper.Models;

namespace MoogleAPI.Scraper;

public record CharacterDetails(
    string? ImageUrl,
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

            var response = await http.GetFromJsonAsync<WikiCategoryResponse>(url, ct);
            var batch = response?.Query?.CategoryMembers ?? [];

            foreach (var member in batch)
            {
                if (member.Ns == 0)
                    articles.Add(member);
            }

            var subcats = batch.Where(m => m.Ns == 14).ToList();
            foreach (var subcat in subcats)
            {
                var subcatName = subcat.Title.StartsWith("Category:")
                    ? subcat.Title["Category:".Length..]
                    : subcat.Title;

                var nested = await CollectMembersAsync(subcatName, depth + 1, visited, ct);
                articles.AddRange(nested);
            }

            continueToken = response?.Continue?.CmContinue;
            await Task.Delay(300, ct);
        }
        while (continueToken is not null);

        return articles;
    }

    public async Task<string?> GetExtractAsync(string title, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}?action=query&prop=extracts&exintro=true&exsentences=2&explaintext=true&titles={Uri.EscapeDataString(title)}&format=json";
        var response = await http.GetFromJsonAsync<WikiExtractResponse>(url, ct);
        var extract = response?.Query?.Pages?.Values.FirstOrDefault()?.Extract;
        await Task.Delay(200, ct);
        return string.IsNullOrWhiteSpace(extract) ? null : extract.Trim();
    }

    // Fetches thumbnail image + infobox fields (occupation, affiliation, race, hometown) in one request.
    public async Task<CharacterDetails> GetCharacterDetailsAsync(string title, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}?action=query&titles={Uri.EscapeDataString(title)}&prop=pageimages|revisions&pithumbsize=400&rvprop=content&rvsection=0&format=json";
        var response = await http.GetFromJsonAsync<WikiDetailsResponse>(url, ct);
        var page = response?.Query?.Pages?.Values.FirstOrDefault();

        var imageUrl = page?.Thumbnail?.Source;
        var wikitext = page?.Revisions?.FirstOrDefault()?.Content;

        string? role = null, affiliation = null, race = null, hometown = null;
        if (wikitext is not null)
        {
            role        = ParseInfoboxField(wikitext, "occupation");
            affiliation = ParseInfoboxField(wikitext, "affiliation");
            race        = ParseInfoboxField(wikitext, "race") ?? ParseInfoboxField(wikitext, "species");
            hometown    = ParseInfoboxField(wikitext, "home")
                       ?? ParseInfoboxField(wikitext, "hometown")
                       ?? ParseInfoboxField(wikitext, "birthplace");
        }

        await Task.Delay(200, ct);
        return new CharacterDetails(imageUrl, role, affiliation, race, hometown);
    }

    private static string? ParseInfoboxField(string wikitext, string fieldName)
    {
        var match = Regex.Match(wikitext,
            $@"\|\s*{Regex.Escape(fieldName)}\s*=\s*([^\n|{{]+)",
            RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var value = match.Groups[1].Value.Trim();

        // [[Link|Display]] → Display, [[Link]] → Link
        value = Regex.Replace(value, @"\[\[(?:[^|\]]+\|)?([^\]]+)\]\]", "$1");
        // {{template|...}} → strip entirely
        value = Regex.Replace(value, @"\{\{[^}]*\}\}", "");
        // <ref ...>...</ref> and self-closing <ref />
        value = Regex.Replace(value, @"<ref\b[^/]*/?>.*?</ref>", "", RegexOptions.Singleline);
        value = Regex.Replace(value, @"<[^>]+>", "");
        // Clean up trailing punctuation left by stripped markup
        value = Regex.Replace(value, @"\s*[,;]\s*$", "").Trim();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
