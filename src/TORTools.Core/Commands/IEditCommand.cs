namespace TORTools.Core.Commands;

/// <summary>
/// Represents a reversible edit operation.
/// </summary>
public interface IEditCommand
{
    /// <summary>
    /// A human-readable description of the command for display in Edit menu.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Executes the command.
    /// </summary>
    void Execute();

    /// <summary>
    /// Reverses the command.
    /// </summary>
    void Undo();
}
