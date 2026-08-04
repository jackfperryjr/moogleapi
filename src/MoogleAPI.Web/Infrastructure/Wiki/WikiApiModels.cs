using System.Text.Json.Serialization;

namespace MoogleAPI.Web.Infrastructure.Wiki;

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
    [property: JsonPropertyName("revisions")] List<WikiRevision>? Revisions,
    [property: JsonPropertyName("length")] int? Length,
    [property: JsonPropertyName("linkshere")] List<WikiMember>? LinksHere,
    [property: JsonPropertyName("imageinfo")] List<WikiImageInfo>? ImageInfo
);

public record WikiImageInfo(
    [property: JsonPropertyName("url")] string? Url
);

public record WikiThumbnail(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height
);

/// <remarks>
/// The API returns revision content in one of two shapes. Requests that pass
/// <c>rvslots=main</c> nest it under <c>slots.main["*"]</c>; requests that omit it get the
/// legacy top-level <c>"*"</c>, which the API marks deprecated. Both are accepted here so a
/// caller's choice of <c>rvslots</c> can't silently yield null content.
/// </remarks>
public record WikiRevision(
    [property: JsonPropertyName("*")] string? LegacyContent,
    [property: JsonPropertyName("slots")] WikiSlots? Slots
)
{
    public string? Content => Slots?.Main?.Content ?? LegacyContent;
}

public record WikiSlots(
    [property: JsonPropertyName("main")] WikiSlot? Main
);

public record WikiSlot(
    [property: JsonPropertyName("*")] string? Content
);
