using System.Net.Http.Json;
using MoogleAPI.Scraper.Models;

namespace MoogleAPI.Scraper;

public class WikiClient(HttpClient http)
{
    private const string BaseUrl = "https://finalfantasy.fandom.com/api.php";

    public async Task<List<WikiMember>> GetCategoryMembersAsync(string category, CancellationToken ct = default)
    {
        var members = new List<WikiMember>();
        string? continueToken = null;

        do
        {
            var url = $"{BaseUrl}?action=query&list=categorymembers&cmtitle=Category:{Uri.EscapeDataString(category)}&format=json&cmlimit=500&cmnamespace=0";
            if (continueToken is not null)
                url += $"&cmcontinue={Uri.EscapeDataString(continueToken)}";

            var response = await http.GetFromJsonAsync<WikiCategoryResponse>(url, ct);
            if (response?.Query?.CategoryMembers is { } batch)
                members.AddRange(batch);

            continueToken = response?.Continue?.CmContinue;
            await Task.Delay(300, ct); // be polite to the wiki
        }
        while (continueToken is not null);

        return members;
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
