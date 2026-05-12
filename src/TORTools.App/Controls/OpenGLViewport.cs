using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Silk.NET.OpenGL;
using TORTools.Core.Services;

namespace TORTools.App.Controls;

/// <summary>
/// OpenGL viewport control for rendering 3D weapon parts.
/// Uses Avalonia's OpenGL context with Silk.NET for rendering.
/// </summary>
public class OpenGLViewport : OpenGlControlBase
{
    // Camera state
    private float _cameraDistance = 100f;
    private float _cameraYaw;
    private float _cameraPitch = 0.3f;
    private Vector3 _cameraTarget = Vector3.Zero;
    private float _sceneSize = 100f; // Used for zoom limits

    // Mouse interaction state
    private Point _lastMousePos;
    private bool _isRotating;
    private bool _isPanning;

    // Render state
    private GL? _gl;
    private bool _initialized;
    private uint _shaderProgram;

    // Mesh data
    private readonly List<MeshRenderData> _meshRenderData = new();
    private MeshData? _highlightedMesh;

    private class MeshRenderData
    {
        public MeshData Mesh { get; set; } = null!;
        public uint Vao { get; set; }
        public uint Vbo { get; set; }
        public uint Ebo { get; set; }
        public int IndexCount { get; set; }
        public Vector3 Offset { get; set; }
        public float Scale { get; set; } = 1.0f; // Per-mesh scale factor (1.0 = 100%)
    }

    // Shader source (GLSL ES 3.0 compatible for ANGLE)
    private const string VertexShaderSource = @"#version 300 es
        precision highp float;
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;

        out vec3 fragNormal;
        out vec3 fragPos;

        void main()
        {
            vec4 worldPos = uModel * vec4(aPosition, 1.0);
            fragPos = worldPos.xyz;
            fragNormal = mat3(transpose(inverse(uModel))) * aNormal;
            gl_Position = uProjection * uView * worldPos;
        }
    ";

    private const string FragmentShaderSource = @"#version 300 es
        precision highp float;
        in vec3 fragNormal;
        in vec3 fragPos;

        uniform vec3 uLightDir;
        uniform vec3 uLightColor;
        uniform vec3 uObjectColor;
        uniform vec3 uViewPos;
        uniform float uHighlight;

        out vec4 FragColor;

        void main()
        {
            // Ambient
            float ambient = 0.3;

            // Diffuse
            vec3 norm = normalize(fragNormal);
            float diff = max(dot(norm, -uLightDir), 0.0);

            // Specular
            vec3 viewDir = normalize(uViewPos - fragPos);
            vec3 reflectDir = reflect(uLightDir, norm);
            float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32.0) * 0.5;

            // Combine
            vec3 result = (ambient + diff + spec) * uObjectColor * uLightColor;

            // Add highlight
            if (uHighlight > 0.5)
            {
                result = mix(result, vec3(1.0, 0.8, 0.2), 0.3);
            }

