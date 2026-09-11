using TORTools.Core.Models;

namespace TORTools.Core.Workspace;

/// <summary>
/// Explicit path overrides for a workspace, from one configuration source.
/// A null or blank value means "this source says nothing about that path".
/// </summary>
public sealed record WorkspaceOverrides
{
    public string? BannerlordPath { get; init; }
    public string? TorCorePath { get; init; }
    public string? TorArmoryPath { get; init; }
    public string? TorEnvironmentPath { get; init; }

    public bool IsEmpty =>
        !Has(BannerlordPath) && !Has(TorCorePath) && !Has(TorArmoryPath) && !Has(TorEnvironmentPath);

    internal static bool Has(string? value) => !string.IsNullOrWhiteSpace(value);
}

/// <summary>Where a resolved path came from.</summary>
public enum WorkspaceSource
{
    None,
    AutoDetected,
    SavedConfig,
    Environment,
    CommandLine,
}

/// <summary>A resolved workspace, plus the source each individual path came from.</summary>
public sealed record ResolvedWorkspace(
    WorkspaceConfig Config,
    IReadOnlyDictionary<string, WorkspaceSource> Sources);

/// <summary>
/// Layers workspace paths from every configuration source.
///
/// Precedence, highest first: command line, environment variables, saved config, auto-detection.
/// Layering is <b>per path</b>: overriding TOR_Core alone leaves the other paths resolving from
/// lower-priority sources, so a single `--tor-core` does not silently blank the rest.
///
/// Auto-detection walks the filesystem, so it runs only when some path is still unresolved.
/// </summary>
public static class WorkspaceResolver
{
    public static ResolvedWorkspace Resolve(
        WorkspaceOverrides commandLine,
        WorkspaceOverrides environment,
        WorkspaceConfig? saved,
        Func<WorkspaceConfig> autoDetect)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(autoDetect);

        var savedOverrides = new WorkspaceOverrides
        {
            BannerlordPath = saved?.BannerlordPath,
            TorCorePath = saved?.TorCorePath,
            TorArmoryPath = saved?.TorArmoryPath,
            TorEnvironmentPath = saved?.TorEnvironmentPath,
        };

        var layers = new List<(WorkspaceSource Source, WorkspaceOverrides Values)>
        {
            (WorkspaceSource.CommandLine, commandLine),
            (WorkspaceSource.Environment, environment),
            (WorkspaceSource.SavedConfig, savedOverrides),
        };

        // Only pay for a filesystem walk if something is still missing.
        if (AnyUnresolved(layers))
        {
            var detected = autoDetect() ?? new WorkspaceConfig();
            layers.Add((WorkspaceSource.AutoDetected, new WorkspaceOverrides
            {
                BannerlordPath = detected.BannerlordPath,
                TorCorePath = detected.TorCorePath,
                TorArmoryPath = detected.TorArmoryPath,
                TorEnvironmentPath = detected.TorEnvironmentPath,
            }));
        }

        var (bannerlord, bannerlordSource) = First(layers, o => o.BannerlordPath);
        var (torCore, torCoreSource) = First(layers, o => o.TorCorePath);
        var (torArmory, torArmorySource) = First(layers, o => o.TorArmoryPath);
        var (torEnvironment, torEnvironmentSource) = First(layers, o => o.TorEnvironmentPath);

        var config = new WorkspaceConfig
        {
            BannerlordPath = bannerlord,
            TorCorePath = torCore,
            TorArmoryPath = torArmory,
            TorEnvironmentPath = torEnvironment,
            RecentFiles = saved?.RecentFiles ?? new List<string>(),
        };

        var sources = new Dictionary<string, WorkspaceSource>
        {
            [nameof(WorkspaceConfig.BannerlordPath)] = bannerlordSource,
            [nameof(WorkspaceConfig.TorCorePath)] = torCoreSource,
            [nameof(WorkspaceConfig.TorArmoryPath)] = torArmorySource,
            [nameof(WorkspaceConfig.TorEnvironmentPath)] = torEnvironmentSource,
        };

        return new ResolvedWorkspace(config, sources);
    }

    private static bool AnyUnresolved(List<(WorkspaceSource Source, WorkspaceOverrides Values)> layers) =>
        First(layers, o => o.BannerlordPath).Value is null ||
        First(layers, o => o.TorCorePath).Value is null ||
        First(layers, o => o.TorArmoryPath).Value is null ||
        First(layers, o => o.TorEnvironmentPath).Value is null;

    private static (string? Value, WorkspaceSource Source) First(
        List<(WorkspaceSource Source, WorkspaceOverrides Values)> layers,
        Func<WorkspaceOverrides, string?> select)
    {
        foreach (var (source, values) in layers)
        {
            var value = select(values);
            if (WorkspaceOverrides.Has(value))
                return (value!.Trim(), source);
        }

        return (null, WorkspaceSource.None);
    }
}
