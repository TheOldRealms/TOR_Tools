using System.Numerics;
using Silk.NET.Assimp;

namespace TORTools.Core.Services;

/// <summary>
/// Represents a loaded 3D mesh with vertices, indices, and normals.
/// </summary>
public class MeshData
{
    /// <summary>
    /// Vertex positions (x, y, z triplets).
    /// </summary>
    public float[] Vertices { get; set; } = Array.Empty<float>();

    /// <summary>
    /// Triangle indices (3 indices per triangle).
    /// </summary>
    public uint[] Indices { get; set; } = Array.Empty<uint>();

    /// <summary>
    /// Vertex normals (x, y, z triplets, same count as vertices).
    /// </summary>
    public float[] Normals { get; set; } = Array.Empty<float>();

    /// <summary>
    /// Axis-aligned bounding box minimum point.
    /// </summary>
    public Vector3 BoundsMin { get; set; }

    /// <summary>
    /// Axis-aligned bounding box maximum point.
    /// </summary>
    public Vector3 BoundsMax { get; set; }

    /// <summary>
    /// The mesh name as found in the FBX file.
    /// </summary>
    public string MeshName { get; set; } = string.Empty;

    /// <summary>
    /// Number of triangles in this mesh.
    /// </summary>
    public int TriangleCount => Indices.Length / 3;

    /// <summary>
    /// Number of vertices in this mesh.
    /// </summary>
    public int VertexCount => Vertices.Length / 3;

    /// <summary>
    /// Gets the center of the bounding box.
    /// </summary>
    public Vector3 Center => (BoundsMin + BoundsMax) / 2;

    /// <summary>
    /// Gets the size of the bounding box.
    /// </summary>
    public Vector3 Size => BoundsMax - BoundsMin;
}

/// <summary>
/// Service for loading FBX mesh files using Assimp.
/// Caches loaded meshes for performance.
/// </summary>
public class FbxLoaderService : IDisposable
{
    private readonly Dictionary<string, MeshData> _meshCache = new();
    private readonly Dictionary<string, string> _meshToFbxPathIndex = new();
    private readonly Assimp _assimp;
    private bool _indexBuilt;
    private string? _assetSourcesPath;
    private bool _disposed;

    public FbxLoaderService()
    {
        _assimp = Assimp.GetApi();
    }

    /// <summary>
    /// Initializes the service with the path to asset sources.
    /// Builds a fast filename-based index (no FBX parsing at startup).
    /// </summary>
    public void Initialize(string assetSourcesPath)
    {
        Console.WriteLine($"[FbxLoader] Initialize called with path: {assetSourcesPath}");
        Console.WriteLine($"[FbxLoader] Path exists: {Directory.Exists(assetSourcesPath)}");
        _assetSourcesPath = assetSourcesPath;
        BuildFilenameIndex();
        Console.WriteLine($"[FbxLoader] Filename index built with {_meshToFbxPathIndex.Count} entries");
    }

    /// <summary>
    /// Builds a fast index mapping potential mesh names to FBX file paths.
    /// Uses filenames only - no FBX parsing required (instant startup).
    /// </summary>
    private void BuildFilenameIndex()
    {
        if (_indexBuilt || string.IsNullOrEmpty(_assetSourcesPath))
            return;

        _meshToFbxPathIndex.Clear();

        if (!Directory.Exists(_assetSourcesPath))
            return;

        // Just scan filenames - no FBX parsing needed!
        // Mesh names typically match the FBX filename (e.g., "reiksguard_knight_sword_001_blade.fbx")
        foreach (var fbxPath in Directory.EnumerateFiles(_assetSourcesPath, "*.fbx", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileNameWithoutExtension(fbxPath);
            if (!string.IsNullOrEmpty(fileName))
            {
                // Index by filename (most common case)
                if (!_meshToFbxPathIndex.ContainsKey(fileName))
                {
                    _meshToFbxPathIndex[fileName] = fbxPath;
                }
            }
        }

        _indexBuilt = true;
    }

    /// <summary>
    /// Gets all mesh names from an FBX file without loading full geometry.
    /// </summary>
    private unsafe List<string> GetMeshNamesFromFbx(string fbxPath)
    {
        var meshNames = new List<string>();

        var scene = _assimp.ImportFile(fbxPath, (uint)PostProcessSteps.Triangulate);
        if (scene == null || scene->MNumMeshes == 0)
            return meshNames;

        try
        {
            for (uint i = 0; i < scene->MNumMeshes; i++)
            {
                var mesh = scene->MMeshes[i];
                if (mesh != null && mesh->MName.Length > 0)
                {
                    var name = mesh->MName.AsString;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        meshNames.Add(name);
                    }
                }
            }
        }
        finally
        {
            _assimp.ReleaseImport(scene);
        }

        return meshNames;
    }

