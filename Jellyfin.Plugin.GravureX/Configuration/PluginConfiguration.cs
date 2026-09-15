using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.GravureX.Configuration;

/// <summary>
/// User configurable settings for the DMM Gravure metadata provider.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Optional HTTP proxy, e.g. <c>http://127.0.0.1:7890</c>. DMM is not reachable
    /// from every network, so keep this configurable.
    /// </summary>
    public string ProxyUrl { get; set; } = string.Empty;

    /// <summary>
    /// User agent sent with every request. Empty uses the bundled default.
    /// </summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>
    /// Minimum delay between two requests to DMM, in milliseconds.
    /// </summary>
    public int RequestIntervalMs { get; set; } = 1500;

    /// <summary>
    /// Per request timeout in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How long a fetched page stays in the in-memory cache. Zero disables caching.
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 1440;

    /// <summary>
    /// Use the DMM keyword search endpoint to resolve a content number to a product.
    /// Without it only full DMM content ids in file names can be matched.
    /// </summary>
    public bool EnableSearchEndpoint { get; set; } = true;

    /// <summary>
    /// Put the content number in front of the title, as in
    /// <c>AB123-4567 タイトル</c>. The number is taken verbatim from the
    /// file name, so it keeps whatever hyphen position the file name uses.
    /// </summary>
    public bool PrefixTitleWithContentNumber { get; set; } = true;

    /// <summary>
    /// When several editions of the same title exist, prefer the plain edition
    /// over limited / bonus editions.
    /// </summary>
    public bool PreferStandardEdition { get; set; } = true;

    /// <summary>
    /// Write DMM genres into the item genres.
    /// </summary>
    public bool EnableGenres { get; set; } = true;

    /// <summary>
    /// Add the director to the item's people, where Jellyfin lists it alongside the
    /// performers. Off by default, which keeps the people list to the cast only.
    /// </summary>
    public bool ImportDirector { get; set; }

    /// <summary>
    /// Add the media type (DVD / Blu-ray) and the maker as tags.
    /// </summary>
    public bool EnableTags { get; set; } = true;

    /// <summary>
    /// Upper bound on how many preview images are offered. Every offered image is
    /// listed in the manual image picker; how many are actually saved during a
    /// scan is decided by the library's own image limit. Zero means no limit.
    /// </summary>
    public int MaxPreviewImages { get; set; } = 50;

    /// <summary>Preview images offered when no limit is set.</summary>
    public const int UnlimitedPreviewImages = 200;

    /// <summary>Shortest delay allowed between two requests.</summary>
    public const int MinRequestIntervalMs = 250;

    /// <summary>Delay used when the configured one is missing or unusable.</summary>
    public const int DefaultRequestIntervalMs = 1500;

    /// <summary>Timeout used when the configured one is missing or unusable.</summary>
    public const int DefaultRequestTimeoutSeconds = 30;

    /// <summary>
    /// Number of preview images to offer, treating a non positive limit as no limit.
    /// </summary>
    /// <remarks>
    /// The settings page used to be able to save zeroes into every numeric field,
    /// and a zero here silently dropped all preview artwork, so a stored zero must
    /// never be honoured as "offer nothing".
    /// </remarks>
    public int EffectiveMaxPreviewImages =>
        MaxPreviewImages > 0 ? Math.Clamp(MaxPreviewImages, 1, UnlimitedPreviewImages) : UnlimitedPreviewImages;

    /// <summary>
    /// Delay between requests, never below <see cref="MinRequestIntervalMs"/>.
    /// </summary>
    /// <remarks>
    /// Guards against a zeroed setting removing the delay altogether, which would
    /// put needless load on the source.
    /// </remarks>
    public int EffectiveRequestIntervalMs =>
        RequestIntervalMs > 0
            ? Math.Max(RequestIntervalMs, MinRequestIntervalMs)
            : DefaultRequestIntervalMs;

    /// <summary>Request timeout, falling back to the default.</summary>
    public int EffectiveRequestTimeoutSeconds =>
        RequestTimeoutSeconds > 0 ? RequestTimeoutSeconds : DefaultRequestTimeoutSeconds;

    /// <summary>
    /// Persist the JAN / EAN barcode as an additional provider id.
    /// </summary>
    public bool EnableJanProviderId { get; set; } = true;

    /// <summary>
    /// Look up performer portraits. DMM only publishes portraits for a subset of
    /// performers, so many lookups will find nothing.
    /// </summary>
    public bool EnableActorImages { get; set; } = true;

    /// <summary>
    /// How long the performer index stays cached before it is rebuilt. Rebuilding
    /// costs roughly fourteen requests.
    /// </summary>
    public int ActorIndexCacheDays { get; set; } = 7;
}
