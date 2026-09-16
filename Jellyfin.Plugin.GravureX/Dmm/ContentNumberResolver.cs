using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.GravureX.Dmm;

/// <summary>
/// Turns a content number found in a file name into a DMM content id.
/// </summary>
/// <remarks>
/// DMM content ids embed a maker specific prefix, so a content number cannot be
/// turned into a detail URL directly. Search results are matched back against the
/// number instead, tolerating both the hyphen positions people use in file names
/// and the zero padding DMM applies per maker.
/// </remarks>
public sealed partial class ContentNumberResolver
{
    private readonly DmmClient _client;
    private readonly ConfigurationAccessor _configuration;
    private readonly ILogger<ContentNumberResolver> _logger;

    public ContentNumberResolver(DmmClient client, ConfigurationAccessor configuration, ILogger<ContentNumberResolver> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Every edition of a content number that the search turns up, in the order
    /// the listing returned them.
    /// </summary>
    /// <remarks>
    /// One number maps to several products: a plain DVD, a Blu-ray, and limited or
    /// bonus editions that add a photo set. Callers decide which of them to use —
    /// see <see cref="PickStandard"/>.
    /// </remarks>
    public async Task<IReadOnlyList<DmmSearchItem>> FindEditionsAsync(string? contentNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contentNumber))
        {
            return Array.Empty<DmmSearchItem>();
        }

        if (!(_configuration.Current?.EnableSearchEndpoint ?? true))
        {
            _logger.LogDebug("GravureX: search endpoint disabled, cannot resolve {Number}", contentNumber);
            return Array.Empty<DmmSearchItem>();
        }

        // DMM only recognises the hyphen in its own position, so a file name such as
        // AB-1234567 has to be searched without separators.
        var query = Normalize(contentNumber);
        var items = await _client.SearchAsync(query, cancellationToken).ConfigureAwait(false);

        if (items.Count == 0 && !string.Equals(query, contentNumber, StringComparison.Ordinal))
        {
            items = await _client.SearchAsync(contentNumber, cancellationToken).ConfigureAwait(false);
        }

        if (items.Count == 0)
        {
            return Array.Empty<DmmSearchItem>();
        }

        var candidates = BuildCandidates(contentNumber);
        var matches = items
            .Where(item => MatchesNumber(item.ContentId, candidates))
            .ToList();

        if (matches.Count == 0)
        {
            _logger.LogDebug("GravureX: no search result matched {Number}", contentNumber);
            return Array.Empty<DmmSearchItem>();
        }

        _logger.LogDebug(
            "GravureX: {Number} matched {Count} edition(s): {Editions}",
            contentNumber,
            matches.Count,
            string.Join(", ", matches.Select(m => $"{m.ContentId} rank {m.EditionRank}")));

        return matches;
    }

    /// <summary>
    /// The plainest edition of those found: the plain DVD first, then a plain
    /// Blu-ray, then a limited or bonus disc. Ties keep the listing order, so the
    /// source's own ranking decides.
    /// </summary>
    public static DmmSearchItem PickStandard(IReadOnlyList<DmmSearchItem> editions) =>
        editions.OrderBy(edition => edition.EditionRank).First();

    /// <summary>
    /// Resolves a content number to a single content id, for an automatic scan.
    /// </summary>
    /// <remarks>
    /// An automatic scan has no way to ask which edition is wanted, so it always
    /// takes the plainest one — the plain DVD, failing that a plain Blu-ray, and
    /// only a limited or bonus edition when nothing plainer exists. The
    /// <see cref="PluginConfiguration.PreferStandardEdition"/> setting therefore
    /// governs the manual search list rather than this.
    /// </remarks>
    public async Task<string?> ResolveAsync(string? contentNumber, CancellationToken cancellationToken)
    {
        var editions = await FindEditionsAsync(contentNumber, cancellationToken).ConfigureAwait(false);
        if (editions.Count == 0)
        {
            return null;
        }

        var selected = PickStandard(editions);

        _logger.LogInformation(
            "GravureX: resolved {Number} to {ContentId} out of {Count} edition(s)",
            contentNumber,
            selected.ContentId,
            editions.Count);

        return selected.ContentId;
    }

    /// <summary>
    /// Extracts a content number or a full DMM content id from a file name or title.
    /// </summary>
    public static string? Extract(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var candidate = Path.GetFileNameWithoutExtension(name.Trim());
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        // A full DMM content id, e.g. n_1234abcd5678.
        var fullId = FullContentIdRegex().Match(candidate);
        if (fullId.Success)
        {
            return fullId.Value;
        }

        // A content number. Hyphens may sit anywhere inside it, so the token is
        // normalised before matching: AB-1234567 and ab1234567 are the same number.
        foreach (var token in Tokenize(candidate))
        {
            if (ContentNumberRegex().IsMatch(Normalize(token)))
            {
                return token;
            }
        }

        return null;
    }

    /// <summary>
    /// True when the given value already looks like a complete DMM content id.
    /// </summary>
    public static bool IsFullContentId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && FullContentIdRegex().IsMatch(value);

    private static IEnumerable<string> Tokenize(string value)
    {
        var buffer = new List<char>();
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer.Add(c);
                continue;
            }

            if (c is '-' or '_' && buffer.Count > 0)
            {
                buffer.Add(c);
                continue;
            }

            if (buffer.Count > 0)
            {
                yield return new string(buffer.ToArray());
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            yield return new string(buffer.ToArray());
        }
    }

    /// <summary>
    /// True when a content id carries the given number.
    /// </summary>
    /// <remarks>
    /// A limited or bonus edition is the same number with a short marker appended
    /// to the whole id — <c>n_1234abcd5678tk</c> for <c>abcd5678</c> — so the marker
    /// has to come off before the id can be compared. Without this the bonus
    /// editions were never matched at all, and only ever one edition was found.
    /// </remarks>
    private static bool MatchesNumber(string contentId, IReadOnlyList<string> candidates)
    {
        var normalized = BonusSuffixRegex().Replace(Normalize(contentId), string.Empty);
        return candidates.Any(candidate => normalized.EndsWith(candidate, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> BuildCandidates(string contentNumber)
    {
        var normalized = Normalize(contentNumber);
        var match = SplitNumberRegex().Match(normalized);
        if (!match.Success)
        {
            return new[] { normalized };
        }

        var letters = match.Groups["letters"].Value;
        var digits = match.Groups["digits"].Value;

        // Makers zero pad the numeric part to their own width, e.g. 556 vs 0556.
        var candidates = new List<string> { normalized };
        for (var width = digits.Length; width <= 6; width++)
        {
            candidates.Add(letters + digits.PadLeft(width, '0'));
        }

        return candidates.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string Normalize(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    [GeneratedRegex(@"\b(?<id>[a-z]{1,2}_?\d{2,6}[a-z]{2,8}\d{2,8}[a-z]{0,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex FullContentIdRegex();

    [GeneratedRegex(@"^[a-z]{2,10}\d{2,8}$", RegexOptions.IgnoreCase)]
    private static partial Regex ContentNumberRegex();

    // A bonus edition appends a short marker to the whole content id.
    [GeneratedRegex(@"(?:tk|btk|bt)$", RegexOptions.IgnoreCase)]
    private static partial Regex BonusSuffixRegex();

    [GeneratedRegex(@"^(?<letters>[a-z]+)(?<digits>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SplitNumberRegex();
}
