using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.GravureX.Dmm;

/// <summary>
/// Works out which content id an item belongs to, and which content number its
/// file name used.
/// </summary>
/// <remarks>
/// The movie provider and the image provider have to agree on the content id, so
/// the lookup lives here rather than being written out twice.
/// <para>
/// A stored provider id saves the search, but it must never stand in for the
/// content number: the number that goes in front of a title is always read from
/// the file name. Returning early on a stored id used to hand back a null number
/// and strip the prefix off titles that already had one.
/// </para>
/// </remarks>
public sealed class ItemContentIdResolver
{
    private readonly ContentNumberResolver _resolver;
    private readonly ILogger<ItemContentIdResolver> _logger;

    public ItemContentIdResolver(ContentNumberResolver resolver, ILogger<ItemContentIdResolver> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>
    /// The content id an earlier lookup stored on the item, if there is one.
    /// </summary>
    public static string? FindStoredId(IReadOnlyDictionary<string, string>? providerIds)
    {
        if (providerIds is null)
        {
            return null;
        }

        foreach (var key in Plugin.ContentIdKeys)
        {
            if (providerIds.TryGetValue(key, out var stored) && !string.IsNullOrWhiteSpace(stored))
            {
                return stored.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// The content id an item resolves to, and the content number its names carried.
    /// </summary>
    public async Task<ContentIdLookup> ResolveAsync(
        IReadOnlyDictionary<string, string>? providerIds,
        IEnumerable<string?> names,
        CancellationToken cancellationToken)
    {
        var candidate = ReadCandidate(names);

        if (FindStoredId(providerIds) is { } stored)
        {
            _logger.LogDebug(
                "GravureX: using stored provider id {Id}, file name number {Number}",
                stored,
                candidate?.Number ?? "(none)");
            return new ContentIdLookup(stored, candidate?.Number);
        }

        if (candidate is not { } found)
        {
            return ContentIdLookup.None;
        }

        var (token, number, isFullContentId) = found;

        // A full content id is not the number the file was named with, so it is
        // not used as a prefix.
        if (isFullContentId)
        {
            return new ContentIdLookup(token, null);
        }

        var resolved = await _resolver.ResolveAsync(token, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(resolved) ? ContentIdLookup.None : new ContentIdLookup(resolved, number);
    }

    /// <summary>
    /// The names a content number may hide in, in the order they are trusted: the
    /// item name first, then the file name, then the folder above it.
    /// </summary>
    public static IEnumerable<string?> NamesFrom(string? name, string? path)
    {
        yield return name;

        if (string.IsNullOrEmpty(path))
        {
            yield break;
        }

        yield return Path.GetFileNameWithoutExtension(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            yield return Path.GetFileName(directory);
        }
    }

    /// <summary>
    /// Reads the first usable content number or content id out of the item's names.
    /// </summary>
    public static (string Token, string? Number, bool IsFullContentId)? ReadCandidate(IEnumerable<string?> names)
    {
        foreach (var source in names)
        {
            var extracted = ContentNumberResolver.Extract(source);
            if (string.IsNullOrEmpty(extracted))
            {
                continue;
            }

            var isFullContentId = ContentNumberResolver.IsFullContentId(extracted);

            // A full content id is not the number the file was named with, so it is
            // not put in front of the title.
            return (extracted, isFullContentId ? null : extracted, isFullContentId);
        }

        return null;
    }
}

/// <summary>
/// The content id an item resolved to, and the content number its names carried.
/// </summary>
public readonly record struct ContentIdLookup(string? ContentId, string? ContentNumber)
{
    /// <summary>Nothing resolved.</summary>
    public static ContentIdLookup None => default;
}
