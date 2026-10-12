using System.Xml.Linq;
using TORTools.Core.DocumentStore;
using TORTools.Core.Models;
using TORTools.Core.Services;
using TORTools.Core.Workspace;
using Xunit;

namespace TORTools.Core.Tests;

/// <summary>
/// Verifies that StandaloneDocumentStore routes linked-field values (category/subcategory)
/// to the sidecar metadata file on save, and merges them back on load. Regression guard for
/// the bug where strings_add wrote category=/subcategory= inline on &lt;string&gt; elements
/// in tor_strings.xml.
/// </summary>
public class StandaloneDocumentStoreLinkedFieldTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _mainFilePath;
    private readonly string _metadataFilePath;

    public StandaloneDocumentStoreLinkedFieldTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tortools_linkedfield_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _mainFilePath = Path.Combine(_tempDir, "tor_strings.xml");
        _metadataFilePath = Path.Combine(_tempDir, "tor_strings_metadata.xml");

        // Prevent FilePathResolver from walking up and finding the REAL repo's data/ dir
        // (which contains the production tor_strings_metadata.xml we must not touch).
        FilePathResolver.OverrideDataDirectoryForTesting(_tempDir);
    }

    public void Dispose()
    {
        FilePathResolver.OverrideDataDirectoryForTesting(null);
        FilePathResolver.ClearCache();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void StringsAddFlow_WritesLinkedFieldsToSidecarNotMainFile()
    {
        // Seed main doc with one template entry.
        File.WriteAllText(_mainFilePath, """
<?xml version="1.0" encoding="UTF-8"?>
<strings>
  <!-- Faith -->
  <string id="existing_faith_entry" text="{=existing_faith_entry}Existing" />
</strings>
""");
        // Seed metadata with the template's category/subcategory so merge-on-load picks it up.
        File.WriteAllText(_metadataFilePath, """
<?xml version="1.0" encoding="UTF-8"?>
<StringMetadata>
  <String id="existing_faith_entry" category="Skill perks" subcategory="Faith" />
</StringMetadata>
""");

        var store = BuildStore();
        store.Initialize();

        // Mirror StringsTools.strings_add: create a new entry cloned from the template,
        // then UpdateEntry with id/text/category/subcategory.
        var newEntry = store.CreateEntry("tor_strings.xml", "existing_faith_entry");
        Assert.NotNull(newEntry);

        var updateOk = store.UpdateEntry("tor_strings.xml", newEntry!.Id!, new Dictionary<string, string?>
        {
            ["id"] = "tor_newly_added_string",
            ["text"] = "{=tor_newly_added_string}Freshly added",
            ["category"] = "Skill perks",
            ["subcategory"] = "Faith",
        });
        Assert.True(updateOk);

        // Verify main file: new entry present, but NO inline category/subcategory on it.
        var mainXml = XDocument.Load(_mainFilePath);
        var newElement = mainXml.Root!.Elements("string")
            .FirstOrDefault(e => e.Attribute("id")?.Value == "tor_newly_added_string");
        Assert.NotNull(newElement);
        Assert.Null(newElement!.Attribute("category"));
        Assert.Null(newElement.Attribute("subcategory"));
        Assert.Equal("{=tor_newly_added_string}Freshly added", newElement.Attribute("text")?.Value);

        // Verify metadata sidecar: new entry has correct category/subcategory.
        var metadataXml = XDocument.Load(_metadataFilePath);
        var metaElement = metadataXml.Root!.Elements("String")
            .FirstOrDefault(e => e.Attribute("id")?.Value == "tor_newly_added_string");
        Assert.NotNull(metaElement);
        Assert.Equal("Skill perks", metaElement!.Attribute("category")?.Value);
        Assert.Equal("Faith", metaElement.Attribute("subcategory")?.Value);
    }

    [Fact]
    public void SaveStripsExistingInlineLinkedFields()
    {
        // The 59 pre-existing polluted entries in the real tor_strings.xml have inline
        // category/subcategory. Confirm save migrates them to the sidecar automatically.
        File.WriteAllText(_mainFilePath, """
<?xml version="1.0" encoding="UTF-8"?>
<strings>
  <string id="polluted_entry" text="{=polluted_entry}Text" category="Units" subcategory="Wood Elves" />
</strings>
""");
        // No metadata file exists yet.

        var store = BuildStore();
        store.Initialize();

        // Touching the file via UpdateEntry triggers a save with the new linked-field logic.
        store.UpdateEntry("tor_strings.xml", "polluted_entry", new Dictionary<string, string?>
        {
            ["text"] = "{=polluted_entry}Text updated",
        });

        var mainXml = XDocument.Load(_mainFilePath);
        var element = mainXml.Root!.Elements("string").First();
        Assert.Null(element.Attribute("category"));
        Assert.Null(element.Attribute("subcategory"));

        // Sidecar was created with the migrated values.
        Assert.True(File.Exists(_metadataFilePath));
        var metadataXml = XDocument.Load(_metadataFilePath);
        var meta = metadataXml.Root!.Elements("String")
            .FirstOrDefault(e => e.Attribute("id")?.Value == "polluted_entry");
        Assert.NotNull(meta);
        Assert.Equal("Units", meta!.Attribute("category")?.Value);
        Assert.Equal("Wood Elves", meta.Attribute("subcategory")?.Value);
    }

    private StandaloneDocumentStore BuildStore()
    {
        var xmlService = new XmlDocumentService();
        var workspace = new StubWorkspaceService(_mainFilePath);
        return new StandaloneDocumentStore(xmlService, workspace);
    }

    private sealed class StubWorkspaceService : IWorkspaceService
    {
        private readonly string _mainFilePath;

        public StubWorkspaceService(string mainFilePath)
        {
            _mainFilePath = mainFilePath;
        }

        public string ConfigFilePath => Path.Combine(Path.GetTempPath(), "stub.config.json");

        public WorkspaceConfig AutoDetect() => BuildConfig();
        public WorkspaceConfig LoadConfig() => BuildConfig();
        public void SaveConfig(WorkspaceConfig config) { }

        public IReadOnlyList<XmlFileInfo> GetXmlFiles(WorkspaceConfig config) => new[]
        {
            new XmlFileInfo
            {
                FilePath = _mainFilePath,
                DisplayName = "Strings",
                Category = "Localization",
                Repository = "TOR_Core",
                RelativePath = "tor_strings.xml",
            }
        };

        public IReadOnlyList<CatalogGroup> GetCatalogs(WorkspaceConfig config) => Array.Empty<CatalogGroup>();

        public WorkspaceValidationResult ValidateWorkspace(WorkspaceConfig config) => new()
        {
            IsValid = true,
            TorCoreFound = true,
        };

        private WorkspaceConfig BuildConfig() => new()
        {
            TorCorePath = Path.GetDirectoryName(_mainFilePath),
        };
    }
}
