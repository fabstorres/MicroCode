namespace MicroCode.Cli;

/// <summary>
/// Metadata used to render slash-command suggestions.
/// </summary>
/// <param name="Name">Command name without the leading slash.</param>
/// <param name="Description">Short user-facing command description.</param>
/// <param name="Usage">Optional usage string for commands that accept arguments.</param>
public sealed record SlashCommandInfo(
    string Name,
    string Description,
    string? Usage = null
);

/// <summary>
/// Shared catalog of available slash commands.
/// </summary>
public static class SlashCommandCatalog
{
    /// <summary>
    /// All commands available for preview.
    /// </summary>
    public static IReadOnlyList<SlashCommandInfo> All { get; } =
    [
        new("help", "Show available commands"),
        new("model", "Show current model or switch model", "/model <name>"),
        new("think", "Toggle thinking mode on/off"),
        new("reasoning", "Set reasoning level", "/reasoning <none|low|medium|high|xhigh>"),
        new("clear", "Clear the console"),
        new("skills", "List loaded skills"),
        new("exit", "Exit the REPL"),
        new("quit", "Exit the REPL"),
    ];
}
