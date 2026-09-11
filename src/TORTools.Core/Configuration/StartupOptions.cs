using TORTools.Core.Workspace;

namespace TORTools.Core.Configuration;

/// <summary>
/// Startup configuration for a TORTools host, read from command-line arguments and environment
/// variables.
///
/// Command line and environment are kept as separate layers rather than merged here, so
/// <see cref="WorkspaceResolver"/> can rank them against the saved config and auto-detection.
///
/// Parsing never throws. A stdio MCP server that dies during startup gives its client no
/// diagnostic beyond a closed pipe, so a malformed argument is ignored and the host falls back
/// to the next configuration source.
/// </summary>
public sealed record StartupOptions
{
    /// <summary>Paths given on the command line. Highest precedence.</summary>
    public required WorkspaceOverrides CommandLine { get; init; }

    /// <summary>Paths given in the environment. Beaten only by the command line.</summary>
    public required WorkspaceOverrides Environment { get; init; }

    /// <summary>
    /// Explicit log file, or null to let the host apply its default location.
    /// </summary>
    public string? LogFile { get; init; }

    /// <summary>Whether to write diagnostic logging at all. Off unless asked for.</summary>
    public bool Verbose { get; init; }

    public const string EnvBannerlord = "TORTOOLS_BANNERLORD";
    public const string EnvTorCore = "TORTOOLS_TOR_CORE";
    public const string EnvTorArmory = "TORTOOLS_TOR_ARMORY";
    public const string EnvTorEnvironment = "TORTOOLS_TOR_ENVIRONMENT";
    public const string EnvLogFile = "TORTOOLS_LOG_FILE";
    public const string EnvVerbose = "TORTOOLS_VERBOSE";

    /// <summary>Usage text, printed by <c>--help</c>.</summary>
    public static string Usage =>
        """
        TORTools MCP server (stdio transport).

        Workspace paths      (or set the matching environment variable)
          --bannerlord <dir>        TORTOOLS_BANNERLORD
          --tor-core <dir>          TORTOOLS_TOR_CORE
          --tor-armory <dir>        TORTOOLS_TOR_ARMORY
          --tor-environment <dir>   TORTOOLS_TOR_ENVIRONMENT

        Logging
          --log-file <path>         TORTOOLS_LOG_FILE
          --verbose, -v             TORTOOLS_VERBOSE=true
          --quiet                   force logging off

        Precedence, highest first: command line, environment, saved config, auto-detection.
        Each path resolves independently, so overriding one leaves the others alone.
        """;

    public static StartupOptions Parse(string[] args, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);

        var flags = ReadFlags(args);

        var commandLine = new WorkspaceOverrides
        {
            BannerlordPath = Value(flags, "bannerlord"),
            TorCorePath = Value(flags, "tor-core"),
            TorArmoryPath = Value(flags, "tor-armory"),
            TorEnvironmentPath = Value(flags, "tor-environment"),
        };

        var fromEnvironment = new WorkspaceOverrides
        {
            BannerlordPath = environment(EnvBannerlord),
            TorCorePath = environment(EnvTorCore),
            TorArmoryPath = environment(EnvTorArmory),
            TorEnvironmentPath = environment(EnvTorEnvironment),
        };

        var quiet = flags.ContainsKey("quiet");
        var verbose = !quiet &&
                      (flags.ContainsKey("verbose") || flags.ContainsKey("v") ||
                       IsTrue(environment(EnvVerbose)));

        return new StartupOptions
        {
            CommandLine = commandLine,
            Environment = fromEnvironment,
            LogFile = Value(flags, "log-file") ?? Blank(environment(EnvLogFile)),
            Verbose = verbose,
        };
    }

    public static bool WantsHelp(string[] args) =>
        args.Any(a => a is "--help" or "-h" or "-?" or "/?");

    /// <summary>
    /// Reads <c>--flag value</c> and <c>--flag=value</c> into a map. A flag with no value is
    /// recorded as present with a null value, so bare switches still register.
    /// </summary>
    private static Dictionary<string, string?> ReadFlags(string[] args)
    {
        var flags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg) || !arg.StartsWith('-'))
                continue;

            var name = arg.TrimStart('-');
            string? value = null;

            var equals = name.IndexOf('=');
            if (equals >= 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                value = args[++i];
            }

            if (name.Length > 0)
                flags[name] = value;
        }

        return flags;
    }

    private static string? Value(Dictionary<string, string?> flags, string name) =>
        flags.TryGetValue(name, out var value) ? Blank(value) : null;

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsTrue(string? value) =>
        Blank(value) is { } v &&
        (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" ||
         v.Equals("yes", StringComparison.OrdinalIgnoreCase));
}
