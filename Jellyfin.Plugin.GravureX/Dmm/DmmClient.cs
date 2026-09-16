using Jellyfin.Plugin.GravureX.Configuration;
using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.GravureX.Dmm;

/// <summary>
/// Thin HTTP client for DMM mono pages: rate limited, cached and proxy aware.
/// </summary>
public sealed class DmmClient : IDisposable
{
    private const string BaseUrl = "https://www.dmm.com";

    /// <summary>
    /// Upper bound on cached pages. A long running server would otherwise keep
    /// every page it ever fetched.
    /// </summary>
    private const int MaxCachedPages = 256;

    private readonly ILogger<DmmClient> _logger;
    private readonly ConfigurationAccessor _configuration;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    private HttpClient? _httpClient;
    private string? _configuredProxy;
    private string? _configuredUserAgent;
    private long _nextRequestTicks;
    private bool _disposed;

    public DmmClient(ILogger<DmmClient> logger)
        : this(logger, new ConfigurationAccessor())
    {
    }

    public DmmClient(ILogger<DmmClient> logger, ConfigurationAccessor configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Fetches and parses a product detail page.
    /// </summary>
    public async Task<DmmTitle?> GetTitleAsync(string contentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return null;
        }

        var url = $"{BaseUrl}/mono/dvd/-/detail/=/cid={Uri.EscapeDataString(contentId)}/";
        var html = await GetHtmlAsync(url, cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return null;
        }

        var title = DmmParser.ParseDetail(html, contentId);
        if (title is null)
        {
            _logger.LogWarning("GravureX: no parsable metadata on {Url}", url);
        }

        return title;
    }

