using TORTools.Core.Commands;

namespace TORTools.App.Commands;

/// <summary>
/// Wrapper for a command that has already been executed on first call.
/// First Execute() does nothing, subsequent calls delegate to inner.
/// </summary>
public class AlreadyExecutedCommand : IEditCommand
{
    private readonly IEditCommand _inner;
    private bool _firstExecute = true;

    public string Description => _inner.Description;

    public AlreadyExecutedCommand(IEditCommand inner)
    {
        _inner = inner;
    }

    public void Execute()
    {
        if (_firstExecute)
        {
            // First time - already executed by the UI
            _firstExecute = false;
            return;
        }
        // Subsequent calls (redo) - actually execute
        _inner.Execute();
    }

    public void Undo()
    {
        _inner.Undo();
    }
}
