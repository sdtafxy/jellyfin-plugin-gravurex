using Jellyfin.Plugin.GravureX.Dmm;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.GravureX.Providers;

/// <summary>
/// Person metadata provider backed by the DMM actor listing pages.
/// </summary>
/// <remarks>
/// DMM publishes no biography or birth date for gravure performers, so this
/// provider resolves the canonical name and the DMM actor id. The id gives
/// Jellyfin a stable link to the performer page and lets the image provider
/// attach a portrait when DMM has one.
/// </remarks>
public class PersonProvider : IRemoteMetadataProvider<Person, PersonLookupInfo>, IHasOrder
{
    private readonly DmmClient _client;
    private readonly DmmIdolIndex _idolIndex;
    private readonly ILogger<PersonProvider> _logger;

    public PersonProvider(DmmClient client, DmmIdolIndex idolIndex, ILogger<PersonProvider> logger)
    {
        _client = client;
        _idolIndex = idolIndex;
        _logger = logger;
    }

    public string Name => Plugin.ProviderName;

    public int Order => 1;

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken cancellationToken)
    {
        var actorId = GetActorId(searchInfo);
        if (!string.IsNullOrEmpty(actorId))
        {
            var actor = await _client.GetActorAsync(actorId, cancellationToken).ConfigureAwait(false);
            if (actor is not null)
            {
                return new[] { ToSearchResult(actor) };
            }
        }

        var byName = await _idolIndex.FindAsync(null, searchInfo.Name, cancellationToken).ConfigureAwait(false);
        if (byName is null)
        {
            _logger.LogDebug("GravureX: no performer match for '{Name}'", searchInfo.Name);
            return Enumerable.Empty<RemoteSearchResult>();
        }

        return new[] { ToSearchResult(byName) };
    }

    public async Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Person> { Item = new Person(), ResultLanguage = "ja" };

        var actorId = GetActorId(info);
        DmmActor? actor = null;

        if (!string.IsNullOrEmpty(actorId))
        {
            actor = await _client.GetActorAsync(actorId, cancellationToken).ConfigureAwait(false);
        }

        actor ??= await _idolIndex.FindAsync(null, info.Name, cancellationToken).ConfigureAwait(false);

        if (actor is null)
        {
            return result;
        }

        // The listing page is the authoritative source for the name and reading;
        // the index adds the portrait when one exists.
        var indexed = await _idolIndex.FindAsync(actor.Id.Length > 0 ? actor.Id : null, actor.Name, cancellationToken)
            .ConfigureAwait(false);

        var person = result.Item;
        person.Name = actor.Name;
        person.HomePageUrl = actor.ListingUrl;

        if (actor.Id.Length > 0)
        {
            person.ProviderIds[Plugin.ActorProviderKey] = actor.Id;
        }

        if (!string.IsNullOrEmpty(actor.Reading))
        {
            person.Overview = $"读音：{actor.Reading}";
        }

        if (indexed?.ImageUrl is { Length: > 0 } portrait)
        {
            person.ImageInfos =
            [
                new ItemImageInfo
                {
                    Path = portrait,
                    Type = ImageType.Primary
                }
            ];
        }

        result.HasMetadata = true;
        result.QueriedById = actor.Id.Length > 0;
        return result;
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    private static RemoteSearchResult ToSearchResult(DmmActor actor) => new()
    {
        Name = actor.Name,
        SearchProviderName = Plugin.ProviderName,
        ImageUrl = actor.ImageUrl,
        ProviderIds = actor.Id.Length == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Plugin.ActorProviderKey] = actor.Id
            }
    };

    private static string? GetActorId(PersonLookupInfo info)
    {
        if (info.ProviderIds is null)
        {
            return null;
        }

        return info.ProviderIds.TryGetValue(Plugin.ActorProviderKey, out var id) && !string.IsNullOrWhiteSpace(id)
            ? id.Trim()
            : null;
    }
}
