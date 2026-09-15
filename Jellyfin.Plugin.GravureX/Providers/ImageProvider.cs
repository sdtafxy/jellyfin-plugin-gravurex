using Jellyfin.Plugin.GravureX.Configuration;
using Jellyfin.Plugin.GravureX.Dmm;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Jellyfin.Plugin.GravureX.Providers;

/// <summary>
/// Supplies artwork for titles and performers.
/// </summary>
/// <remarks>
/// The poster is the right half of the package spread, cropped while downloading.
/// The spread itself is offered as a backdrop, followed by the full size gallery
/// images, and the gallery also provides the thumbnails.
/// </remarks>
public class ImageProvider : IRemoteImageProvider, IHasOrder
{
    private const string JpegContentType = "image/jpeg";

    /// <summary>Where the front cover starts in the package spread.</summary>
    private const float FrontCoverStart = 0.53f;

    private readonly DmmClient _client;
    private readonly ItemContentIdResolver _contentIds;
    private readonly DmmIdolIndex _idolIndex;
    private readonly ILogger<ImageProvider> _logger;

    public ImageProvider(
        DmmClient client,
        ItemContentIdResolver contentIds,
        DmmIdolIndex idolIndex,
        ILogger<ImageProvider> logger)
    {
        _client = client;
        _contentIds = contentIds;
        _idolIndex = idolIndex;
        _logger = logger;
    }

    public string Name => Plugin.DisplayName;

    public int Order => 1;

