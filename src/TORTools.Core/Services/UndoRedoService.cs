using TORTools.Core.Commands;

namespace TORTools.Core.Services;

/// <summary>
/// Service for managing undo/redo operations using command stacks.
/// </summary>
public class UndoRedoService : IUndoRedoService
{
    private readonly Stack<IEditCommand> _undoStack = new();
    private readonly Stack<IEditCommand> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public string? UndoDescription => _undoStack.TryPeek(out var cmd) ? cmd.Description : null;
    public string? RedoDescription => _redoStack.TryPeek(out var cmd) ? cmd.Description : null;

    public event EventHandler? StateChanged;

    public void Execute(IEditCommand command)
    {
        command.Execute();
        _undoStack.Push(command);

        // Clear redo stack when a new command is executed
        _redoStack.Clear();

        OnStateChanged();
    }

    public void Undo()
    {
        if (!CanUndo)
            return;

        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);

        OnStateChanged();
    }

    public void Redo()
    {
        if (!CanRedo)
            return;

        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);

        OnStateChanged();
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        OnStateChanged();
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
