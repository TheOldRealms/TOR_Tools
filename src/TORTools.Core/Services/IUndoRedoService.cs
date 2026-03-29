using TORTools.Core.Commands;

namespace TORTools.Core.Services;

/// <summary>
/// Service for managing undo/redo operations.
/// </summary>
public interface IUndoRedoService
{
    /// <summary>
    /// Whether there are commands to undo.
    /// </summary>
    bool CanUndo { get; }

    /// <summary>
    /// Whether there are commands to redo.
    /// </summary>
    bool CanRedo { get; }

    /// <summary>
    /// Description of the command that would be undone.
    /// </summary>
    string? UndoDescription { get; }

    /// <summary>
    /// Description of the command that would be redone.
    /// </summary>
    string? RedoDescription { get; }

    /// <summary>
    /// Event raised when undo/redo state changes.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Executes a command and adds it to the undo stack.
    /// </summary>
    void Execute(IEditCommand command);

    /// <summary>
    /// Undoes the last command.
    /// </summary>
    void Undo();

    /// <summary>
    /// Redoes the last undone command.
    /// </summary>
    void Redo();

    /// <summary>
    /// Clears all undo/redo history.
    /// </summary>
    void Clear();
}
