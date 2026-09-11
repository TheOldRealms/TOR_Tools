using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using TORTools.Core.Configuration;
using TORTools.Core.DocumentStore;
using TORTools.Core.Services;
using TORTools.Core.Workspace;
using TORTools.Mcp.Host.Services;
using TORTools.Mcp.Host.Tools;

// stdout IS the JSON-RPC channel for a stdio MCP server. TORTools.Core writes diagnostics to
// Console.WriteLine in ~15 services (fine for the desktop app, fatal here), so redirect the
// console writer to stderr before anything can emit. The MCP transport holds the real stdout
// stream directly and is unaffected.
Console.SetOut(Console.Error);

if (StartupOptions.WantsHelp(args))
{
    Console.Error.WriteLine(StartupOptions.Usage);
    return;
}

var options = StartupOptions.Parse(args, Environment.GetEnvironmentVariable);

// Configure logging before anything else, so workspace resolution is itself logged.
if (options.Verbose)
{
    StandaloneDocumentStore.VerboseLogging = true;
    StandaloneDocumentStore.LogFilePath = options.LogFile ?? DefaultLogFile();
    StandaloneDocumentStore.InitializeLogging();
    Console.Error.WriteLine($"[MCP] Verbose logging enabled. Log file: {StandaloneDocumentStore.LogFilePath ?? "(stderr)"}");
}

var builder = Host.CreateApplicationBuilder(args);

// Register core services
builder.Services.AddSingleton<IXmlDocumentService, XmlDocumentService>();
builder.Services.AddSingleton<IWorkspaceService, WorkspaceService>();
builder.Services.AddSingleton<CrossReferenceService>();

// Register MCP services
builder.Services.AddSingleton<QueryService>();

// Register document store
builder.Services.AddSingleton<IDocumentStore, StandaloneDocumentStore>();

// Configure MCP server with stdio transport
builder.Services.AddMcpServer(mcp =>
{
    mcp.ServerInfo = new()
    {
        Name = "TORTools",
        Version = "1.0.0"
    };
})
.WithStdioServerTransport()
.WithTools<FileTools>()
.WithTools<EntryTools>()
.WithTools<QueryTools>()
.WithTools<CompareTools>()
.WithTools<StringsTools>()
.WithTools<TranslationTools>();

var app = builder.Build();

// Resolve the workspace: command line, then environment, then saved config, then auto-detection.
// Each path resolves independently, so --tor-core alone does not blank the others.
var workspaceService = app.Services.GetRequiredService<IWorkspaceService>();
var resolved = WorkspaceResolver.Resolve(
    commandLine: options.CommandLine,
    environment: options.Environment,
    saved: workspaceService.LoadConfig(),
    autoDetect: workspaceService.AutoDetect);

// Always report which repository the server is about to edit, and shout if a configured path is
// wrong. Two checkouts of the same repo on one machine is normal here, and silently editing the
// wrong one — or silently resolving nothing because of a typo — is the expensive failure.
var savedConfig = workspaceService.LoadConfig();
var problems = 0;

foreach (var (path, source) in resolved.Sources.OrderBy(s => s.Key))
{
    var value = path switch
    {
        nameof(resolved.Config.BannerlordPath) => resolved.Config.BannerlordPath,
        nameof(resolved.Config.TorCorePath) => resolved.Config.TorCorePath,
        nameof(resolved.Config.TorArmoryPath) => resolved.Config.TorArmoryPath,
        nameof(resolved.Config.TorEnvironmentPath) => resolved.Config.TorEnvironmentPath,
        _ => null
    };

    if (value is null)
    {
        Console.Error.WriteLine($"[MCP] {path} = (not set)");
        continue;
    }

    if (Directory.Exists(value))
    {
        Console.Error.WriteLine($"[MCP] {path} = {value} [{source}]");
    }
    else
    {
        problems++;
        Console.Error.WriteLine($"[MCP] ERROR {path} = {value} [{source}] -- directory does not exist");
    }

    // A path that beat a different saved value is worth naming: it is the case where the server
    // edits one checkout while the desktop app is configured for another.
    var saved = path switch
    {
        nameof(savedConfig.BannerlordPath) => savedConfig.BannerlordPath,
        nameof(savedConfig.TorCorePath) => savedConfig.TorCorePath,
        nameof(savedConfig.TorArmoryPath) => savedConfig.TorArmoryPath,
        nameof(savedConfig.TorEnvironmentPath) => savedConfig.TorEnvironmentPath,
        _ => null
    };

    if (source is not WorkspaceSource.SavedConfig &&
        !string.IsNullOrWhiteSpace(saved) &&
        !string.Equals(Path.TrimEndingDirectorySeparator(saved.Trim()),
                       Path.TrimEndingDirectorySeparator(value),
                       StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"[MCP]   overrides saved config: {saved}");
    }
}

if (problems > 0)
{
    Console.Error.WriteLine($"[MCP] ERROR {problems} configured path(s) do not exist. Run with --help for the options.");
}


var documentStore = app.Services.GetRequiredService<IDocumentStore>();
var initResult = documentStore.Initialize(resolved.Config);
if (!initResult.Success)
{
    Console.Error.WriteLine($"[MCP] Failed to initialize workspace: {initResult.Error}");
    Console.Error.WriteLine("[MCP] Set --tor-core (or TORTOOLS_TOR_CORE). Run with --help for all options.");
    // Continue anyway - tools will report errors for missing files.
}

await app.RunAsync();

// Default log location: <repo root>/.logs/mcp.log, used when neither --log-file nor
// TORTOOLS_LOG_FILE is set. Walks up from the assembly to find the TORTools checkout.
static string DefaultLogFile()
{
    var assemblyPath = typeof(Program).Assembly.Location;
    var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyPath) ?? ".");

    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "TORTools.slnx")) ||
            File.Exists(Path.Combine(dir.FullName, "TORTools.sln")) ||
            Directory.Exists(Path.Combine(dir.FullName, "src", "TORTools.Mcp.Host")))
        {
            break;
        }
        dir = dir.Parent;
    }

    var rootDir = dir?.FullName ?? Path.GetDirectoryName(assemblyPath) ?? ".";
    return Path.Combine(rootDir, ".logs", "mcp.log");
}
