namespace GMConverter.SDK.Plugins;

/// <summary>
/// Plugin metadata loaded from <c>plugin.json</c>. The host parses this with
/// <c>System.Text.Json</c> before loading the entry assembly so it can show plugins in a
/// manager UI and reject incompatible SDK versions without paying assembly-load cost.
/// </summary>
/// <param name="Id">Stable plugin id (e.g. <c>gmconverter.unrealengine</c>).</param>
/// <param name="Version">Plugin version string (SemVer recommended, but not enforced).</param>
/// <param name="DisplayName">Human-readable name for plugin manager UIs.</param>
/// <param name="Entry">Path to the entry assembly relative to the manifest directory.</param>
/// <param name="SdkVersion">Host SDK version this plugin was built against (informational; the
/// host accepts plugins built against the same major version).</param>
/// <param name="Capabilities">Optional list of capability tags for surfacing in the plugin
/// manager (e.g. <c>importer:psk</c>, <c>explorer:ue4</c>).</param>
public sealed record PluginManifest(
    string Id,
    string Version,
    string DisplayName,
    string Entry,
    string SdkVersion,
    IReadOnlyList<string>? Capabilities = null);
