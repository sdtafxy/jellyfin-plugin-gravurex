using Jellyfin.Plugin.GravureX.Configuration;
using Jellyfin.Plugin.GravureX.Dmm;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.GravureX;

/// <summary>
/// GravureX plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Name shown in the Jellyfin dashboard, and the provider name reported with
    /// every result. Distinct from <see cref="ProviderKey"/>, which is the key
    /// under which ids are stored.
    /// </summary>
    public const string DisplayName = "GravureX";

    /// <summary>Key used in <c>ProviderIds</c> for the DMM content id.</summary>
    public const string ProviderKey = "GravureX";

    /// <summary>Key used in <c>ProviderIds</c> for the JAN / EAN barcode.</summary>
    public const string JanProviderKey = "GravureXJan";

    /// <summary>Key used in <c>ProviderIds</c> for a DMM actor id.</summary>
    public const string ActorProviderKey = "GravureXActor";

    /// <summary>Display name of the JAN barcode provider id.</summary>
    public const string JanDisplayName = DisplayName + " JAN";

    /// <summary>Display name of the performer provider id.</summary>
    public const string ActorDisplayName = DisplayName + " Actor";

    /// <summary>
    /// Every provider id that may hold a content id, newest first. The keys after
    /// the first one were written by earlier releases and are still read, so items
    /// that were already matched keep their stored id instead of being searched again.
    /// </summary>
    public static readonly string[] ContentIdKeys = { ProviderKey, "GravureDex", "DmmMono", "Dmm" };

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override Guid Id => Guid.Parse("9BD97D01-52E6-4219-9294-25E80B7FB254");

    public override string Name => DisplayName;

    public override string Description =>
        "为日本 DVD / Blu-ray 影片（含写真偶像类）补齐元数据与图片。";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }
}

/// <summary>
/// Registers plugin services with the Jellyfin container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, MediaBrowser.Controller.IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ConfigurationAccessor>();
        serviceCollection.AddSingleton<DmmClient>();
        serviceCollection.AddSingleton<DmmIdolIndex>();
        serviceCollection.AddSingleton<ContentNumberResolver>();
        serviceCollection.AddSingleton<ItemContentIdResolver>();
    }
}
