using System.Reflection;

// Pulsar compiles the plugin from source and ignores Version.Build.props, so without these attributes the
// plugin list shows 0.0.0.0. The version lives in one place: Plugin.Version.
// (The SDK's own version attributes are switched off in Version.Build.props to avoid duplicates.)
[assembly: AssemblyVersion(ClientPlugin.Plugin.Version)]
[assembly: AssemblyFileVersion(ClientPlugin.Plugin.Version)]
[assembly: AssemblyInformationalVersion(ClientPlugin.Plugin.Version)]
