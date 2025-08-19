namespace ScratchShell.UserControls.TerminalControl.Handler;

public interface IAnsiCommandHandler
{
    void Handle(string fullSequence, TerminalState state);
}