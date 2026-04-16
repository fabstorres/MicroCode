namespace MicroCode.Cli;

/// <summary>
/// Simple slash-command dispatcher for the REPL.
/// </summary>
public class CommandRegistry
{
    private readonly Dictionary<string, (string Description, Func<string[], Task<bool>> Handler)> _commands = new();

    /// <summary>
    /// Registers a command with the given name and handler.
    /// </summary>
    /// <param name="name">Command name without the leading slash.</param>
    /// <param name="description">Short description of the command.</param>
    /// <param name="handler">Handler that receives arguments and returns true to continue, false to exit.</param>
    public void Register(string name, string description, Func<string[], Task<bool>> handler)
    {
        _commands[name.ToLowerInvariant()] = (description, handler);
    }

    /// <summary>
    /// Registers a synchronous command.
    /// </summary>
    public void Register(string name, string description, Func<string[], bool> handler)
    {
        Register(name, description, args => Task.FromResult(handler(args)));
    }

    /// <summary>
    /// Tries to execute a command if the input starts with '/'.
    /// </summary>
    /// <returns>
    /// A tuple of (wasCommand, shouldContinue). If wasCommand is false, the input was not a command.
    /// If shouldContinue is false, the REPL should exit.
    /// </returns>
    public async Task<(bool WasCommand, bool ShouldContinue)> TryExecuteAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith('/'))
        {
            return (false, true);
        }

        var parts = input[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return (false, true);
        }

        var commandName = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1..] : [];

        if (_commands.TryGetValue(commandName, out var command))
        {
            var shouldContinue = await command.Handler(args);
            return (true, shouldContinue);
        }

        ConsoleDisplay.PrintError($"Unknown command: /{commandName}. Type /help for available commands.");
        return (true, true);
    }

    /// <summary>
    /// Prints help for all registered commands.
    /// </summary>
    public void PrintHelp()
    {
        ConsoleDisplay.PrintInfo("Available commands:");
        foreach (var (name, (description, _)) in _commands.OrderBy(x => x.Key))
        {
            Console.WriteLine($"  /{name,-10} - {description}");
        }
    }
}