    /// <summary>
    /// Loads a mesh by name. Returns cached data if already loaded.
    /// </summary>
    /// <param name="meshName">The mesh name as specified in crafting_pieces.xml</param>
    /// <returns>The loaded mesh data, or null if not found</returns>
    public MeshData? LoadMesh(string meshName)
    {
        if (string.IsNullOrEmpty(meshName))
        {
            Console.WriteLine($"[FbxLoader] LoadMesh called with empty name");
            return null;
        }

        Console.WriteLine($"[FbxLoader] LoadMesh requested: {meshName}");

        // Check cache first
        if (_meshCache.TryGetValue(meshName, out var cachedMesh))
        {
            Console.WriteLine($"[FbxLoader] Found in cache: {meshName}");
            return cachedMesh;
        }

        // Find the FBX file containing this mesh
        if (!_meshToFbxPathIndex.TryGetValue(meshName, out var fbxPath))
        {
            Console.WriteLine($"[FbxLoader] Not in index, trying file search for: {meshName}");
            // Try direct file path as fallback
            fbxPath = FindFbxByMeshName(meshName);
            if (fbxPath == null)
            {
                Console.WriteLine($"[FbxLoader] Could not find FBX for mesh: {meshName}");
                return null;
            }
        }

        Console.WriteLine($"[FbxLoader] Loading from: {fbxPath}");

        // Load the specific mesh from the FBX file
        var mesh = LoadMeshFromFbx(fbxPath, meshName);
        if (mesh != null)
        {
            Console.WriteLine($"[FbxLoader] Loaded mesh: {meshName} with {mesh.VertexCount} vertices, {mesh.TriangleCount} triangles");
            _meshCache[meshName] = mesh;
        }
        else
        {
            Console.WriteLine($"[FbxLoader] Failed to load mesh from FBX: {meshName}");
        }

        return mesh;
    }

    /// <summary>
    /// Loads all meshes from an FBX file.
    /// </summary>
    public List<MeshData> LoadAllMeshesFromFbx(string fbxPath)
    {
        var meshes = new List<MeshData>();

        unsafe
        {
            var scene = _assimp.ImportFile(fbxPath,
                (uint)(PostProcessSteps.Triangulate |
                       PostProcessSteps.GenerateNormals |
                       PostProcessSteps.JoinIdenticalVertices));

            if (scene == null || scene->MNumMeshes == 0)
                return meshes;

            try
            {
                for (uint i = 0; i < scene->MNumMeshes; i++)
                {
                    var mesh = ExtractMeshData(scene->MMeshes[i]);
                    if (mesh != null)
                    {
                        meshes.Add(mesh);
                    }
                }
            }
            finally
            {
                _assimp.ReleaseImport(scene);
            }
        }

        return meshes;
    }

    /// <summary>
    /// Loads a specific mesh from an FBX file by name.
    /// </summary>
    private unsafe MeshData? LoadMeshFromFbx(string fbxPath, string meshName)
    {
        var scene = _assimp.ImportFile(fbxPath,
            (uint)(PostProcessSteps.Triangulate |
                   PostProcessSteps.GenerateNormals |
                   PostProcessSteps.JoinIdenticalVertices));

        if (scene == null || scene->MNumMeshes == 0)
            return null;

        try
        {
            // Find the mesh with matching name
            for (uint i = 0; i < scene->MNumMeshes; i++)
            {
                var mesh = scene->MMeshes[i];
                if (mesh != null && mesh->MName.AsString == meshName)
                {
                    return ExtractMeshData(mesh);
                }
            }

            // If exact name not found, try partial match
            for (uint i = 0; i < scene->MNumMeshes; i++)
            {
                var mesh = scene->MMeshes[i];
                if (mesh != null && mesh->MName.AsString.Contains(meshName, StringComparison.OrdinalIgnoreCase))
                {
                    return ExtractMeshData(mesh);
                }
            }

            // Return first mesh if no match found (fallback for simple files)
            if (scene->MNumMeshes > 0)
            {
                return ExtractMeshData(scene->MMeshes[0]);
            }
        }
        finally
        {
            _assimp.ReleaseImport(scene);
        }

        return null;
    }

