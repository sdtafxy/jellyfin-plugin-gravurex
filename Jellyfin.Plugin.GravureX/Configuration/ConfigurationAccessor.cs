using Jellyfin.Plugin.GravureX.Configuration;

namespace Jellyfin.Plugin.GravureX;

/// <summary>
/// Supplies the plugin configuration to the services that need it.
/// </summary>
/// <remarks>
/// Every service reads the configuration through this one accessor instead of
/// reaching for <see cref="Plugin.Instance"/> directly. That keeps a service from
/// depending on a static that is only assigned once the server has loaded the
/// plugin, and it lets a service be handed any configuration, which is what the
/// offline tests do.
/// </remarks>
public sealed class ConfigurationAccessor
{
    private readonly Func<PluginConfiguration?> _source;

    public ConfigurationAccessor()
        : this(null)
    {
    }

    public ConfigurationAccessor(Func<PluginConfiguration?>? source) =>
        _source = source ?? (() => Plugin.Instance?.Configuration);

    /// <summary>
    /// The configuration in force, or null when the plugin has not been loaded
    /// yet. Callers fall back to the defaults of <see cref="PluginConfiguration"/>.
    /// </summary>
    public PluginConfiguration? Current => _source();
}