    /// <summary>
    /// Runs a keyword search and returns the raw result entries.
    /// </summary>
    public async Task<IReadOnlyList<DmmSearchItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<DmmSearchItem>();
        }

        var url = $"{BaseUrl}/mono/dvd/-/search/=/searchstr={Uri.EscapeDataString(query)}/";
        var html = await GetHtmlAsync(url, cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return Array.Empty<DmmSearchItem>();
        }

        var items = DmmParser.ParseSearchResults(html);
        _logger.LogInformation("GravureX: search '{Query}' returned {Count} result(s)", query, items.Count);
        return items;
    }

    /// <summary>
    /// Fetches an actor listing page to read the canonical name and kana reading.
    /// </summary>
    public async Task<DmmActor?> GetActorAsync(string actorId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return null;
        }

        var url = $"{BaseUrl}/mono/dvd/-/list/=/article=actor/id={Uri.EscapeDataString(actorId)}/";
        var html = await GetHtmlAsync(url, cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return null;
        }

        var actor = DmmParser.ParseActorPage(html, actorId);
        if (actor is null)
        {
            _logger.LogWarning("GravureX: no parsable actor on {Url}", url);
        }

        return actor;
    }

    /// <summary>
    /// Downloads a page as text with caching and rate limiting applied.
    /// </summary>
    public async Task<string?> GetHtmlAsync(string url, CancellationToken cancellationToken)
    {
        var configuration = _configuration.Current;
        var cacheMinutes = configuration?.CacheDurationMinutes ?? PluginConfiguration.DefaultCacheDurationMinutes;

        if (cacheMinutes > 0 && _cache.TryGetValue(url, out var cached))
        {
            if (DateTime.UtcNow - cached.CreatedAt < TimeSpan.FromMinutes(cacheMinutes))
            {
                _logger.LogDebug("GravureX: cache hit {Url}", url);
                return cached.Html;
            }

            _cache.TryRemove(url, out _);
        }

        var client = GetClient(configuration);
        var html = await FetchWithRateLimitAsync(client, url, configuration?.EffectiveRequestIntervalMs ?? PluginConfiguration.DefaultRequestIntervalMs, cancellationToken)
            .ConfigureAwait(false);

        if (html is not null && cacheMinutes > 0)
        {
            if (_cache.Count >= MaxCachedPages)
            {
                EvictOldest();
            }

            _cache[url] = new CacheEntry(html, DateTime.UtcNow);
        }

        return html;
    }

    /// <summary>
    /// Downloads a binary resource such as an image.
    /// </summary>
    /// <remarks>
    /// The final URL is reported alongside the bytes because DMM answers a request
    /// for a missing image with a redirect to a shared placeholder and an HTTP 200,
    /// so the status code alone says nothing about whether the image exists.
    /// </remarks>
    public async Task<DmmImage?> GetImageAsync(string url, CancellationToken cancellationToken)
    {
        var configuration = _configuration.Current;
        var client = GetClient(configuration);

        await ThrottleAsync(configuration?.EffectiveRequestIntervalMs ?? PluginConfiguration.DefaultRequestIntervalMs, cancellationToken).ConfigureAwait(false);
        try
        {
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GravureX: {StatusCode} for {Url}", response.StatusCode, url);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
            return new DmmImage(bytes, finalUrl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GravureX: failed to download {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Clears the in-memory page cache.
    /// </summary>
    public void ClearCache() => _cache.Clear();

    /// <summary>
    /// Drops the oldest quarter of the cache once it is full, so it stays bounded
    /// without a separate expiry sweep.
    /// </summary>
    private void EvictOldest()
    {
        var stale = _cache
            .OrderBy(pair => pair.Value.CreatedAt)
            .Take((_cache.Count / 4) + 1)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in stale)
        {
            _cache.TryRemove(key, out _);
        }
    }

    private async Task<string?> FetchWithRateLimitAsync(HttpClient client, string url, int intervalMs, CancellationToken cancellationToken)
    {
        await ThrottleAsync(intervalMs, cancellationToken).ConfigureAwait(false);

        try
        {
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GravureX: {StatusCode} for {Url}", response.StatusCode, url);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GravureX: failed to fetch {Url}", url);
            return null;
        }
    }

    private async Task ThrottleAsync(int intervalMs, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = _nextRequestTicks - DateTime.UtcNow.Ticks;
            if (wait > 0)
            {
                await Task.Delay(TimeSpan.FromTicks(wait), cancellationToken).ConfigureAwait(false);
            }

            _nextRequestTicks = DateTime.UtcNow.Ticks + TimeSpan.FromMilliseconds(Math.Max(0, intervalMs)).Ticks;
        }
        finally
        {
            _gate.Release();
        }
    }

    private HttpClient GetClient(Configuration.PluginConfiguration? configuration)
    {
        var proxy = configuration?.ProxyUrl?.Trim();
        var userAgent = string.IsNullOrWhiteSpace(configuration?.UserAgent)
            ? DefaultUserAgent
            : configuration!.UserAgent.Trim();

        if (_httpClient is not null
            && string.Equals(_configuredProxy, proxy, StringComparison.Ordinal)
            && string.Equals(_configuredUserAgent, userAgent, StringComparison.Ordinal))
        {
            return _httpClient;
        }

        _httpClient?.Dispose();

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        if (!string.IsNullOrEmpty(proxy) && Uri.TryCreate(proxy, UriKind.Absolute, out var proxyUri))
        {
            handler.Proxy = new System.Net.WebProxy(proxyUri);
            handler.UseProxy = true;
            _logger.LogInformation("GravureX: routing requests through {Proxy}", proxyUri);
        }

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(configuration?.EffectiveRequestTimeoutSeconds ?? PluginConfiguration.DefaultRequestTimeoutSeconds, 5, 120))
        };

        client.DefaultRequestHeaders.Add("User-Agent", userAgent);
        client.DefaultRequestHeaders.Add("Accept-Language", "ja,en-US;q=0.9,en;q=0.8");
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

        _httpClient = client;
        _configuredProxy = proxy;
        _configuredUserAgent = userAgent;

        // A new client means the proxy or the user agent changed, and pages fetched
        // through the old one should not be handed out any more.
        _cache.Clear();

        return client;
    }

    private const string DefaultUserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        _httpClient?.Dispose();
    }

    private sealed record CacheEntry(string Html, DateTime CreatedAt);
}

/// <summary>
/// A downloaded image together with the URL it actually resolved to.
/// </summary>
public sealed record DmmImage(byte[] Bytes, string FinalUrl)
{
    public bool IsPlaceholder => FinalUrl.Contains("/noimage/", StringComparison.OrdinalIgnoreCase);
}
