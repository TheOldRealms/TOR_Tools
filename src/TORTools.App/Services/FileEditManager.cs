using TORTools.App.Models;
using TORTools.Core.Services;
using TORTools.Core.Schema;
using TORTools.Core.Validation;

namespace TORTools.App.Services;

/// <summary>
/// Coordinates all file editing operations.
/// This is the main business logic coordinator that orchestrates services
/// and manages the FileEditContext.
/// </summary>
public class FileEditManager
{
    private readonly ISchemaService _schemaService;
    private readonly IUndoRedoService _undoRedoService;
    private readonly FileLoaderService _fileLoaderService;
    private readonly FileSaverService _fileSaverService;
    private readonly ValidationCoordinator _validationCoordinator;
    private readonly CrossReferenceService _crossRefService;
    private readonly TupleListService _tupleListService;

    public FileEditContext Context { get; }

    public FileEditManager(
        FileEditContext context,
        ISchemaService schemaService,
        IUndoRedoService undoRedoService,
        FileLoaderService fileLoaderService,
        FileSaverService fileSaverService,
        ValidationCoordinator validationCoordinator,
        CrossReferenceService crossRefService,
        TupleListService tupleListService)
    {
        Context = context;
        _schemaService = schemaService;
        _undoRedoService = undoRedoService;
        _fileLoaderService = fileLoaderService;
        _fileSaverService = fileSaverService;
        _validationCoordinator = validationCoordinator;
        _crossRefService = crossRefService;
        _tupleListService = tupleListService;
    }

    /// <summary>
    /// Loads a file and prepares it for editing.
    /// </summary>
    public async Task LoadFileAsync(string filePath)
    {
        Context.Clear();
        Context.FilePath = filePath;

        try
        {
            // Load schema
            var fileName = Path.GetFileName(filePath);
            Context.Schema = _schemaService.GetSchema(fileName);

            // Delegate to FileLoaderService
            _fileLoaderService.LoadFile(Context);

            // Cross-references are loaded on-demand by the UI when needed
            // This keeps the loading fast and allows lazy loading of external references
        }
        catch (Exception ex)
        {
            Context.HasError = true;
            Context.ErrorMessage = $"Error loading file: {ex.Message}";
            throw;
        }
    }

    /// <summary>
    /// Saves the file.
    /// </summary>
    public void Save()
    {
        if (Context.Document == null)
            return;

        try
        {
            // Delegate to FileSaverService
            _fileSaverService.Save(Context);
        }
        catch (Exception ex)
        {
            Context.HasError = true;
            Context.ErrorMessage = $"Error saving file: {ex.Message}";
            throw;
        }
    }

    /// <summary>
    /// Runs validation on the current data.
    /// </summary>
    public async Task RunValidationAsync()
    {
        // Delegate to ValidationCoordinator
        await _validationCoordinator.RunValidationAsync(Context);
    }

    /// <summary>
    /// Marks the context as modified.
    /// </summary>
    public void MarkAsModified()
    {
        Context.HasUnsavedChanges = true;
        if (Context.Document != null)
        {
            Context.Document.HasUnsavedChanges = true;
        }
    }
}
