using System.ComponentModel;
using System.Xml.Linq;
using ModelContextProtocol.Server;
using TORTools.Core.Workspace;

namespace TORTools.Mcp.Host.Tools;

/// <summary>
/// MCP tools for inspecting Bannerlord scene.xscene files.
///
/// Scope: read-only. Scenes are authored in the Bannerlord editor, so these
/// endpoints exist to let callers skim scene content without opening the
/// editor (positions, prefab counts, script instance details, retreat-zone
/// vs. battle-set distances for the ambush workflow). No edit methods —
/// scenes should always flow back through the editor.
///
/// Resolves scenes under &lt;bannerlord_root&gt;/Modules/*/SceneObj/*/scene.xscene,
/// where bannerlord_root is derived from the workspace's TorCorePath.
/// </summary>
[McpServerToolType]
public class ScenesTools(IWorkspaceService workspaceService)
{
    private string? ResolveBannerlordRoot()
    {
        var config = workspaceService.LoadConfig();
        if (string.IsNullOrEmpty(config.TorCorePath)) return null;
        var modulesDir = Path.GetDirectoryName(config.TorCorePath.TrimEnd('/', '\\'));
        if (string.IsNullOrEmpty(modulesDir)) return null;
        var bannerlordRoot = Path.GetDirectoryName(modulesDir);
        return bannerlordRoot;
    }

    private IEnumerable<string> IterSceneFiles(string bannerlordRoot)
    {
        var modulesDir = Path.Combine(bannerlordRoot, "Modules");
        if (!Directory.Exists(modulesDir)) yield break;
        foreach (var moduleDir in Directory.EnumerateDirectories(modulesDir).OrderBy(d => d))
        {
            var sceneObj = Path.Combine(moduleDir, "SceneObj");
            if (!Directory.Exists(sceneObj)) continue;
            foreach (var sceneDir in Directory.EnumerateDirectories(sceneObj).OrderBy(d => d))
            {
                var scene = Path.Combine(sceneDir, "scene.xscene");
                if (File.Exists(scene)) yield return scene;
            }
        }
    }

    private string? ResolveScenePath(string sceneName, string bannerlordRoot)
    {
        if (File.Exists(sceneName) && sceneName.EndsWith(".xscene")) return sceneName;
        foreach (var f in IterSceneFiles(bannerlordRoot))
        {
            var sceneDir = Path.GetFileName(Path.GetDirectoryName(f));
            if (sceneDir == sceneName) return f;
        }
        return null;
    }

