using Jellyfin.Data.Enums;
using Jellyfin.Plugin.GravureX.Dmm;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.GravureX.Providers;

/// <summary>
/// Movie metadata provider backed by DMM mono (DVD / Blu-ray).
/// </summary>
public class MovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>, IHasOrder
{
    private readonly DmmClient _client;
    private readonly ContentNumberResolver _resolver;
    private readonly ILogger<MovieProvider> _logger;

    public MovieProvider(DmmClient client, ContentNumberResolver resolver, ILogger<MovieProvider> logger)
    {
        _client = client;
        _resolver = resolver;
        _logger = logger;
    }

    public string Name => Plugin.ProviderName;

    public int Order => 1;

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
    {
        var (contentId, _) = await ResolveContentIdAsync(searchInfo, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(contentId))
        {
            var title = await _client.GetTitleAsync(contentId, cancellationToken).ConfigureAwait(false);
            if (title is not null)
            {
                return new[] { ToSearchResult(title) };
            }
        }

        if (string.IsNullOrWhiteSpace(searchInfo.Name))
        {
            return Enumerable.Empty<RemoteSearchResult>();
        }

        var items = await _client.SearchAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false);
        return items.Select(item => new RemoteSearchResult
        {
            Name = item.Title,
            SearchProviderName = Name,
            ImageUrl = item.ImageUrl,
            PremiereDate = item.ReleaseDate,
            ProductionYear = item.ReleaseDate?.Year,
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Plugin.ProviderKey] = item.ContentId
            }
        }).ToList();
    }

    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Movie> { Item = new Movie(), ResultLanguage = "ja" };

        var (contentId, contentNumber) = await ResolveContentIdAsync(info, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(contentId))
        {
            _logger.LogInformation("GravureX: no content id resolvable for '{Name}'", info.Name);
            return result;
        }

        var title = await _client.GetTitleAsync(contentId, cancellationToken).ConfigureAwait(false);
        if (title is null)
        {
            return result;
        }

        var configuration = Plugin.Instance?.Configuration;
        var movie = result.Item;

        var baseTitle = string.IsNullOrEmpty(title.CleanTitle) ? title.Title : title.CleanTitle;
        movie.Name = BuildTitle(baseTitle, contentNumber, configuration?.PrefixTitleWithContentNumber ?? true);
        movie.OriginalTitle = title.Title;
        movie.Overview = title.Overview;
        movie.PremiereDate = title.ReleaseDate;
        movie.ProductionYear = title.ReleaseDate?.Year;
        movie.HomePageUrl = title.DetailUrl;

        if (title.RuntimeMinutes is > 0)
        {
            movie.RunTimeTicks = TimeSpan.FromMinutes(title.RuntimeMinutes.Value).Ticks;
        }

        if (title.CommunityRating is > 0)
        {
            movie.CommunityRating = title.CommunityRating;
        }

        movie.ProviderIds[Plugin.ProviderKey] = title.ContentId;

        if (!string.IsNullOrEmpty(title.Jan) && configuration?.EnableJanProviderId != false)
        {
            movie.ProviderIds[Plugin.JanProviderKey] = title.Jan;
        }

        if (!string.IsNullOrEmpty(title.Maker))
        {
            movie.Studios = new[] { title.Maker };
        }

        if (configuration?.EnableGenres != false && title.Genres.Count > 0)
        {
            movie.Genres = title.Genres.ToArray();
        }

        if (configuration?.EnableTags != false)
        {
            var tags = new List<string>();
            if (!string.IsNullOrEmpty(title.Media))
            {
                tags.Add(title.Media);
            }

            if (!string.IsNullOrEmpty(title.Series))
            {
                tags.Add(title.Series);
            }

            if (tags.Count > 0)
            {
                movie.Tags = tags.ToArray();
            }
        }

        var people = new List<PersonInfo>();
        foreach (var actor in title.Actors)
        {
            var person = new PersonInfo
            {
                Name = actor.Name,
                Type = PersonKind.Actor
            };

            if (!string.IsNullOrEmpty(actor.ActorId))
            {
                person.ProviderIds[Plugin.ActorProviderKey] = actor.ActorId;
            }

            people.Add(person);
        }

        if (!string.IsNullOrEmpty(title.Director) && (configuration?.ImportDirector ?? false))
        {
            people.Add(new PersonInfo { Name = title.Director, Type = PersonKind.Director });
        }

        result.People = people;

        result.HasMetadata = true;
        result.QueriedById = true;
        return result;
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    private static RemoteSearchResult ToSearchResult(DmmTitle title) => new()
    {
        Name = string.IsNullOrEmpty(title.CleanTitle) ? title.Title : title.CleanTitle,
        SearchProviderName = Plugin.ProviderName,
        Overview = title.Overview,
        ImageUrl = title.PackageImageUrl,
        PremiereDate = title.ReleaseDate,
        ProductionYear = title.ReleaseDate?.Year,
        ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Plugin.ProviderKey] = title.ContentId
        }
    };

    /// <summary>
    /// Resolves the DMM content id, and reports the content number the file name
    /// used so it can be put in front of the title.
    /// </summary>
    private async Task<(string? ContentId, string? ContentNumber)> ResolveContentIdAsync(MovieInfo info, CancellationToken cancellationToken)
    {
        // The number that goes in front of the title always comes from the file name,
        // never from the stored provider id, so it is read first. A stored id only
        // saves the search; returning early on it used to drop the number and strip
        // the prefix off a title that already had it.
        var candidate = ReadCandidate(info);

        if (info.ProviderIds is not null)
        {
            foreach (var key in Plugin.ContentIdKeys)
            {
                if (info.ProviderIds.TryGetValue(key, out var stored) && !string.IsNullOrWhiteSpace(stored))
                {
                    _logger.LogDebug(
                        "GravureX: using stored provider id {Id}, file name number {Number}",
                        stored,
                        candidate?.Number ?? "(none)");
                    return (stored.Trim(), candidate?.Number);
                }
            }
        }

        if (candidate is null)
        {
            return (null, null);
        }

        var (token, number, isFullContentId) = candidate.Value;

        if (isFullContentId)
        {
            return (token, null);
        }

        var resolved = await _resolver.ResolveAsync(token, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(resolved) ? (null, null) : (resolved, number);
    }

    /// <summary>
    /// Reads the first usable content number or content id out of the item's names.
    /// </summary>
    private static (string Token, string? Number, bool IsFullContentId)? ReadCandidate(MovieInfo info)
    {
        foreach (var source in CandidateNames(info))
        {
            var extracted = ContentNumberResolver.Extract(source);
            if (string.IsNullOrEmpty(extracted))
            {
                continue;
            }

            var isFullContentId = ContentNumberResolver.IsFullContentId(extracted);

            // A full content id is not the number the file was named with, so it is
            // not used as a prefix.
            return (extracted, isFullContentId ? null : extracted, isFullContentId);
        }

        return null;
    }

    /// <summary>
    /// Puts the content number in front of the title, unless the title already
    /// carries it or no number was found in the file name.
    /// </summary>
    public static string BuildTitle(string title, string? contentNumber, bool prefix)
    {
        if (!prefix || string.IsNullOrWhiteSpace(contentNumber) || string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        var number = contentNumber.Trim();
        if (title.StartsWith(number, StringComparison.OrdinalIgnoreCase))
        {
            return title;
        }

        return $"{number} {title}";
    }

    private static IEnumerable<string?> CandidateNames(MovieInfo info)
    {
        yield return info.Name;

        if (string.IsNullOrEmpty(info.Path))
        {
            yield break;
        }

        yield return Path.GetFileNameWithoutExtension(info.Path);

        var directory = Path.GetDirectoryName(info.Path);
        if (!string.IsNullOrEmpty(directory))
        {
            yield return Path.GetFileName(directory);
        }
    }
}