            FragColor = vec4(result, 1.0);
        }
    ";

    public OpenGLViewport()
    {
        Focusable = true;
        // Ensure we receive pointer events
        IsHitTestVisible = true;
        ClipToBounds = true;
    }

    /// <summary>
    /// Public method to handle rotation input (for external event forwarding).
    /// </summary>
    public void HandleRotate(double deltaX, double deltaY)
    {
        _cameraYaw += (float)deltaX * 0.01f;
        _cameraPitch = Math.Clamp(_cameraPitch + (float)deltaY * 0.01f, -MathF.PI / 2 + 0.1f, MathF.PI / 2 - 0.1f);
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Public method to handle pan input (for external event forwarding).
    /// </summary>
    public void HandlePan(double deltaX, double deltaY)
    {
        var right = new Vector3(MathF.Cos(_cameraYaw), 0, -MathF.Sin(_cameraYaw));
        var up = Vector3.UnitY;
        var panSpeed = _cameraDistance * 0.005f;
        _cameraTarget -= right * (float)deltaX * panSpeed;
        _cameraTarget += up * (float)deltaY * panSpeed;
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Public method to handle zoom input (for external event forwarding).
    /// </summary>
    public void HandleZoom(double delta)
    {
        var zoomFactor = 1f - (float)delta * 0.1f;
        var minDist = _sceneSize * 0.1f;
        var maxDist = _sceneSize * 10f;
        _cameraDistance = Math.Clamp(_cameraDistance * zoomFactor, minDist, maxDist);
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlInit(GlInterface glInterface)
    {
        if (_initialized) return;

        try
        {
            // Create Silk.NET GL instance from Avalonia's context
            _gl = GL.GetApi(glInterface.GetProcAddress);

            // Compile shaders
            var vertexShader = CompileShader(ShaderType.VertexShader, VertexShaderSource);
            var fragmentShader = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);

            if (vertexShader == 0 || fragmentShader == 0)
            {
                System.Diagnostics.Debug.WriteLine("Failed to compile shaders");
                return;
            }

            // Create program
            _shaderProgram = _gl.CreateProgram();
            _gl.AttachShader(_shaderProgram, vertexShader);
            _gl.AttachShader(_shaderProgram, fragmentShader);
            _gl.LinkProgram(_shaderProgram);

            // Check link status
            _gl.GetProgram(_shaderProgram, ProgramPropertyARB.LinkStatus, out var linkStatus);
            if (linkStatus == 0)
            {
                var infoLog = _gl.GetProgramInfoLog(_shaderProgram);
                System.Diagnostics.Debug.WriteLine($"Shader link error: {infoLog}");
                return;
            }

            // Clean up shader objects
            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);

            _initialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenGL init error: {ex}");
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (!_initialized || _gl == null) return;

        Console.WriteLine($"[OpenGLViewport] OnOpenGlDeinit - cleaning up");

        // Clean up mesh buffers
        foreach (var data in _meshRenderData)
        {
            _gl.DeleteVertexArray(data.Vao);
            _gl.DeleteBuffer(data.Vbo);
            _gl.DeleteBuffer(data.Ebo);
        }
        _meshRenderData.Clear();
        _pendingMeshes.Clear();
        _pendingDeletions.Clear();

        if (_shaderProgram != 0)
        {
            _gl.DeleteProgram(_shaderProgram);
            _shaderProgram = 0;
        }

        _gl.Dispose();
        _gl = null;
        _initialized = false;
    }

    protected override void OnOpenGlRender(GlInterface glInterface, int fb)
    {
        // Process any pending mesh additions
        ProcessPendingMeshes();

        if (!_initialized || _gl == null) return;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = (int)(Bounds.Width * scaling);
        var height = (int)(Bounds.Height * scaling);

        if (width <= 0 || height <= 0) return;

        // Clear
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.ClearColor(0.15f, 0.15f, 0.15f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.DepthTest);
        // Disable backface culling for now - FBX normals might be flipped
        _gl.Disable(EnableCap.CullFace);

        if (_meshRenderData.Count == 0) return;

        // Use shader
        _gl.UseProgram(_shaderProgram);

        // Calculate camera position
        var cameraPos = _cameraTarget + new Vector3(
            _cameraDistance * MathF.Cos(_cameraPitch) * MathF.Sin(_cameraYaw),
            _cameraDistance * MathF.Sin(_cameraPitch),
            _cameraDistance * MathF.Cos(_cameraPitch) * MathF.Cos(_cameraYaw)
        );

        // Create matrices
        var view = Matrix4x4.CreateLookAt(cameraPos, _cameraTarget, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4f,
            (float)width / height,
            0.01f,  // Near plane - allow closer viewing
            1000f   // Far plane - allow viewing larger objects
        );

        // Set common uniforms
        SetUniformMatrix4("uView", view);
        SetUniformMatrix4("uProjection", projection);
        SetUniformVec3("uLightDir", Vector3.Normalize(new Vector3(0.5f, -1f, 0.3f)));
        SetUniformVec3("uLightColor", new Vector3(1f, 1f, 1f));
        SetUniformVec3("uViewPos", cameraPos);

        // Render each mesh
        foreach (var data in _meshRenderData)
        {
            RenderMeshData(data);
        }
    }

    // Scale factor - FBX meshes are in meters, offsets are in centimeters
    // Scale meshes by 100x to convert meters to centimeters
    private const float MeshScaleFactor = 100.0f;

    private void RenderMeshData(MeshRenderData data)
    {
        if (_gl == null) return;

        // Set model matrix with scale and mesh offset
        // Mesh geometry is in meters, offsets are in centimeters
        // Scale mesh by 100 to convert to cm, then apply per-piece scale
        // Offset is already in cm
        var totalScale = MeshScaleFactor * data.Scale;
        var model = Matrix4x4.CreateScale(totalScale) *
                    Matrix4x4.CreateTranslation(data.Offset);
        SetUniformMatrix4("uModel", model);

        // Set object color
        var color = new Vector3(0.6f, 0.6f, 0.6f);
        SetUniformVec3("uObjectColor", color);
        SetUniformFloat("uHighlight", data.Mesh == _highlightedMesh ? 1f : 0f);

        // Bind and draw
        _gl.BindVertexArray(data.Vao);
        unsafe
        {
            _gl.DrawElements(PrimitiveType.Triangles, (uint)data.IndexCount, DrawElementsType.UnsignedInt, null);
        }
        _gl.BindVertexArray(0);
    }

    private uint CompileShader(ShaderType type, string source)
    {
        if (_gl == null) return 0;

        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compileStatus);
        if (compileStatus == 0)
        {
            var infoLog = _gl.GetShaderInfoLog(shader);
            System.Diagnostics.Debug.WriteLine($"Shader compile error ({type}): {infoLog}");
            _gl.DeleteShader(shader);
            return 0;
        }

        return shader;
    }

    private void SetUniformMatrix4(string name, Matrix4x4 matrix)
    {
        if (_gl == null) return;
        var location = _gl.GetUniformLocation(_shaderProgram, name);
        if (location >= 0)
        {
            unsafe
            {
                _gl.UniformMatrix4(location, 1, false, (float*)&matrix);
            }
        }
    }

    private void SetUniformVec3(string name, Vector3 vec)
    {
        if (_gl == null) return;
        var location = _gl.GetUniformLocation(_shaderProgram, name);
        if (location >= 0)
        {
            _gl.Uniform3(location, vec.X, vec.Y, vec.Z);
        }
    }

    private void SetUniformFloat(string name, float value)
    {
        if (_gl == null) return;
        var location = _gl.GetUniformLocation(_shaderProgram, name);
        if (location >= 0)
        {
            _gl.Uniform1(location, value);
        }
    }

    private MeshRenderData CreateMeshRenderData(MeshData mesh, Vector3 offset)
    {
        if (_gl == null) throw new InvalidOperationException("GL not initialized");

        // Create VAO
        var vao = _gl.GenVertexArray();
        _gl.BindVertexArray(vao);

        // Create VBO (interleaved position + normal)
        var vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        // Interleave vertex data
        var vertexCount = mesh.VertexCount;
        var vertexData = new float[vertexCount * 6];
        for (int i = 0; i < vertexCount; i++)
        {
            vertexData[i * 6 + 0] = mesh.Vertices[i * 3 + 0];
            vertexData[i * 6 + 1] = mesh.Vertices[i * 3 + 1];
            vertexData[i * 6 + 2] = mesh.Vertices[i * 3 + 2];
            vertexData[i * 6 + 3] = mesh.Normals[i * 3 + 0];
            vertexData[i * 6 + 4] = mesh.Normals[i * 3 + 1];
            vertexData[i * 6 + 5] = mesh.Normals[i * 3 + 2];
        }

        unsafe
        {
            fixed (float* ptr = vertexData)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertexData.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        // Create EBO
        var ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);

        unsafe
        {
            fixed (uint* ptr = mesh.Indices)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(mesh.Indices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        // Set vertex attributes
        unsafe
        {
            // Position (location 0)
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            // Normal (location 1)
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
        }

        _gl.BindVertexArray(0);

        return new MeshRenderData
        {
            Mesh = mesh,
            Vao = vao,
            Vbo = vbo,
            Ebo = ebo,
            IndexCount = mesh.Indices.Length,
            Offset = offset,
            Scale = 1.0f // Default scale, will be set by caller
        };
    }

    private MeshRenderData CreateMeshRenderData(MeshData mesh, Vector3 offset, float scale)
    {
        var renderData = CreateMeshRenderData(mesh, offset);
        renderData.Scale = scale;
        return renderData;
    }

    // Public API

    /// <summary>
    /// Adds a mesh to be rendered at the specified offset with optional scale.
    /// Must be called when GL context is available (during render or via dispatcher).
    /// </summary>
    /// <param name="mesh">The mesh data to render</param>
    /// <param name="offset">Position offset in centimeters</param>
    /// <param name="scale">Scale factor (1.0 = 100%)</param>
    public void AddMesh(MeshData mesh, Vector3 offset = default, float scale = 1.0f)
    {
        Console.WriteLine($"[OpenGLViewport] AddMesh called: {mesh.MeshName}, vertices={mesh.VertexCount}, scale={scale}");

        // Store mesh data to be created during render
        _pendingMeshes.Add((mesh, offset, scale));
        RequestNextFrameRendering();
    }

    private readonly List<(MeshData mesh, Vector3 offset, float scale)> _pendingMeshes = new();
    private readonly List<MeshRenderData> _pendingDeletions = new();

    /// <summary>
    /// Removes all meshes and clears the view.
    /// </summary>
    public void ClearMeshes()
    {
        Console.WriteLine($"[OpenGLViewport] ClearMeshes called. Pending={_pendingMeshes.Count}, Rendered={_meshRenderData.Count}");

        _pendingMeshes.Clear();

        // Queue deletions to happen during render (when GL context is available)
        _pendingDeletions.AddRange(_meshRenderData);
        _meshRenderData.Clear();
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Sets the highlighted mesh.
    /// </summary>
    public void SetHighlightedMesh(MeshData? mesh)
    {
        _highlightedMesh = mesh;
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Resets the camera to default view.
    /// </summary>
    public void ResetCamera()
    {
        _cameraDistance = _sceneSize * 1.5f;
        _cameraYaw = 0f;
        _cameraPitch = 0.3f;
        // Don't reset target - keep it centered on content
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Fits the camera to show all loaded meshes.
    /// </summary>
    public void FitToContent()
    {
        if (_meshRenderData.Count == 0)
        {
            Console.WriteLine($"[OpenGLViewport] FitToContent: No meshes to fit");
            return;
        }

        var boundsMin = new Vector3(float.MaxValue);
        var boundsMax = new Vector3(float.MinValue);

        foreach (var data in _meshRenderData)
        {
            var mesh = data.Mesh;
            var offset = data.Offset;
            var totalScale = MeshScaleFactor * data.Scale;
            // Mesh bounds scaled to cm with per-piece scale, + offset (already in cm)
            var scaledMeshMin = mesh.BoundsMin * totalScale + offset;
            var scaledMeshMax = mesh.BoundsMax * totalScale + offset;
            boundsMin = Vector3.Min(boundsMin, scaledMeshMin);
            boundsMax = Vector3.Max(boundsMax, scaledMeshMax);
            Console.WriteLine($"[OpenGLViewport] Mesh {mesh.MeshName}: scaled bounds ({scaledMeshMin}) to ({scaledMeshMax}), scale={data.Scale}");
        }

        var center = (boundsMin + boundsMax) / 2;
        var size = Vector3.Distance(boundsMin, boundsMax);

        Console.WriteLine($"[OpenGLViewport] FitToContent: Total bounds ({boundsMin}) to ({boundsMax})");
        Console.WriteLine($"[OpenGLViewport] FitToContent: Center={center}, Size={size}");

        _cameraTarget = center;
        _sceneSize = Math.Max(size, 10f);
        // Set camera distance based on size - multiply by 1.5 to give some margin
        _cameraDistance = _sceneSize * 1.5f;
        _cameraPitch = 0.3f; // Slight top-down angle
        _cameraYaw = MathF.PI / 4; // 45 degree angle

        Console.WriteLine($"[OpenGLViewport] Camera: target={_cameraTarget}, distance={_cameraDistance}, sceneSize={_sceneSize}");
        RequestNextFrameRendering();
    }

    // Process pending meshes and deletions during render (when GL context is available)
    private void ProcessPendingMeshes()
    {
        if (_gl == null) return;

        // Process deletions first
        if (_pendingDeletions.Count > 0)
        {
            Console.WriteLine($"[OpenGLViewport] Processing {_pendingDeletions.Count} deletions");
            foreach (var data in _pendingDeletions)
            {
                _gl.DeleteVertexArray(data.Vao);
                _gl.DeleteBuffer(data.Vbo);
                _gl.DeleteBuffer(data.Ebo);
            }
            _pendingDeletions.Clear();
        }

        // Process new meshes
        if (_pendingMeshes.Count > 0)
        {
            Console.WriteLine($"[OpenGLViewport] Processing {_pendingMeshes.Count} new meshes");
            foreach (var (mesh, offset, scale) in _pendingMeshes)
            {
                var renderData = CreateMeshRenderData(mesh, offset, scale);
                _meshRenderData.Add(renderData);
                Console.WriteLine($"[OpenGLViewport] Created render data for {mesh.MeshName}, scale={scale}");
            }
            _pendingMeshes.Clear();
            Console.WriteLine($"[OpenGLViewport] Total meshes now: {_meshRenderData.Count}");
        }
    }

    // Mouse interaction

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);

        Console.WriteLine($"[OpenGLViewport] PointerPressed: Left={point.Properties.IsLeftButtonPressed}, Middle={point.Properties.IsMiddleButtonPressed}, Right={point.Properties.IsRightButtonPressed}");

        if (point.Properties.IsLeftButtonPressed)
        {
            _isRotating = true;
            _lastMousePos = point.Position;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
        else if (point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed)
        {
            _isPanning = true;
            _lastMousePos = point.Position;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        Focus();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        Console.WriteLine($"[OpenGLViewport] PointerReleased");
        _isRotating = false;
        _isPanning = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetCurrentPoint(this);
        var currentPos = point.Position;
        var delta = currentPos - _lastMousePos;
        _lastMousePos = currentPos;

        if (_isRotating)
        {
            _cameraYaw += (float)delta.X * 0.01f;
            _cameraPitch = Math.Clamp(_cameraPitch + (float)delta.Y * 0.01f, -MathF.PI / 2 + 0.1f, MathF.PI / 2 - 0.1f);
            RequestNextFrameRendering();
        }
        else if (_isPanning)
        {
            var right = new Vector3(MathF.Cos(_cameraYaw), 0, -MathF.Sin(_cameraYaw));
            var up = Vector3.UnitY;
            // Pan speed scales with camera distance for consistent feel
            var panSpeed = _cameraDistance * 0.005f;
            _cameraTarget -= right * (float)delta.X * panSpeed;
            _cameraTarget += up * (float)delta.Y * panSpeed;
            RequestNextFrameRendering();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        Console.WriteLine($"[OpenGLViewport] PointerWheel: Delta={e.Delta.Y}, CurrentDistance={_cameraDistance}");

        var zoomFactor = 1f - (float)e.Delta.Y * 0.1f;
        // Zoom limits relative to scene size
        var minDist = _sceneSize * 0.1f;
        var maxDist = _sceneSize * 10f;
        _cameraDistance = Math.Clamp(_cameraDistance * zoomFactor, minDist, maxDist);

        Console.WriteLine($"[OpenGLViewport] New distance={_cameraDistance}");
        RequestNextFrameRendering();
        e.Handled = true;
    }
}