    private static (double x, double y, double z)? ParseVec3(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split(',');
        if (parts.Length < 3) return null;
        if (!double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)) return null;
        if (!double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)) return null;
        if (!double.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z)) return null;
        return (x, y, z);
    }

    private static List<SceneEntity> CollectEntities(XElement root)
    {
        var list = new List<SceneEntity>();
        foreach (var ge in root.Descendants("game_entity"))
        {
            var tr = ge.Element("transform");
            var pos = ParseVec3((string?)tr?.Attribute("position"));
            var scripts = new List<SceneScript>();
            foreach (var s in ge.Elements("scripts").Elements("script"))
            {
                var vars = new Dictionary<string, string>();
                foreach (var v in s.Elements("variables").Elements("variable"))
                {
                    var vn = (string?)v.Attribute("name");
                    var vv = (string?)v.Attribute("value");
                    if (vn != null) vars[vn] = vv ?? "";
                }
                scripts.Add(new SceneScript { Name = (string?)s.Attribute("name") ?? "", Variables = vars });
            }
            list.Add(new SceneEntity
            {
                Prefab = (string?)ge.Attribute("prefab") ?? "",
                Name = (string?)ge.Attribute("name") ?? "",
                Tag = (string?)ge.Attribute("tag") ?? "",
                Position = pos.HasValue ? new double[] { pos.Value.x, pos.Value.y, pos.Value.z } : null,
                Scripts = scripts,
            });
        }
        return list;
    }

    private static bool MatchFilter(SceneEntity e, string? prefab, string? name, string? tag, string? script)
    {
        if (!string.IsNullOrEmpty(prefab) && !e.Prefab.Contains(prefab!, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(name) && !e.Name.Contains(name!, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(tag) && !e.Tag.Contains(tag!, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(script) && !e.Scripts.Any(s => s.Name.Contains(script!, StringComparison.OrdinalIgnoreCase))) return false;
        return true;
    }

    private static bool MatchVarFilter(SceneEntity e, string? varName, string? varValueContains)
    {
        if (string.IsNullOrEmpty(varName)) return true;
        foreach (var s in e.Scripts)
        {
            if (!s.Variables.TryGetValue(varName!, out var v)) continue;
            if (string.IsNullOrEmpty(varValueContains)) return true;
            if ((v ?? "").Contains(varValueContains!, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool MatchRadius(SceneEntity e, double? x, double? y, double? z, double? radius)
    {
        if (x == null || y == null || z == null || radius == null) return true;
        if (e.Position == null) return false;
        var center = new double[] { x.Value, y.Value, z.Value };
        return Distance(e.Position, center) <= radius.Value;
    }

    [McpServerTool, Description("List every scene.xscene across all modules. Optional filters: module (substring, e.g. 'TOR_Core'), name (substring on the scene directory name), has_script (name of a <script> that must appear in the scene).")]
    public SceneListResult scene_list(
        [Description("Substring filter on the module directory name, e.g. 'TOR_Environment'. Omit for all modules.")]
        string? module = null,
        [Description("Substring filter on the scene directory name, e.g. 'ambush' matches 'test_ambush'. Omit for all.")]
        string? name = null,
        [Description("Only include scenes whose XML contains a <script name=\"<has_script>\"> somewhere. Omit for no filter.")]
        string? has_script = null,
        [Description("Max rows (default 200).")]
        int limit = 200)
    {
        var root = ResolveBannerlordRoot();
        if (root == null) return new SceneListResult { Error = "Bannerlord root not resolvable from TorCorePath" };

        var rows = new List<SceneRef>();
        foreach (var scenePath in IterSceneFiles(root))
        {
            var moduleName = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(scenePath)!)) ?? "";
            var sceneName = Path.GetFileName(Path.GetDirectoryName(scenePath)!) ?? "";
            if (!string.IsNullOrEmpty(module) && !moduleName.Contains(module!, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(name) && !sceneName.Contains(name!, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(has_script))
            {
                try
                {
                    var doc = XDocument.Load(scenePath);
                    if (!doc.Descendants("script").Any(s => (string?)s.Attribute("name") == has_script)) continue;
                }
                catch { continue; }
            }
            rows.Add(new SceneRef { Scene = sceneName, Module = moduleName, Path = scenePath });
            if (rows.Count >= limit) break;
        }
        return new SceneListResult { Scenes = rows, Count = rows.Count };
    }

    [McpServerTool, Description("Summarize a scene: metadata, entity count, prefab histogram, script instances, and ambush diagnostics (battle_set / flee-line presence, retreat-zone position + distance to battle set).")]
    public SceneSummaryResult scene_summary(
        [Description("Scene directory name (e.g. 'test_ambush') or full path to scene.xscene.")]
        string scene,
        [Description("If true, include the full per-script detail list (vars + positions). Default false.")]
        bool include_scripts = false)
    {
        var bannerlordRoot = ResolveBannerlordRoot();
        if (bannerlordRoot == null) return new SceneSummaryResult { Error = "Bannerlord root not resolvable" };
        var path = ResolveScenePath(scene, bannerlordRoot);
        if (path == null) return new SceneSummaryResult { Error = $"Scene not found: {scene}" };

        XDocument doc;
        try { doc = XDocument.Load(path); }
        catch (Exception ex) { return new SceneSummaryResult { Error = ex.Message }; }

        var root = doc.Root!;
        var entities = CollectEntities(root);

        var prefabHist = entities.Where(e => !string.IsNullOrEmpty(e.Prefab))
            .GroupBy(e => e.Prefab)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        var scriptHist = entities.SelectMany(e => e.Scripts)
            .GroupBy(s => s.Name)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        var ambushZones = entities
            .SelectMany(e => e.Scripts.Where(s => s.Name == "AmbushRetreatZone").Select(s => new { entity = e, script = s }))
            .ToList();
        var battleSets = entities.Where(e => e.Prefab == "sp_battle_set").ToList();
        var fleeAttackers = entities.Where(e => e.Prefab == "flee_line_attacker").ToList();
        var fleeDefenders = entities.Where(e => e.Prefab == "flee_line_defender").ToList();

        var diagnostics = new List<string>();
        if (battleSets.Count == 0)
            diagnostics.Add("MISSING sp_battle_set — field-battle spawn hierarchy absent; engine falls back to map-edge defaults.");
        else
            foreach (var bs in battleSets)
                diagnostics.Add($"sp_battle_set at {FormatPos(bs.Position)}");
        foreach (var fa in fleeAttackers) diagnostics.Add($"flee_line_attacker at {FormatPos(fa.Position)}");
        foreach (var fd in fleeDefenders) diagnostics.Add($"flee_line_defender at {FormatPos(fd.Position)}");
        if (ambushZones.Count == 0)
            diagnostics.Add("No AmbushRetreatZone scripts; scene relies on procedural fallback.");
        foreach (var z in ambushZones)
        {
            z.script.Variables.TryGetValue("Radius", out var radius);
            z.script.Variables.TryGetValue("ShowDebugMarkerInGame", out var debug);
            diagnostics.Add($"AmbushRetreatZone at {FormatPos(z.entity.Position)}  Radius={radius ?? "?"}  ShowDebugMarkerInGame={debug ?? "?"}");
            if (debug == "true") diagnostics.Add("  WARNING: debug marker enabled — strip before shipping.");
            if (battleSets.Count > 0 && z.entity.Position != null && battleSets[0].Position != null)
            {
                var d = Distance(z.entity.Position, battleSets[0].Position!);
                diagnostics.Add($"  distance to sp_battle_set: {d:F1}m");
                if (d > 150) diagnostics.Add($"  WARNING: zone is >150m from the battle set — likely off playable terrain.");
            }
        }

        return new SceneSummaryResult
        {
            Scene = Path.GetFileName(Path.GetDirectoryName(path)!) ?? "",
            Path = path,
            Version = (string?)root.Attribute("version") ?? "",
            Revision = (string?)root.Attribute("revision") ?? "",
            UniqueToken = (string?)root.Attribute("unique_token") ?? "",
            EntityCount = entities.Count,
            PrefabHistogram = prefabHist,
            ScriptHistogram = scriptHist,
            AmbushDiagnostics = diagnostics,
            Scripts = include_scripts
                ? entities.SelectMany(e => e.Scripts.Select(s => new SceneScriptDetail
                {
                    Name = s.Name,
                    OnPrefab = e.Prefab,
                    OnName = e.Name,
                    Position = e.Position,
                    Variables = s.Variables,
                })).ToList()
                : null,
        };
    }

    [McpServerTool, Description("List entities in a scene, optionally narrowed by substring filters on prefab/name/tag/script, by script-variable match, or by proximity to a point. All filters AND-combine; omit any filter to leave it unconstrained.")]
    public SceneEntitiesResult scene_entities(
        [Description("Scene directory name (e.g. 'test_ambush') or full path to scene.xscene.")]
        string scene,
        [Description("Substring filter on the entity's prefab attribute, e.g. 'sp_battle_set' or 'flee_line'.")]
        string? prefab = null,
        [Description("Substring filter on the entity's name attribute.")]
        string? name = null,
        [Description("Substring filter on the entity's tag attribute.")]
        string? tag = null,
        [Description("Substring filter on an attached script's name, e.g. 'AmbushRetreatZone' or 'TacticalPosition'.")]
        string? script = null,
        [Description("Match a script variable by exact name, e.g. 'Radius' or 'ShowDebugMarkerInGame'. Combine with script_var_value_contains to also constrain the value.")]
        string? script_var = null,
        [Description("Only match if the variable named in script_var contains this substring. Omit for 'any value' / presence match.")]
        string? script_var_value_contains = null,
        [Description("Center of a proximity filter; combine with near_y, near_z, near_radius. Omit to disable.")]
        double? near_x = null,
        [Description("See near_x.")] double? near_y = null,
        [Description("See near_x.")] double? near_z = null,
        [Description("Radius in metres for the near_x/y/z filter. Required if any near_* is set.")]
        double? near_radius = null,
        [Description("Max rows (default 200).")]
        int limit = 200)
    {
        var bannerlordRoot = ResolveBannerlordRoot();
        if (bannerlordRoot == null) return new SceneEntitiesResult { Error = "Bannerlord root not resolvable" };
        var path = ResolveScenePath(scene, bannerlordRoot);
        if (path == null) return new SceneEntitiesResult { Error = $"Scene not found: {scene}" };

        XDocument doc;
        try { doc = XDocument.Load(path); }
        catch (Exception ex) { return new SceneEntitiesResult { Error = ex.Message }; }

        var entities = CollectEntities(doc.Root!);
        var matches = entities
            .Where(e => MatchFilter(e, prefab, name, tag, script))
            .Where(e => MatchVarFilter(e, script_var, script_var_value_contains))
            .Where(e => MatchRadius(e, near_x, near_y, near_z, near_radius))
            .Take(limit).ToList();
        return new SceneEntitiesResult { Entities = matches, Count = matches.Count, TotalInScene = entities.Count };
    }

    [McpServerTool, Description("Tabulate script-instance counts grouped by the owning entity's prefab. Siege-debug lens: 'which prefab carries most of the script load, and which scripts are on it'. Optional proximity filter to zoom in on one broken area.")]
    public SceneScriptsByPrefabResult scene_scripts_by_prefab(
        [Description("Scene directory name or full path.")]
        string scene,
        [Description("Center of an optional proximity filter; combine with near_y, near_z, near_radius.")]
        double? near_x = null,
        [Description("See near_x.")] double? near_y = null,
        [Description("See near_x.")] double? near_z = null,
        [Description("Radius in metres for the near_x/y/z filter.")]
        double? near_radius = null,
        [Description("Max prefab groups returned (default 100).")]
        int limit = 100)
    {
        var bannerlordRoot = ResolveBannerlordRoot();
        if (bannerlordRoot == null) return new SceneScriptsByPrefabResult { Error = "Bannerlord root not resolvable" };
        var path = ResolveScenePath(scene, bannerlordRoot);
        if (path == null) return new SceneScriptsByPrefabResult { Error = $"Scene not found: {scene}" };

        XDocument doc;
        try { doc = XDocument.Load(path); }
        catch (Exception ex) { return new SceneScriptsByPrefabResult { Error = ex.Message }; }

        var entities = CollectEntities(doc.Root!)
            .Where(e => e.Scripts.Count > 0)
            .Where(e => MatchRadius(e, near_x, near_y, near_z, near_radius))
            .ToList();

        var groups = entities
            .GroupBy(e => string.IsNullOrEmpty(e.Prefab) ? (string.IsNullOrEmpty(e.Name) ? (string.IsNullOrEmpty(e.Tag) ? "<untagged>" : e.Tag) : e.Name) : e.Prefab)
            .Select(g => new ScriptsByPrefabGroup
            {
                Prefab = g.Key,
                EntityCount = g.Count(),
                ScriptCount = g.Sum(e => e.Scripts.Count),
                Scripts = g.SelectMany(e => e.Scripts.Select(s => s.Name))
                    .GroupBy(n => n)
                    .ToDictionary(x => x.Key, x => x.Count()),
                SamplePosition = g.First().Position,
            })
            .OrderByDescending(g => g.ScriptCount)
            .ThenBy(g => g.Prefab)
            .Take(limit)
            .ToList();

        return new SceneScriptsByPrefabResult { Groups = groups, DistinctPrefabs = groups.Count };
    }

    [McpServerTool, Description("Compute the distance (in metres) between two entities in a scene, selected by substring filters. Picks the first match on each side. Also returns both positions.")]
    public SceneDistanceResult scene_distance(
        [Description("Scene directory name or full path.")]
        string scene,
        [Description("Filter for the first entity: 'prefab:<v>' / 'name:<v>' / 'tag:<v>' / 'script:<v>'.")]
        string from_filter,
        [Description("Filter for the second entity: same syntax as from_filter.")]
        string to_filter)
    {
        var bannerlordRoot = ResolveBannerlordRoot();
        if (bannerlordRoot == null) return new SceneDistanceResult { Error = "Bannerlord root not resolvable" };
        var path = ResolveScenePath(scene, bannerlordRoot);
        if (path == null) return new SceneDistanceResult { Error = $"Scene not found: {scene}" };

        XDocument doc;
        try { doc = XDocument.Load(path); }
        catch (Exception ex) { return new SceneDistanceResult { Error = ex.Message }; }

        var entities = CollectEntities(doc.Root!);
        var fromMatch = FindByFilterSpec(entities, from_filter);
        var toMatch = FindByFilterSpec(entities, to_filter);
        if (fromMatch == null) return new SceneDistanceResult { Error = $"No entity matched: {from_filter}" };
        if (toMatch == null) return new SceneDistanceResult { Error = $"No entity matched: {to_filter}" };
        if (fromMatch.Position == null || toMatch.Position == null)
            return new SceneDistanceResult { Error = "One or both matched entities have no position." };

        return new SceneDistanceResult
        {
            From = fromMatch,
            To = toMatch,
            DistanceMetres = Distance(fromMatch.Position, toMatch.Position),
        };
    }

    private static SceneEntity? FindByFilterSpec(List<SceneEntity> entities, string spec)
    {
        var ix = spec.IndexOf(':');
        if (ix < 0) return null;
        var key = spec.Substring(0, ix).Trim();
        var val = spec.Substring(ix + 1).Trim();
        return key switch
        {
            "prefab" => entities.FirstOrDefault(e => MatchFilter(e, val, null, null, null)),
            "name" => entities.FirstOrDefault(e => MatchFilter(e, null, val, null, null)),
            "tag" => entities.FirstOrDefault(e => MatchFilter(e, null, null, val, null)),
            "script" => entities.FirstOrDefault(e => MatchFilter(e, null, null, null, val)),
            _ => null,
        };
    }

    private static double Distance(double[] a, double[] b)
    {
        var dx = a[0] - b[0]; var dy = a[1] - b[1]; var dz = a[2] - b[2];
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static string FormatPos(double[]? p)
        => p == null ? "(no pos)" : $"({p[0]:F1}, {p[1]:F1}, {p[2]:F1})";
}

public record SceneRef
{
    public string Scene { get; init; } = "";
    public string Module { get; init; } = "";
    public string Path { get; init; } = "";
}

public record SceneListResult
{
    public List<SceneRef> Scenes { get; init; } = new();
    public int Count { get; init; }
    public string? Error { get; init; }
}

public record SceneScript
{
    public string Name { get; init; } = "";
    public Dictionary<string, string> Variables { get; init; } = new();
}

public record SceneEntity
{
    public string Prefab { get; init; } = "";
    public string Name { get; init; } = "";
    public string Tag { get; init; } = "";
    public double[]? Position { get; init; }
    public List<SceneScript> Scripts { get; init; } = new();
}

public record SceneScriptDetail
{
    public string Name { get; init; } = "";
    public string OnPrefab { get; init; } = "";
    public string OnName { get; init; } = "";
    public double[]? Position { get; init; }
    public Dictionary<string, string> Variables { get; init; } = new();
}

public record SceneSummaryResult
{
    public string Scene { get; init; } = "";
    public string Path { get; init; } = "";
    public string Version { get; init; } = "";
    public string Revision { get; init; } = "";
    public string UniqueToken { get; init; } = "";
    public int EntityCount { get; init; }
    public Dictionary<string, int> PrefabHistogram { get; init; } = new();
    public Dictionary<string, int> ScriptHistogram { get; init; } = new();
    public List<string> AmbushDiagnostics { get; init; } = new();
    public List<SceneScriptDetail>? Scripts { get; init; }
    public string? Error { get; init; }
}

public record SceneEntitiesResult
{
    public List<SceneEntity> Entities { get; init; } = new();
    public int Count { get; init; }
    public int TotalInScene { get; init; }
    public string? Error { get; init; }
}

public record SceneDistanceResult
{
    public SceneEntity? From { get; init; }
    public SceneEntity? To { get; init; }
    public double DistanceMetres { get; init; }
    public string? Error { get; init; }
}

public record ScriptsByPrefabGroup
{
    public string Prefab { get; init; } = "";
    public int EntityCount { get; init; }
    public int ScriptCount { get; init; }
    public Dictionary<string, int> Scripts { get; init; } = new();
    public double[]? SamplePosition { get; init; }
}

public record SceneScriptsByPrefabResult
{
    public List<ScriptsByPrefabGroup> Groups { get; init; } = new();
    public int DistinctPrefabs { get; init; }
    public string? Error { get; init; }
}
