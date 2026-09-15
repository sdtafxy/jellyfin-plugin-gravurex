using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Jellyfin.Plugin.GravureX.Dmm;

/// <summary>
/// Extracts structured data from DMM mono (DVD / Blu-ray) pages.
/// </summary>
public static partial class DmmParser
{
    private static readonly string[] DateFormats = { "yyyy/MM/dd", "yyyy/M/dd", "yyyy/MM/d", "yyyy/M/d", "yyyy/MM", "yyyy" };

    /// <summary>
    /// Parses a search or listing page into individual results.
    /// </summary>
    public static IReadOnlyList<DmmSearchItem> ParseSearchResults(string html)
    {
        var results = new List<DmmSearchItem>();
        if (string.IsNullOrWhiteSpace(html))
        {
            return results;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var anchors = doc.DocumentNode.SelectNodes("//a[contains(@href,'/mono/dvd/-/detail/=/cid=')]");
        if (anchors is null)
        {
            return results;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchor in anchors)
        {
            var contentId = ExtractContentId(anchor.GetAttributeValue("href", string.Empty));
            if (string.IsNullOrEmpty(contentId) || !seen.Add(contentId))
            {
                continue;
            }

            var imageNode = anchor.SelectSingleNode(".//img");
            var titleNode = anchor.SelectSingleNode(".//span[contains(@class,'txt')]");

            var item = new DmmSearchItem
            {
                ContentId = contentId,
                Title = HtmlEntity.DeEntitize(titleNode?.InnerText ?? anchor.GetAttributeValue("title", string.Empty)).Trim(),
                ImageUrl = ToLargePackageImage(NormalizeImageUrl(
                    imageNode?.GetAttributeValue("src", null) ?? imageNode?.GetAttributeValue("data-lazy", null))),
                ReleaseDate = ParseDate(ParseListingDate(anchor.SelectSingleNode("ancestor::li[1]") ?? anchor.ParentNode))
            };

            results.Add(item);
        }

        return results;
    }

    /// <summary>
    /// Parses a product detail page.
    /// </summary>
    public static DmmTitle? ParseDetail(string html, string? fallbackContentId = null)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        var title = new DmmTitle();

        ApplyJsonLd(root, title);

        var metaTitle = GetMetaContent(root, "og:title");
        if (string.IsNullOrEmpty(title.Title) && !string.IsNullOrEmpty(metaTitle))
        {
            title.Title = metaTitle;
        }

        var metaImage = GetMetaContent(root, "og:image");
        if (string.IsNullOrEmpty(title.PackageImageUrl) && !string.IsNullOrEmpty(metaImage))
        {
            title.PackageImageUrl = NormalizeImageUrl(metaImage);
        }

        ApplyInfoTable(root, title);
        ApplyGallery(html, root, title);

        if (string.IsNullOrEmpty(title.ContentId))
        {
            var canonical = root.SelectSingleNode("//link[@rel='canonical']")?.GetAttributeValue("href", string.Empty) ?? string.Empty;
            if (ExtractContentId(canonical) is { } canonicalId)
            {
                title.ContentId = canonicalId;
            }
        }

        if (string.IsNullOrEmpty(title.ContentId) && !string.IsNullOrEmpty(fallbackContentId))
        {
            title.ContentId = fallbackContentId;
        }

        if (string.IsNullOrEmpty(title.Title))
        {
            return null;
        }

        title.CleanTitle = StripPerformerSuffix(title.Title, title.Actors);

        return title;
    }

    /// <summary>
    /// Parses a gravure idol index page into performer entries.
    /// </summary>
    /// <remarks>
    /// The index lists every performer as a plain name link, and additionally
    /// renders portraits for a small "recommended" subset. Entries without a
    /// portrait are still returned, because the name to id mapping is useful on
    /// its own.
    /// </remarks>
    public static IReadOnlyList<DmmActor> ParseIdolIndex(string html)
    {
        var actors = new List<DmmActor>();
        if (string.IsNullOrWhiteSpace(html))
        {
            return actors;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var anchors = doc.DocumentNode.SelectNodes("//a[contains(@href,'article=actor')]");
        if (anchors is null)
        {
            return actors;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var anchor in anchors)
        {
            var id = ExtractActorId(anchor.GetAttributeValue("href", string.Empty));
            var name = HtmlEntity.DeEntitize(anchor.InnerText).Trim();
            if (string.IsNullOrEmpty(id) || name.Length == 0 || !seen.Add(id))
            {
                continue;
            }

            var imageNode = anchor.SelectSingleNode(".//img");
            var image = NormalizeImageUrl(imageNode?.GetAttributeValue("src", null));

            actors.Add(new DmmActor
            {
                Id = id,
                Name = name,
                ImageUrl = IsPlaceholderImage(image) ? null : image
            });
        }

        return actors;
    }

    /// <summary>
    /// Reads the performer name and kana reading from an actor listing page.
    /// </summary>
    public static DmmActor? ParseActorPage(string html, string? fallbackId = null)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var match = ActorTitleRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }

