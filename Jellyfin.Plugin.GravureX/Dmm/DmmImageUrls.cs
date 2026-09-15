using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.GravureX.Dmm;

/// <summary>
/// URL helpers for DMM artwork.
/// </summary>
/// <remarks>
/// DMM publishes two package sizes for a title:
/// <list type="bullet">
///   <item><c>{id}pl.jpg</c> — the front and back cover side by side, roughly 800x536.</item>
///   <item><c>{id}ps.jpg</c> — the front cover alone, but only about 147x200.</item>
/// </list>
/// There is no large front-cover image, so the poster is produced by cropping the
/// right half out of <c>pl.jpg</c> at download time. A marker in the URL keeps the
/// cropped poster and the uncropped spread apart in the provider's cache.
/// </remarks>
public static partial class DmmImageUrls
{
    /// <summary>Query string marking a URL that should be cropped to the front cover.</summary>
    public const string CoverMarker = "gravurex=cover";

    /// <summary>Marker appended to a package URL to request the cropped front cover.</summary>
    public static string AsCover(string packageUrl) =>
        packageUrl.Contains('?') ? $"{packageUrl}&{CoverMarker}" : $"{packageUrl}?{CoverMarker}";

    public static bool IsCover(string url) => url.Contains(CoverMarker, StringComparison.Ordinal);

    public static string WithoutCoverMarker(string url) =>
        url.Replace($"?{CoverMarker}", string.Empty, StringComparison.Ordinal)
           .Replace($"&{CoverMarker}", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Maps a gallery thumbnail (<c>{id}-3.jpg</c>) to its full size counterpart
    /// (<c>{id}jp-3.jpg</c>). Returns the input unchanged when it is not a gallery URL.
    /// </summary>
    public static string ToLarge(string galleryUrl)
    {
        if (IsLargeGallery(galleryUrl))
        {
            return galleryUrl;
        }

        var match = GalleryRegex().Match(galleryUrl);
        return match.Success ? $"{match.Groups["prefix"].Value}jp-{match.Groups["n"].Value}.jpg" : galleryUrl;
    }

    /// <summary>Maps a full size gallery image back to the thumbnail.</summary>
    public static string ToSmall(string largeUrl)
    {
        var match = LargeRegex().Match(largeUrl);
        return match.Success ? $"{match.Groups["prefix"].Value}-{match.Groups["n"].Value}.jpg" : largeUrl;
    }

    public static bool IsLargeGallery(string url) => LargeRegex().IsMatch(url);

    // The published URL already ends in "-{n}.jpg", so the hyphen is part of the
    // separator rather than of the prefix.
    [GeneratedRegex(@"^(?<prefix>.+/[^/]+?)-(?<n>\d+)\.jpg$", RegexOptions.IgnoreCase)]
    private static partial Regex GalleryRegex();

    [GeneratedRegex(@"^(?<prefix>.+/[^/]+?)jp-(?<n>\d+)\.jpg$", RegexOptions.IgnoreCase)]
    private static partial Regex LargeRegex();
}
