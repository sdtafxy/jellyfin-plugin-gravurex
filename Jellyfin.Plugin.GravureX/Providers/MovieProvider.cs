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
    private readonly ItemContentIdResolver _contentIds;
    private readonly ConfigurationAccessor _configuration;
    private readonly ILogger<MovieProvider> _logger;

    public MovieProvider(
        DmmClient client,
        ContentNumberResolver resolver,
        ItemContentIdResolver contentIds,
        ConfigurationAccessor configuration,
        ILogger<MovieProvider> logger)
    {
        _client = client;
        _resolver = resolver;
        _contentIds = contentIds;
        _configuration = configuration;
        _logger = logger;
    }

    public string Name => Plugin.DisplayName;

    public int Order => 1;

    /// <summary>
    /// Offers the editions of a title that the user can choose between.
    /// </summary>
    /// <remarks>
    /// One content number maps to several products: the plain DVD, a Blu-ray, and
    /// limited or bonus editions that add a photo set. With
    /// <see cref="Configuration.PluginConfiguration.PreferStandardEdition"/> on, only
    /// the plainest edition is offered. With it off every edition is offered, which
    /// is what a manual search needs, and the plainest one is listed first either
    /// way. A stored id, or a full content id in the file name, pinpoints one
    /// product, so that single edition is offered on its own.
    /// </remarks>
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
    {
        var candidate = ItemContentIdResolver.ReadCandidate(
            ItemContentIdResolver.NamesFrom(searchInfo.Name, searchInfo.Path));

        var exact = ItemContentIdResolver.FindStoredId(searchInfo.ProviderIds)
                    ?? (candidate is { IsFullContentId: true } full ? full.Token : null);

        if (!string.IsNullOrEmpty(exact))
        {
            var exactTitle = await _client.GetTitleAsync(exact, cancellationToken).ConfigureAwait(false);
            if (exactTitle is not null)
            {
                return new[] { ToSearchResult(exactTitle) };
            }
        }

        if (candidate?.Number is { Length: > 0 } number)
        {
            var editions = await _resolver.FindEditionsAsync(number, cancellationToken).ConfigureAwait(false);
            if (editions.Count > 0)
            {
                var preferStandard = _configuration.Current?.PreferStandardEdition ?? true;

                _logger.LogInformation(
                    "GravureX: offering {Offered} of {Total} edition(s) for {Number} (standard preferred {Preferred})",
                    preferStandard ? 1 : editions.Count,
                    editions.Count,
                    number,
                    preferStandard);

                IReadOnlyList<DmmSearchItem> offered = preferStandard
                    ? new[] { ContentNumberResolver.PickStandard(editions) }
                    : editions.OrderBy(edition => edition.EditionRank).ToList();

                return offered.Select(ToSearchResult).ToList();
            }
        }

        // Nothing in the file name identified a product, so fall back to searching
        // with whatever name the item has.
        if (string.IsNullOrWhiteSpace(searchInfo.Name))
        {
            return Enumerable.Empty<RemoteSearchResult>();
        }

        var items = await _client.SearchAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false);
        return items.Select(ToSearchResult).ToList();
    }

    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Movie> { Item = new Movie(), ResultLanguage = "ja" };

        var lookup = await ResolveAsync(info, cancellationToken).ConfigureAwait(false);
        var contentId = lookup.ContentId;
        var contentNumber = lookup.ContentNumber;

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

        var configuration = _configuration.Current;
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

    /// <summary>
    /// Answers Jellyfin when it asks this provider for an image. Artwork is served
    /// by <see cref="ImageProvider"/>; a 404 keeps the request from failing loudly
    /// if the image pipeline happens to route it here.
    /// </summary>
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));

    /// <summary>
    /// Builds a search result from a listing entry the user can pick.
    /// </summary>
    private static RemoteSearchResult ToSearchResult(DmmSearchItem item) => new()
    {
        Name = item.Title,
        SearchProviderName = Plugin.DisplayName,
        ImageUrl = item.ImageUrl,
        PremiereDate = item.ReleaseDate,
        ProductionYear = item.ReleaseDate?.Year,
        ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Plugin.ProviderKey] = item.ContentId
        }
    };

    private static RemoteSearchResult ToSearchResult(DmmTitle title) => new()
    {
        Name = string.IsNullOrEmpty(title.CleanTitle) ? title.Title : title.CleanTitle,
        SearchProviderName = Plugin.DisplayName,
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
    /// Resolves the content id for an item, and with it the content number the
    /// item's names carried.
    /// </summary>
    private Task<ContentIdLookup> ResolveAsync(MovieInfo info, CancellationToken cancellationToken) =>
        _contentIds.ResolveAsync(info.ProviderIds, ItemContentIdResolver.NamesFrom(info.Name, info.Path), cancellationToken);

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
}
