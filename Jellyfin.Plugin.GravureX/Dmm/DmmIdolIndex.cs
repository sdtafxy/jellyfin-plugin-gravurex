using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.GravureX.Dmm;

/// <summary>
/// Builds and caches the DMM gravure idol index, which is the only place DMM
/// publishes performer portraits.
/// </summary>
public sealed class DmmIdolIndex
{
    private const string IndexUrl = "https://www.dmm.com/mono/dvd/-/idol/=/keyword={0}/";

    /// <summary>
    /// The gravure idol index is split by the first kana of the reading.
    /// </summary>
    private static readonly string[] KanaPages =
    {
        "a", "i", "u", "e", "o",
        "ka", "sa", "ta", "na", "ha", "ma", "ya", "ra", "wa"
    };

    private readonly DmmClient _client;
    private readonly ILogger<DmmIdolIndex> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Dictionary<string, DmmActor>? _byId;
    private Dictionary<string, DmmActor>? _byName;
    private DateTime _builtAt;

    public DmmIdolIndex(DmmClient client, ILogger<DmmIdolIndex> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Looks a performer up by DMM actor id, falling back to an exact name match.
    /// </summary>
    public async Task<DmmActor?> FindAsync(string? actorId, string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorId) && string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (!IsEnabled())
        {
            return null;
        }

        var byId = await GetIndexAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(actorId) && byId.TryGetValue(actorId.Trim(), out var byIdMatch))
        {
            return byIdMatch;
        }

        var byName = _byName;
        if (byName is null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return byName.TryGetValue(name.Trim(), out var byNameMatch) ? byNameMatch : null;
    }

    private async Task<Dictionary<string, DmmActor>> GetIndexAsync(CancellationToken cancellationToken)
    {
        if (_byId is not null && DateTime.UtcNow - _builtAt < CacheWindow())
        {
            return _byId;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_byId is not null && DateTime.UtcNow - _builtAt < CacheWindow())
            {
                return _byId;
            }

            var byId = new Dictionary<string, DmmActor>(StringComparer.Ordinal);
            var byName = new Dictionary<string, DmmActor>(StringComparer.Ordinal);

            foreach (var kana in KanaPages)
            {
                var url = string.Format(System.Globalization.CultureInfo.InvariantCulture, IndexUrl, kana);
                var html = await _client.GetHtmlAsync(url, cancellationToken).ConfigureAwait(false);
                if (html is null)
                {
                    continue;
                }

                foreach (var actor in DmmParser.ParseIdolIndex(html))
                {
                    // Later pages should not downgrade an entry that already has a portrait.
                    if (byId.TryGetValue(actor.Id, out var existing) && existing.ImageUrl is not null)
                    {
                        if (actor.ImageUrl is not null)
                        {
                            existing.ImageUrl = actor.ImageUrl;
                        }

                        continue;
                    }

                    byId[actor.Id] = actor;
                    byName.TryAdd(actor.Name, actor);
                }
            }

            _byId = byId;
            _byName = byName;
            _builtAt = DateTime.UtcNow;

            _logger.LogInformation(
                "GravureX: idol index built with {Count} performers, {WithImage} with a portrait",
                byId.Count,
                byId.Values.Count(a => a.ImageUrl is not null));
        }
        finally
        {
            _gate.Release();
        }

        return _byId;
    }

    private static bool IsEnabled() => Plugin.Instance?.Configuration.EnableActorImages ?? true;

    private static TimeSpan CacheWindow()
    {
        var days = Plugin.Instance?.Configuration.ActorIndexCacheDays ?? 7;
        return TimeSpan.FromDays(Math.Clamp(days, 0, 30));
    }
}
