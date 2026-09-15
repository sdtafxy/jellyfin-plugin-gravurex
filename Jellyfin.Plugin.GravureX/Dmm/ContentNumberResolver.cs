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
    private readonly ILogger<ContentNumberResolver> _logger;

    public ContentNumberResolver(DmmClient client, ILogger<ContentNumberResolver> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Returns the content id for <paramref name="contentNumber"/>, or null when unresolved.
    /// </summary>
    public async Task<string?> ResolveAsync(string? contentNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contentNumber))
        {
            return null;
        }

        var searchEnabled = Plugin.Instance?.Configuration.EnableSearchEndpoint ?? true;
        if (!searchEnabled)
        {
            _logger.LogDebug("GravureX: search endpoint disabled, cannot resolve {Number}", contentNumber);
            return null;
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
            return null;
        }

        var candidates = BuildCandidates(contentNumber);
        var matches = items
            .Where(item => candidates.Any(candidate => Normalize(item.ContentId).EndsWith(candidate, StringComparison.Ordinal)))
            .ToList();

        if (matches.Count == 0)
        {
            _logger.LogDebug("GravureX: no search result matched {Number}", contentNumber);
            return null;
        }

        if (Plugin.Instance?.Configuration.PreferStandardEdition ?? true)
        {
            var standard = matches
                .Where(m => !IsSpecialEdition(m.ContentId))
                .OrderBy(m => m.ContentId.Length)
                .ToList();

            if (standard.Count > 0)
            {
                matches = standard;
            }
        }

        var selected = matches.OrderBy(m => m.ContentId.Length).First();
        _logger.LogInformation("GravureX: resolved {Number} to {ContentId}", contentNumber, selected.ContentId);
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

    private static bool IsSpecialEdition(string contentId) =>
        contentId.EndsWith("tk", StringComparison.OrdinalIgnoreCase)
        || contentId.EndsWith("bt", StringComparison.OrdinalIgnoreCase)
        || contentId.EndsWith("btk", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    [GeneratedRegex(@"\b(?<id>[a-z]{1,2}_?\d{2,6}[a-z]{2,8}\d{2,8}[a-z]{0,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex FullContentIdRegex();

    [GeneratedRegex(@"^[a-z]{2,10}\d{2,8}$", RegexOptions.IgnoreCase)]
    private static partial Regex ContentNumberRegex();

    [GeneratedRegex(@"^(?<letters>[a-z]+)(?<digits>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SplitNumberRegex();
}