    public bool Supports(BaseItem item) => item is Movie || item is Person;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) =>
        item is Person
            ? new[] { ImageType.Primary }
            : new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb };

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        if (item is Person person)
        {
            return await GetPersonImagesAsync(person, cancellationToken).ConfigureAwait(false);
        }

        var contentId = (await _contentIds
            .ResolveAsync(
                item.ProviderIds,
                ItemContentIdResolver.NamesFrom(item.Name, item.Path),
                cancellationToken)
            .ConfigureAwait(false)).ContentId;

        if (string.IsNullOrEmpty(contentId))
        {
            return Enumerable.Empty<RemoteImageInfo>();
        }

        var title = await _client.GetTitleAsync(contentId, cancellationToken).ConfigureAwait(false);
        if (title is null)
        {
            return Enumerable.Empty<RemoteImageInfo>();
        }

        return BuildImages(title);
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var wantsCover = DmmImageUrls.IsCover(url);
        var source = wantsCover ? DmmImageUrls.WithoutCoverMarker(url) : url;

        var image = await _client.GetImageAsync(source, cancellationToken).ConfigureAwait(false);
        if (image is null)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }

        // A redirect into /noimage/ means the full size gallery variant does not
        // exist for this entry, so fall back to the published thumbnail.
        if (image.IsPlaceholder && DmmImageUrls.IsLargeGallery(source))
        {
            var small = DmmImageUrls.ToSmall(source);
            _logger.LogDebug("GravureX: no full size image at {Url}, falling back to {Small}", source, small);

            var fallback = await _client.GetImageAsync(small, cancellationToken).ConfigureAwait(false);
            if (fallback is not null && !fallback.IsPlaceholder)
            {
                image = fallback;
                wantsCover = false;
            }
        }

        var bytes = wantsCover ? CropToFrontCover(image.Bytes) : image.Bytes;

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(JpegContentType);
        return response;
    }

    /// <summary>
    /// Offers the same set of artwork to all three image types, ordered so that an
    /// automatic refresh picks the right one for the type.
    /// </summary>
    /// <remarks>
    /// Jellyfin takes the first entry of each type during an automatic refresh but
    /// shows every entry in the manual image picker, so listing the whole set gives
    /// the picker full choice without changing what a scan picks. The poster is
    /// taken from the front cover cropped out of the package spread; backdrops and
    /// thumbnails default to the spread itself.
    /// </remarks>
    private List<RemoteImageInfo> BuildImages(DmmTitle title)
    {
        var maxPreviews = Plugin.Instance?.Configuration.EffectiveMaxPreviewImages
                         ?? PluginConfiguration.UnlimitedPreviewImages;

        var previews = title.GalleryImageUrls
            .Take(maxPreviews)
            .Select(DmmImageUrls.ToLarge)
            .ToList();

        var spread = title.PackageImageUrl;
        var cover = string.IsNullOrEmpty(spread) ? null : DmmImageUrls.AsCover(spread);

        // The spread is deliberately left out of the poster candidates. It is what
        // the poster is cropped from, and the picker renders the raw URL, so the two
        // entries would look identical there.
        var forPoster = PreferFirst(cover, previews);
        var forLandscape = PreferFirst(spread, previews);

        var images = new List<RemoteImageInfo>();
        Add(images, forPoster, ImageType.Primary);
        Add(images, forLandscape, ImageType.Backdrop);
        Add(images, forLandscape, ImageType.Thumb);

        _logger.LogInformation(
            "GravureX: {ContentId} offers {Primary} primary, {Backdrop} backdrop, {Thumb} thumbnail "
            + "(previews {Previews}, spread {HasSpread}, page {PageLength} bytes)",
            title.ContentId,
            images.Count(i => i.Type == ImageType.Primary),
            images.Count(i => i.Type == ImageType.Backdrop),
            images.Count(i => i.Type == ImageType.Thumb),
            title.GalleryImageUrls.Count,
            !string.IsNullOrEmpty(spread),
            title.PageLength);

        if (previews.Count == 0)
        {
            _logger.LogInformation(
                "GravureX: {ContentId} yielded no preview images; only the package spread is offered. "
                + "Pre-order titles genuinely have none, but for a released title this means the "
                + "page did not carry them and is worth reporting.",
                title.ContentId);
            _logger.LogInformation("GravureX: {ContentId} page was {PageLength} bytes", title.ContentId, title.PageLength);
        }

        // Logged per image so a report of "no backdrop" can be traced back to
        // whether the provider offered one at all.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            foreach (var image in images)
            {
                _logger.LogDebug("GravureX: offered {Type} {Url}", image.Type, image.Url);
            }
        }

        return images;
    }

    /// <summary>
    /// Builds a candidate list with <paramref name="first"/> at the front, since
    /// that is the entry an automatic refresh will take.
    /// </summary>
    private static List<string> PreferFirst(string? first, IEnumerable<string?> rest)
    {
        var list = new List<string>();
        if (!string.IsNullOrEmpty(first))
        {
            list.Add(first);
        }

        foreach (var url in rest)
        {
            if (!string.IsNullOrEmpty(url) && !list.Contains(url, StringComparer.Ordinal))
            {
                list.Add(url);
            }
        }

        return list;
    }

    private void Add(List<RemoteImageInfo> images, IEnumerable<string> urls, ImageType type)
    {
        foreach (var url in urls)
        {
            images.Add(Remote(url, type));
        }
    }

    private async Task<IEnumerable<RemoteImageInfo>> GetPersonImagesAsync(Person person, CancellationToken cancellationToken)
    {
        string? actorId = null;
        if (person.ProviderIds is not null
            && person.ProviderIds.TryGetValue(Plugin.ActorProviderKey, out var stored)
            && !string.IsNullOrWhiteSpace(stored))
        {
            actorId = stored.Trim();
        }

        var actor = await _idolIndex.FindAsync(actorId, person.Name, cancellationToken).ConfigureAwait(false);
        if (actor?.ImageUrl is not { Length: > 0 } portrait)
        {
            _logger.LogDebug("GravureX: no portrait for performer '{Name}'", person.Name);
            return Enumerable.Empty<RemoteImageInfo>();
        }

        return new[] { Remote(portrait, ImageType.Primary) };
    }

    /// <summary>
    /// Builds a remote image entry.
    /// </summary>
    /// <remarks>
    /// Width and Height are deliberately left unset. Jellyfin only applies a
    /// library's minimum width when the provider reports one, and the default
    /// minimum for backdrops is 1280 while the largest gallery image published
    /// here is 800 wide. Reporting the real size would therefore get every
    /// backdrop rejected.
    /// </remarks>
    private static RemoteImageInfo Remote(string url, ImageType type) => new()
    {
        ProviderName = Plugin.DisplayName,
        Url = url,
        Type = type
    };

    /// <summary>
    /// Keeps the front cover, which is the right portion of the package spread.
    /// </summary>
    /// <remarks>
    /// The spread is the back cover, the spine and the front cover side by side.
    /// The spine sits just left of the centre line, so cropping at exactly half
    /// leaves a vertical strip of spine along the left edge; a little past the
    /// middle gives a clean cover.
    /// </remarks>
    private byte[] CropToFrontCover(byte[] source)
    {
        try
        {
            using var image = Image.Load(source);
            var start = (int)(image.Width * FrontCoverStart);

            if (start <= 0 || start >= image.Width)
            {
                return source;
            }

            image.Mutate(context => context.Crop(new Rectangle(start, 0, image.Width - start, image.Height)));

            using var output = new MemoryStream();
            image.SaveAsJpeg(output);
            return output.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GravureX: could not crop the package image, serving it unchanged");
            return source;
        }
    }
}
