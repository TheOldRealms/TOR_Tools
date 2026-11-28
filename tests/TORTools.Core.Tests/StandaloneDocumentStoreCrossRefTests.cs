using System.Xml.Linq;
using TORTools.Core.DocumentStore;
using TORTools.Core.Models;
using TORTools.Core.Services;
using TORTools.Core.Workspace;
using Xunit;

namespace TORTools.Core.Tests;

/// <summary>
/// Verifies that StandaloneDocumentStore routes crossReference writes (e.g. RaceLock) to
/// the source file (tor_extendeditemproperties.xml), not inline on the main item element.
/// Regression guard for the bug where update_entry wrote RaceLock inline on &lt;Item&gt;.
/// </summary>
[Collection("FilePathResolverSerial")]
public class StandaloneDocumentStoreCrossRefTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _mainFilePath;
    private readonly string _sidecarPath;

    public StandaloneDocumentStoreCrossRefTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tortools_crossref_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _mainFilePath = Path.Combine(_tempDir, "tor_armors.xml");
        // Sidecar lives in tor_custom_xmls/ next to the main file (per FilePathResolver search order).
        Directory.CreateDirectory(Path.Combine(_tempDir, "tor_custom_xmls"));
        _sidecarPath = Path.Combine(_tempDir, "tor_custom_xmls", "tor_extendeditemproperties.xml");

        // Prevent FilePathResolver from walking up and finding the REAL repo's data/ dir.
        FilePathResolver.OverrideDataDirectoryForTesting(_tempDir);
    }

    public void Dispose()
    {
        FilePathResolver.OverrideDataDirectoryForTesting(null);
        FilePathResolver.ClearCache();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void UpdateEntryWithCrossRefField_WritesToSidecarNotInline()
    {
        File.WriteAllText(_mainFilePath, """
<?xml version="1.0" encoding="utf-8"?>
<Items>
  <Item id="test_item_1" name="Test Helmet" Type="HeadArmor" culture="Culture.vlandia" value="100" />
</Items>
""");
        File.WriteAllText(_sidecarPath, """
<?xml version="1.0" encoding="utf-8"?>
<ExtendedItemObjectPropertiesRoot>
  <ExtendedItemObjectProperties ItemStringId="unrelated_item" RaceLock="human" />
</ExtendedItemObjectPropertiesRoot>
""");

        var store = BuildStore();
        store.Initialize();

        // The real tor_armors schema defines RaceLock as a crossReference pointing at
        // tor_extendeditemproperties.xml with sourceValuePath="@RaceLock".
        var ok = store.UpdateEntry("tor_armors.xml", "test_item_1", new Dictionary<string, string?>
        {
            ["RaceLock"] = "elf",
        });
        Assert.True(ok);

        // Main file: NO inline RaceLock on the Item.
        var mainXml = XDocument.Load(_mainFilePath);
        var item = mainXml.Root!.Elements("Item").First(e => e.Attribute("id")?.Value == "test_item_1");
        Assert.Null(item.Attribute("RaceLock"));

        // Regression: other attributes (including crossRef fields with only TargetFile,
        // like `culture`) must NOT be stripped. Those live inline on the main element.
        Assert.Equal("Culture.vlandia", item.Attribute("culture")?.Value);
        Assert.Equal("Test Helmet", item.Attribute("name")?.Value);
        Assert.Equal("100", item.Attribute("value")?.Value);

        // Sidecar: has an entry for test_item_1 with RaceLock=elf.
        var sidecarXml = XDocument.Load(_sidecarPath);
        var sideEntry = sidecarXml.Root!.Elements("ExtendedItemObjectProperties")
            .FirstOrDefault(e => e.Attribute("ItemStringId")?.Value == "test_item_1");
        Assert.NotNull(sideEntry);
        Assert.Equal("elf", sideEntry!.Attribute("RaceLock")?.Value);

        // Unrelated existing sidecar entries are untouched.
        var unrelated = sidecarXml.Root.Elements("ExtendedItemObjectProperties")
            .FirstOrDefault(e => e.Attribute("ItemStringId")?.Value == "unrelated_item");
        Assert.NotNull(unrelated);
        Assert.Equal("human", unrelated!.Attribute("RaceLock")?.Value);
    }

    [Fact]
    public void LoadMergesCrossRefValueIntoInMemoryEntry()
    {
        File.WriteAllText(_mainFilePath, """
<?xml version="1.0" encoding="utf-8"?>
<Items>
  <Item id="helmet_a" name="Helmet A" Type="HeadArmor" culture="Culture.vlandia" value="100" />
</Items>
""");
        File.WriteAllText(_sidecarPath, """
<?xml version="1.0" encoding="utf-8"?>
<ExtendedItemObjectPropertiesRoot>
  <ExtendedItemObjectProperties ItemStringId="helmet_a" RaceLock="dwarf" />
</ExtendedItemObjectPropertiesRoot>
""");

        var store = BuildStore();
        store.Initialize();

        var entry = store.GetEntry("tor_armors.xml", "helmet_a");
        Assert.NotNull(entry);
        // RaceLock from the sidecar should appear as an attribute on the in-memory entry.
        Assert.Equal("dwarf", entry!.GetAttributeValue("RaceLock"));
    }

    [Fact]
    public void SaveStripsInlineLocalizationIdAttrFromMainFile()
    {
        // The `localizationId` virtual field's value is carried by the {=key} prefix on
        // the name attribute. Any pre-existing inline `localizationId="..."` on an item
        // is duplicate pollution and must be stripped on save.
        File.WriteAllText(_mainFilePath, """
<?xml version="1.0" encoding="utf-8"?>
<Items>
  <Item id="poll_item" name="{=str_poll_item}Polluted" Type="HeadArmor" culture="Culture.vlandia" value="100" localizationId="str_poll_item" />
</Items>
""");
        File.WriteAllText(_sidecarPath, """
<?xml version="1.0" encoding="utf-8"?>
<ExtendedItemObjectPropertiesRoot>
</ExtendedItemObjectPropertiesRoot>
""");

        var store = BuildStore();
        store.Initialize();

        // Unrelated update to trigger a save.
        store.UpdateEntry("tor_armors.xml", "poll_item", new Dictionary<string, string?>
        {
            ["value"] = "200",
        });

        var mainXml = XDocument.Load(_mainFilePath);
        var item = mainXml.Root!.Elements("Item").First();
        Assert.Null(item.Attribute("localizationId"));
        // The {=key}text prefix on name is preserved.
        Assert.Equal("{=str_poll_item}Polluted", item.Attribute("name")?.Value);
        Assert.Equal("200", item.Attribute("value")?.Value);
    }

    [Fact]
    public void SaveStripsInlineCrossRefAttrFromMainFile()
    {
        // Simulate a tor_armors.xml with the pre-existing inline-RaceLock pollution we
        // just cleaned up. Any save should strip it AND migrate it to the sidecar so the
        // value isn't silently lost.
        File.WriteAllText(_mainFilePath, """
<?xml version="1.0" encoding="utf-8"?>
<Items>
  <Item id="legacy_item" name="Legacy" Type="HeadArmor" culture="Culture.vlandia" RaceLock="gor2" value="100" />
</Items>
""");
        File.WriteAllText(_sidecarPath, """
<?xml version="1.0" encoding="utf-8"?>
<ExtendedItemObjectPropertiesRoot>
  <ExtendedItemObjectProperties ItemStringId="other" RaceLock="human" />
</ExtendedItemObjectPropertiesRoot>
""");

        var store = BuildStore();
        store.Initialize();

        // Unrelated update to trigger a save.
        store.UpdateEntry("tor_armors.xml", "legacy_item", new Dictionary<string, string?>
        {
            ["value"] = "150",
        });

        var mainXml = XDocument.Load(_mainFilePath);
        var item = mainXml.Root!.Elements("Item").First();
        Assert.Null(item.Attribute("RaceLock"));
        Assert.Equal("150", item.Attribute("value")?.Value);

        // The inline RaceLock value must survive in the sidecar.
        var sidecarXml = XDocument.Load(_sidecarPath);
        var migrated = sidecarXml.Root!.Elements("ExtendedItemObjectProperties")
            .FirstOrDefault(e => e.Attribute("ItemStringId")?.Value == "legacy_item");
        Assert.NotNull(migrated);
        Assert.Equal("gor2", migrated!.Attribute("RaceLock")?.Value);

        // Pre-existing unrelated entries are untouched.
        var unrelated = sidecarXml.Root.Elements("ExtendedItemObjectProperties")
            .FirstOrDefault(e => e.Attribute("ItemStringId")?.Value == "other");
        Assert.NotNull(unrelated);
        Assert.Equal("human", unrelated!.Attribute("RaceLock")?.Value);
    }

    [Fact]
    public void SaveMigratesInlineCrossRefAttr_NoExistingSidecarEntry()
    {
        // The real-world scenario: user opens an armor with inline RaceLock, saves
        // without editing the RaceLock column. Prior behavior: inline stripped, sidecar
        // never written -> data loss. New behavior: migrate inline to sidecar on save.
        File.WriteAllText(_mainFilePath, """
<?xml version="1.0" encoding="utf-8"?>
<Items>
  <Item id="helmet_a" name="Helmet A" Type="HeadArmor" culture="Culture.beastmen" RaceLock="gor2" value="100" />
  <Item id="helmet_b" name="Helmet B" Type="HeadArmor" culture="Culture.beastmen" RaceLock="gor" value="100" />
  <Item id="helmet_c" name="Helmet C" Type="HeadArmor" culture="Culture.vlandia" value="100" />
</Items>
""");
        File.WriteAllText(_sidecarPath, """
<?xml version="1.0" encoding="utf-8"?>
<ExtendedItemObjectPropertiesRoot>
</ExtendedItemObjectPropertiesRoot>
""");

        var store = BuildStore();
        store.Initialize();

        // Trigger a save via an unrelated update on helmet_c (no inline RaceLock).
        store.UpdateEntry("tor_armors.xml", "helmet_c", new Dictionary<string, string?>
        {
            ["value"] = "200",
        });

        // All inline RaceLock attrs stripped from main.
        var mainXml = XDocument.Load(_mainFilePath);
        foreach (var item in mainXml.Root!.Elements("Item"))
            Assert.Null(item.Attribute("RaceLock"));

        // Both inline values migrated to sidecar.
        var sidecarXml = XDocument.Load(_sidecarPath);
        var entries = sidecarXml.Root!.Elements("ExtendedItemObjectProperties").ToList();

        var a = entries.FirstOrDefault(e => e.Attribute("ItemStringId")?.Value == "helmet_a");
        Assert.NotNull(a);
        Assert.Equal("gor2", a!.Attribute("RaceLock")?.Value);

        var b = entries.FirstOrDefault(e => e.Attribute("ItemStringId")?.Value == "helmet_b");
        Assert.NotNull(b);
        Assert.Equal("gor", b!.Attribute("RaceLock")?.Value);

        // helmet_c had no inline RaceLock; no sidecar entry needed.
        Assert.Null(entries.FirstOrDefault(e => e.Attribute("ItemStringId")?.Value == "helmet_c"));
    }

    [Fact]
    public void SaveMigratesInlineCrossRefAttr_PreservesExistingSidecarValue()
    {
        // If the sidecar already has a value for the key, it wins over the stale inline
        // value. (Scenario: user edited RaceLock through the UI, which wrote to sidecar,
        // but the stale inline attribute is still on the element.)
        File.WriteAllText(_mainFilePath, """
<?xml version="1.0" encoding="utf-8"?>
<Items>
  <Item id="helmet_a" name="Helmet A" Type="HeadArmor" culture="Culture.beastmen" RaceLock="gor2" value="100" />
</Items>
""");
        File.WriteAllText(_sidecarPath, """
<?xml version="1.0" encoding="utf-8"?>
<ExtendedItemObjectPropertiesRoot>
  <ExtendedItemObjectProperties ItemStringId="helmet_a" RaceLock="elf" />
</ExtendedItemObjectPropertiesRoot>
""");

        var store = BuildStore();
        store.Initialize();

        store.UpdateEntry("tor_armors.xml", "helmet_a", new Dictionary<string, string?>
        {
            ["value"] = "150",
        });

        var mainXml = XDocument.Load(_mainFilePath);
        Assert.Null(mainXml.Root!.Elements("Item").First().Attribute("RaceLock"));

        var sidecarXml = XDocument.Load(_sidecarPath);
        var entry = sidecarXml.Root!.Elements("ExtendedItemObjectProperties").First();
        // Sidecar value wins — not overwritten with stale inline "gor2".
        Assert.Equal("elf", entry.Attribute("RaceLock")?.Value);
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
        public StubWorkspaceService(string mainFilePath) { _mainFilePath = mainFilePath; }

        public string ConfigFilePath => Path.Combine(Path.GetTempPath(), "stub.config.json");
        public WorkspaceConfig AutoDetect() => BuildConfig();
        public WorkspaceConfig LoadConfig() => BuildConfig();
        public void SaveConfig(WorkspaceConfig config) { }

        public IReadOnlyList<XmlFileInfo> GetXmlFiles(WorkspaceConfig config) => new[]
        {
            new XmlFileInfo
            {
                FilePath = _mainFilePath,
                DisplayName = "Armors",
                Category = "Item Catalog",
                Repository = "TOR_Armory",
                RelativePath = "tor_armors.xml",
            }
        };

        public IReadOnlyList<CatalogGroup> GetCatalogs(WorkspaceConfig config) => Array.Empty<CatalogGroup>();

        public WorkspaceValidationResult ValidateWorkspace(WorkspaceConfig config) => new()
        {
            IsValid = true,
            TorCoreFound = true,
            TorArmoryFound = true,
        };

        private WorkspaceConfig BuildConfig() => new()
        {
            TorCorePath = Path.GetDirectoryName(_mainFilePath),
            TorArmoryPath = Path.GetDirectoryName(_mainFilePath),
        };
    }
}