        return new DmmActor
        {
            Name = HtmlEntity.DeEntitize(match.Groups["name"].Value).Trim(),
            Reading = HtmlEntity.DeEntitize(match.Groups["reading"].Value).Trim(),
            Id = fallbackId ?? string.Empty
        };
    }

    /// <summary>
    /// True when the URL points at DMM's shared "no portrait" placeholder.
    /// DMM serves it with HTTP 200, so it has to be filtered by URL.
    /// </summary>
    private static bool IsPlaceholderImage(string? url) =>
        string.IsNullOrEmpty(url) || url.Contains("printing", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts the DMM content id from a detail URL.
    /// </summary>
    public static string? ExtractContentId(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        var match = ContentIdRegex().Match(url);
        return match.Success ? match.Groups["cid"].Value : null;
    }

    /// <summary>
    /// Extracts the DMM actor id from an actor listing URL.
    /// </summary>
    public static string? ExtractActorId(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        var match = ActorIdRegex().Match(url);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static void ApplyJsonLd(HtmlNode root, DmmTitle title)
    {
        var scripts = root.SelectNodes("//script[@type='application/ld+json']");
        if (scripts is null)
        {
            return;
        }

        foreach (var script in scripts)
        {
            if (!script.ChildNodes.Any(n => n.Name == "#text"))
            {
                continue;
            }

            using var document = TryParseJson(script.InnerText);
            if (document is null)
            {
                continue;
            }

            var element = document.RootElement;
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryGetString(element, "sku") is { Length: > 0 } sku)
            {
                title.ContentId = sku;
            }

            if (TryGetString(element, "name") is { Length: > 0 } name)
            {
                title.Title = name.Trim();
            }

            if (TryGetString(element, "description") is { Length: > 0 } description)
            {
                title.Overview = description.Trim();
            }

            if (TryGetString(element, "gtin13") is { Length: > 0 } jan)
            {
                title.Jan = jan;
            }

            if (element.TryGetProperty("brand", out var brand)
                && brand.ValueKind == JsonValueKind.Object
                && TryGetString(brand, "name") is { Length: > 0 } brandName)
            {
                title.Maker = brandName.Trim();
            }

            if (element.TryGetProperty("image", out var image))
            {
                if (image.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in image.EnumerateArray())
                    {
                        if (entry.ValueKind == JsonValueKind.String)
                        {
                            var url = NormalizeImageUrl(entry.GetString());
                            if (url is not null)
                            {
                                title.PackageImageUrl = url;
                                break;
                            }
                        }
                    }
                }
                else if (image.ValueKind == JsonValueKind.String
                    && NormalizeImageUrl(image.GetString()) is { } imageUrl)
                {
                    title.PackageImageUrl = imageUrl;
                }
            }

            if (element.TryGetProperty("aggregateRating", out var rating) && rating.ValueKind == JsonValueKind.Object)
            {
                if (rating.TryGetProperty("ratingValue", out var value))
                {
                    if (value.ValueKind == JsonValueKind.String && float.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedString))
                    {
                        title.CommunityRating = parsedString;
                    }
                    else if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var parsedNumber))
                    {
                        title.CommunityRating = parsedNumber;
                    }
                }

            }
        }
    }

    private static void ApplyInfoTable(HtmlNode root, DmmTitle title)
    {
        var rows = root.SelectNodes("//table//tr");
        if (rows is null)
        {
            return;
        }

        foreach (var row in rows)
        {
            var cells = row.SelectNodes("td");
            if (cells is null || cells.Count < 2)
            {
                continue;
            }

            var label = HtmlEntity.DeEntitize(cells[0].InnerText).Trim().TrimEnd('：', ':').Trim();
            var cell = cells[1];

            switch (label)
            {
                case "発売日":
                    title.ReleaseDate = ParseDate(HtmlEntity.DeEntitize(cell.InnerText).Trim());
                    break;

                case "収録時間":
                    title.RuntimeMinutes = ParseRuntime(HtmlEntity.DeEntitize(cell.InnerText));
                    break;

                case "出演者":
                    title.Actors = ParseActors(cell);
                    break;

                case "監督":
                    title.Director = NormalizePlaceholder(HtmlEntity.DeEntitize(cell.InnerText).Trim());
                    break;

                case "メディア":
                    // Published on some pages only, so the media type tag is best
                    // effort. The value is like "DVD" or "Blu-ray".
                    title.Media = NormalizePlaceholder(HtmlEntity.DeEntitize(cell.InnerText).Trim().Split(' ', '\t', '\n', '\r')[0]);
                    break;

                case "シリーズ":
                    title.Series = NormalizePlaceholder(HtmlEntity.DeEntitize(cell.InnerText).Trim());
                    break;

                case "メーカー":
                    var maker = NormalizePlaceholder(HtmlEntity.DeEntitize(cell.InnerText).Trim());
                    if (!string.IsNullOrEmpty(maker))
                    {
                        title.Maker = maker;
                    }

                    break;

                case "ジャンル":
                    title.Genres = (cell.SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>())
                        .Select(a => HtmlEntity.DeEntitize(a.InnerText).Trim())
                        .Where(g => g.Length > 0)
                        .Distinct()
                        .ToList();
                    break;

                case "品番":
                    // A fallback only. The printed product code is not guaranteed to
                    // be the content id the page was fetched with, and that id is
                    // what the JSON-LD block reports.
                    if (string.IsNullOrEmpty(title.ContentId))
                    {
                        title.ContentId = HtmlEntity.DeEntitize(cell.InnerText).Trim();
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Collects the preview images for a title.
    /// </summary>
    /// <remarks>
    /// Two passes are made on purpose. The markup that wraps the previews has not
    /// been stable between titles, and one title returned none at all from the
    /// container based pass, so the URLs are also scraped straight out of the
    /// response text: they always live under <c>/digital/video/{id}/{id}-{n}.jpg</c>
    /// regardless of how the page chooses to present them.
    /// </remarks>
    private static void ApplyGallery(string html, HtmlNode root, DmmTitle title)
    {
        var primary = title.PackageImageUrl;
        var primaryName = primary is null ? null : Path.GetFileName(new Uri(primary).LocalPath);

        var images = new List<string>();
        var nodes = root.SelectNodes("//li[contains(@class,'layout-sampleImage__item')]//img");
        if (nodes is not null)
        {
            foreach (var node in nodes)
            {
                var url = NormalizeImageUrl(node.GetAttributeValue("data-lazy", null) ?? node.GetAttributeValue("src", null));
                if (url is null)
                {
                    continue;
                }

                // The gallery exposes the small package image; upgrade it to the large one.
                url = ToLargePackageImage(url)!;

                if (primaryName is not null && Path.GetFileName(new Uri(url).LocalPath)
                        .Equals(primaryName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                images.Add(url);
            }
        }

        foreach (Match match in PreviewRegex().Matches(html))
        {
            // The full size twin of a preview carries "jp" before the number; the
            // pipeline derives it, so only the published thumbnail is kept here.
            if (match.Groups["base"].Value.EndsWith("jp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (NormalizeImageUrl(match.Groups["url"].Value) is { } preview)
            {
                images.Add(preview);
            }
        }

        title.GalleryImageUrls = images
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(PreviewOrder)
            .ThenBy(url => url, StringComparer.OrdinalIgnoreCase)
            .ToList();

        title.PageLength = html.Length;
    }

    /// <summary>
    /// Sorts preview images by their trailing number, so the page order does not
    /// matter once several sources have been merged.
    /// </summary>
    private static int PreviewOrder(string url)
    {
        var match = PreviewRegex().Match(url);
        return match.Success && int.TryParse(match.Groups["n"].Value, out var number) ? number : int.MaxValue;
    }

    private static IReadOnlyList<DmmPerson> ParseActors(HtmlNode cell)
    {
        var actors = new List<DmmPerson>();
        var links = cell.SelectNodes(".//a[contains(@href,'article=actor')]");
        if (links is not null)
        {
            foreach (var link in links)
            {
                var name = HtmlEntity.DeEntitize(link.InnerText).Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                actors.Add(new DmmPerson
                {
                    Name = name,
                    ActorId = ExtractActorId(link.GetAttributeValue("href", string.Empty))
                });
            }
        }
        else
        {
            var text = NormalizePlaceholder(HtmlEntity.DeEntitize(cell.InnerText).Trim());
            if (!string.IsNullOrEmpty(text))
            {
                actors.Add(new DmmPerson { Name = text });
            }
        }

        return actors;
    }

    private static string StripPerformerSuffix(string title, IReadOnlyList<DmmPerson> actors)
    {
        if (actors.Count == 0)
        {
            return title;
        }

        // Titles separate the performer with either a half width or a full width
        // solidus, and both appear in the wild.
        var separator = title.LastIndexOfAny(new[] { '/', '\uFF0F' });
        if (separator <= 0)
        {
            return title;
        }

        var suffix = title[(separator + 1)..].Trim();
        if (actors.Any(a => string.Equals(a.Name, suffix, StringComparison.Ordinal)))
        {
            return title[..separator].Trim();
        }

        return title;
    }

    private static int? ParseRuntime(string value)
    {
        var match = Regex.Match(value, @"(\d+)\s*分");
        return match.Success && int.TryParse(match.Groups[1].Value, out var minutes) ? minutes : null;
    }

    private static DateTime? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-'))
        {
            return null;
        }

        return DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static string? NormalizePlaceholder(string value)
    {
        var trimmed = value.Replace("&nbsp;", " ", StringComparison.Ordinal).Trim();
        return trimmed.Length == 0 || trimmed.StartsWith("----", StringComparison.Ordinal) ? null : trimmed;
    }

    /// <summary>
    /// Upgrades a package thumbnail to the package spread published under the same
    /// name: the listing and the gallery only ever expose the small one.
    /// </summary>
    private static string? ToLargePackageImage(string? url) =>
        url is not null && url.EndsWith("ps.jpg", StringComparison.OrdinalIgnoreCase)
            ? string.Concat(url.AsSpan(0, url.Length - "ps.jpg".Length), "pl.jpg")
            : url;

    /// <summary>
    /// Reads the release date a listing entry prints as <c>発売日：yyyy/MM/dd</c>.
    /// </summary>
    private static string? ParseListingDate(HtmlNode? container)
    {
        if (container is null)
        {
            return null;
        }

        var match = ListingDateRegex().Match(HtmlEntity.DeEntitize(container.InnerText));
        return match.Success ? match.Groups["date"].Value : null;
    }

    private static string? NormalizeImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + url;
        }

        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : null;
    }

    private static string? GetMetaContent(HtmlNode root, string property)
    {
        var node = root.SelectSingleNode($"//meta[@property='{property}']") ?? root.SelectSingleNode($"//meta[@name='{property}']");
        return node?.GetAttributeValue("content", null);
    }

    private static string? TryGetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static JsonDocument? TryParseJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\[(?<name>[^\(\[]+?)\((?<reading>[^\)]+)\)\]", RegexOptions.None)]
    private static partial Regex ActorTitleRegex();

    // Preview images always live under /digital/video/{id}/ and end in "-{n}.jpg".
    [GeneratedRegex(@"(?<url>(?:https?:)?//pics\.dmm\.com/digital/video/[^/'""\s]+/(?<base>[^/'""\s-]+)-(?<n>\d+)\.jpg)", RegexOptions.IgnoreCase)]
    private static partial Regex PreviewRegex();

    // A listing entry prints its release date as 発売日：yyyy/MM/dd.
    [GeneratedRegex(@"発売日[：:]\s*(?<date>\d{4}/\d{1,2}/\d{1,2})", RegexOptions.None)]
    private static partial Regex ListingDateRegex();

    [GeneratedRegex(@"cid=(?<cid>[A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ContentIdRegex();

    [GeneratedRegex(@"article=actor/id=(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ActorIdRegex();
}