    /// <summary>
    /// Extracts mesh data from an Assimp mesh structure.
    /// </summary>
    private unsafe MeshData? ExtractMeshData(Mesh* mesh)
    {
        if (mesh == null || mesh->MNumVertices == 0)
            return null;

        var vertexCount = (int)mesh->MNumVertices;
        var vertices = new float[vertexCount * 3];
        var normals = new float[vertexCount * 3];

        var boundsMin = new Vector3(float.MaxValue);
        var boundsMax = new Vector3(float.MinValue);

        // Extract vertices and normals
        for (int i = 0; i < vertexCount; i++)
        {
            var v = mesh->MVertices[i];
            vertices[i * 3] = v.X;
            vertices[i * 3 + 1] = v.Y;
            vertices[i * 3 + 2] = v.Z;

            // Update bounds
            boundsMin = Vector3.Min(boundsMin, new Vector3(v.X, v.Y, v.Z));
            boundsMax = Vector3.Max(boundsMax, new Vector3(v.X, v.Y, v.Z));

            if (mesh->MNormals != null)
            {
                var n = mesh->MNormals[i];
                normals[i * 3] = n.X;
                normals[i * 3 + 1] = n.Y;
                normals[i * 3 + 2] = n.Z;
            }
            else
            {
                // Default up normal if none provided
                normals[i * 3] = 0;
                normals[i * 3 + 1] = 1;
                normals[i * 3 + 2] = 0;
            }
        }

        // Extract indices
        var indexCount = 0;
        for (uint i = 0; i < mesh->MNumFaces; i++)
        {
            indexCount += (int)mesh->MFaces[i].MNumIndices;
        }

        var indices = new uint[indexCount];
        var indexOffset = 0;

        for (uint i = 0; i < mesh->MNumFaces; i++)
        {
            var face = mesh->MFaces[i];
            for (uint j = 0; j < face.MNumIndices; j++)
            {
                indices[indexOffset++] = face.MIndices[j];
            }
        }

        return new MeshData
        {
            MeshName = mesh->MName.AsString ?? string.Empty,
            Vertices = vertices,
            Normals = normals,
            Indices = indices,
            BoundsMin = boundsMin,
            BoundsMax = boundsMax
        };
    }

    /// <summary>
    /// Attempts to find an FBX file by mesh name using file system search.
    /// </summary>
    private string? FindFbxByMeshName(string meshName)
    {
        if (string.IsNullOrEmpty(_assetSourcesPath))
            return null;

        // Mesh names are like: we_glade_guard_sword_blade_001, bretonnian_sword_001_guard
        // FBX files might be: we_glade_guard_swords.fbx, bretonnian_sword_001.fbx
        // Strategy: try progressively shorter prefixes

        // First, try exact and contains matches
        var searchPatterns = new[]
        {
            $"{meshName}.fbx",
            $"*{meshName}*.fbx"
        };

        foreach (var pattern in searchPatterns)
        {
            try
            {
                var files = Directory.GetFiles(_assetSourcesPath, pattern, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    Console.WriteLine($"[FbxLoader] Found via pattern '{pattern}': {files[0]}");
                    return files[0];
                }
            }
            catch
            {
                // Ignore search errors
            }
        }

        // Try extracting base name by removing part suffixes (_blade, _handle, _guard, _pommel, _001, etc.)
        var baseName = meshName;
        var partSuffixes = new[] { "_blade", "_handle", "_guard", "_pommel" };
        foreach (var suffix in partSuffixes)
        {
            var idx = baseName.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                baseName = baseName.Substring(0, idx);
                break;
            }
        }

        // Remove trailing numbers like _001, _002
        while (baseName.Length > 4 && baseName[^4] == '_' && char.IsDigit(baseName[^3]) && char.IsDigit(baseName[^2]) && char.IsDigit(baseName[^1]))
        {
            baseName = baseName.Substring(0, baseName.Length - 4);
        }

        if (baseName != meshName)
        {
            Console.WriteLine($"[FbxLoader] Trying base name: {baseName}");
            var basePatterns = new[]
            {
                $"{baseName}.fbx",
                $"{baseName}s.fbx",  // Try plural (sword -> swords)
                $"*{baseName}*.fbx"
            };

            foreach (var pattern in basePatterns)
            {
                try
                {
                    var files = Directory.GetFiles(_assetSourcesPath, pattern, SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        Console.WriteLine($"[FbxLoader] Found via base pattern '{pattern}': {files[0]}");
                        return files[0];
                    }
                }
                catch
                {
                    // Ignore search errors
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Clears the mesh cache to free memory.
    /// </summary>
    public void ClearCache()
    {
        _meshCache.Clear();
    }

    /// <summary>
    /// Gets all indexed mesh names.
    /// </summary>
    public IEnumerable<string> GetAvailableMeshNames()
    {
        return _meshToFbxPathIndex.Keys;
    }

    /// <summary>
    /// Checks if a mesh is available in the index.
    /// </summary>
    public bool IsMeshAvailable(string meshName)
    {
        return _meshToFbxPathIndex.ContainsKey(meshName) || _meshCache.ContainsKey(meshName);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _meshCache.Clear();
        _meshToFbxPathIndex.Clear();
        _assimp.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
