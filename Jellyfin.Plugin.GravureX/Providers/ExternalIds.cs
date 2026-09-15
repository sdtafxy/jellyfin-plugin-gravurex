using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.GravureX.Providers;

/// <summary>
/// Exposes the DMM content id as an external id, rendering a link back to the product page.
/// </summary>
public class GravureXExternalId : IExternalId
{
    public string ProviderName => Plugin.DisplayName;

    public string Key => Plugin.ProviderKey;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    public string? UrlFormatString => "https://www.dmm.com/mono/dvd/-/detail/=/cid={0}/";

    public bool Supports(IHasProviderIds item) => item is Movie;
}

/// <summary>
/// Exposes the JAN / EAN barcode as an external id.
/// </summary>
public class GravureXJanExternalId : IExternalId
{
    public string ProviderName => Plugin.JanDisplayName;

    public string Key => Plugin.JanProviderKey;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    public string? UrlFormatString => null;

    public bool Supports(IHasProviderIds item) => item is Movie;
}

/// <summary>
/// Exposes the DMM actor id as an external id, rendering a link to the performer page.
/// </summary>
public class GravureXActorExternalId : IExternalId
{
    public string ProviderName => Plugin.ActorDisplayName;

    public string Key => Plugin.ActorProviderKey;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Person;

    public string? UrlFormatString => "https://www.dmm.com/mono/dvd/-/list/=/article=actor/id={0}/";

    public bool Supports(IHasProviderIds item) => item is Person;
}
