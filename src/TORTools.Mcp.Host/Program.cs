using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using TORTools.Core.Services;
using TORTools.Core.Workspace;
using TORTools.Core.DocumentStore;
using TORTools.Mcp.Host.Services;
using TORTools.Mcp.Host.Tools;

// Debug: Write args to a known location to verify startup
var debugLogPath = @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\TORTools\.logs\mcp_debug.log";
try
{
    Directory.CreateDirectory(Path.GetDirectoryName(debugLogPath)!);
    File.AppendAllText(debugLogPath, $"\n=== MCP Started {DateTime.Now} ===\nArgs: {string.Join(" ", args)}\n");
}
catch { /* ignore */ }

// Parse command-line arguments
// Default to verbose=true for debugging (args not being passed by Claude Code)
var verbose = true; // args.Contains("--verbose") || args.Contains("-v");
var logFileIndex = Array.IndexOf(args, "--log-file");
string? logFile = null;

if (logFileIndex >= 0 && logFileIndex < args.Length - 1)
{
    logFile = args[logFileIndex + 1];
}
else if (verbose)
{
    // Default log location: TORTools/.logs/mcp.log (gitignored)
    // Walk up from the executing assembly location to find TORTools root
    var assemblyPath = typeof(Program).Assembly.Location;
    var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyPath) ?? ".");

    // Walk up looking for TORTools markers (sln file or src folder with TORTools.Mcp.Host)
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "TORTools.sln")) ||
            Directory.Exists(Path.Combine(dir.FullName, "src", "TORTools.Mcp.Host")))
        {
            break;
        }
        dir = dir.Parent;
    }

    var rootDir = dir?.FullName ?? Path.GetDirectoryName(assemblyPath) ?? ".";
    logFile = Path.Combine(rootDir, ".logs", "mcp.log");
}

// Configure logging
if (verbose)
{
    StandaloneDocumentStore.VerboseLogging = true;
    StandaloneDocumentStore.LogFilePath = logFile;
    StandaloneDocumentStore.InitializeLogging();
    Console.Error.WriteLine($"[MCP] Verbose logging enabled. Log file: {logFile ?? "(stderr)"}");
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
builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new()
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
.WithTools<StringsTools>();

var app = builder.Build();

// Initialize workspace on startup
var documentStore = app.Services.GetRequiredService<IDocumentStore>();
var initResult = documentStore.Initialize();
if (!initResult.Success)
{
    Console.Error.WriteLine($"Failed to initialize workspace: {initResult.Error}");
    // Continue anyway - tools will report errors for missing files
}

await app.RunAsync();
