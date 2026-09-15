namespace Jellyfin.Plugin.GravureX.Dmm;

/// <summary>
/// Normalised representation of a DMM mono (DVD / Blu-ray) product page.
/// </summary>
public sealed class DmmTitle
{
    /// <summary>DMM content id, e.g. <c>n_1234abcd5678</c>.</summary>
    public string ContentId { get; set; } = string.Empty;

    /// <summary>Full product title as published by DMM, e.g. <c>タイトル/出演者名</c>.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Title with the trailing <c>/出演者名</c> segment removed.</summary>
    public string CleanTitle { get; set; } = string.Empty;

    public string Overview { get; set; } = string.Empty;

    /// <summary>JAN / EAN-13 barcode.</summary>
    public string? Jan { get; set; }

    /// <summary>Maker / label.</summary>
    public string? Maker { get; set; }

    public DateTime? ReleaseDate { get; set; }

    /// <summary>Runtime in minutes.</summary>
    public int? RuntimeMinutes { get; set; }

    public string? Series { get; set; }

    public string? Director { get; set; }

    public IReadOnlyList<string> Genres { get; set; } = Array.Empty<string>();

    public IReadOnlyList<DmmPerson> Actors { get; set; } = Array.Empty<DmmPerson>();

    /// <summary>
    /// Package image: the front and back cover side by side, roughly 800x536.
    /// The poster is cropped out of the right half of this at download time.
    /// </summary>
    public string? PackageImageUrl { get; set; }

    /// <summary>
    /// Gallery images as published on the page, one per sample photo. These are
    /// small thumbnails; <see cref="DmmImageUrls.ToLarge"/> maps them to full size.
    /// </summary>
    public IReadOnlyList<string> GalleryImageUrls { get; set; } = Array.Empty<string>();

    public float? CommunityRating { get; set; }

    /// <summary>Media type as shown on the page, e.g. <c>DVD</c> or <c>Blu-ray</c>.</summary>
    public string? Media { get; set; }

    /// <summary>
    /// Length of the page this was parsed from. Recorded so a truncated or
    /// degraded response can be told apart from a title that genuinely has no
    /// preview images.
    /// </summary>
    public int PageLength { get; set; }

    public string DetailUrl =>
        string.IsNullOrEmpty(ContentId)
            ? string.Empty
            : $"https://www.dmm.com/mono/dvd/-/detail/=/cid={ContentId}/";
}

/// <summary>
/// A performer credited on a title.
/// </summary>
public sealed class DmmPerson
{
    public string Name { get; set; } = string.Empty;

    /// <summary>DMM actor id, usable to build the actor listing URL.</summary>
    public string? ActorId { get; set; }
}

/// <summary>
/// A performer entry taken from the DMM gravure idol index.
/// </summary>
public sealed class DmmActor
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Kana reading, when the actor listing page publishes one.</summary>
    public string? Reading { get; set; }

    /// <summary>
    /// Portrait URL. DMM only publishes portraits for a subset of performers; the
    /// rest point at a shared placeholder image, which is filtered out.
    /// </summary>
    public string? ImageUrl { get; set; }

    public string ListingUrl =>
        string.IsNullOrEmpty(Id)
            ? string.Empty
            : $"https://www.dmm.com/mono/dvd/-/list/=/article=actor/id={Id}/";
}

/// <summary>
/// One entry of a DMM search or listing result page.
/// </summary>
public sealed class DmmSearchItem
{
    public string ContentId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    public DateTime? ReleaseDate { get; set; }
}
