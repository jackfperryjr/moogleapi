using System.Net.Http.Json;
using MoogleAPI.Scraper.Models;

namespace MoogleAPI.Scraper;

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
            // Fetch both pages (ns=0) and subcategories (ns=14) in one pass
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

            // Recurse into subcategories at this depth
            var subcats = batch.Where(m => m.Ns == 14).ToList();
            foreach (var subcat in subcats)
            {
                // Subcategory titles come as "Category:Foo" — strip the prefix
                var subcatName = subcat.Title.StartsWith("Category:")
                    ? subcat.Title["Category:".Length..]
                    : subcat.Title;

                var nested = await CollectMembersAsync(subcatName, depth + 1, visited, ct);
                articles.AddRange(nested);
            }

            continueToken = response?.Continue?.CmContinue;
            await Task.Delay(300, ct); // be polite to the wiki
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
}
