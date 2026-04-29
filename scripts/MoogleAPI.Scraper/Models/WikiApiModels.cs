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

// prop=pageimages|revisions combined response
public record WikiDetailsResponse(
    [property: JsonPropertyName("query")] WikiDetailsQuery? Query
);

public record WikiDetailsQuery(
    [property: JsonPropertyName("pages")] Dictionary<string, WikiDetailsPage>? Pages
);

public record WikiDetailsPage(
    [property: JsonPropertyName("pageid")] int PageId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("thumbnail")] WikiThumbnail? Thumbnail,
    [property: JsonPropertyName("revisions")] List<WikiRevision>? Revisions
);

public record WikiThumbnail(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height
);

public record WikiRevision(
    [property: JsonPropertyName("*")] string? Content
);
