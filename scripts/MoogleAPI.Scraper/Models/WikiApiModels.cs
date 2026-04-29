using System.Text.Json.Serialization;

namespace MoogleAPI.Scraper.Models;

public record WikiCategoryResponse(
    [property: JsonPropertyName("query")] WikiCategoryQuery? Query,
    [property: JsonPropertyName("continue")] WikiContinue? Continue
);

public record WikiCategoryQuery(
    [property: JsonPropertyName("categorymembers")] List<WikiMember>? CategoryMembers
);

public record WikiMember(
    [property: JsonPropertyName("pageid")] int PageId,
    [property: JsonPropertyName("ns")] int Ns,
    [property: JsonPropertyName("title")] string Title
);

public record WikiContinue(
    [property: JsonPropertyName("cmcontinue")] string? CmContinue
);

public record WikiExtractResponse(
    [property: JsonPropertyName("query")] WikiExtractQuery? Query
);

public record WikiExtractQuery(
    [property: JsonPropertyName("pages")] Dictionary<string, WikiPage>? Pages
);

public record WikiPage(
    [property: JsonPropertyName("pageid")] int PageId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("extract")] string? Extract
);
