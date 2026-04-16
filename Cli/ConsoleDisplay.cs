using OllamaSharp.Models.Chat;
using ToolCall = OllamaSharp.Models.Chat.Message.ToolCall;

namespace MicroCode.Cli;

/// <summary>
/// Static helper for all console output formatting.
/// </summary>
public static class ConsoleDisplay
{
    private static readonly string[] Logo =
    [
        "@@@@@@@%**%#%@@@@@@@",
        "@@@@#     +  +:#@@@@",
        "@@%       +  +   %@@",
        "@#        +  +    #@",
        "@:        +  +   :%@",
        "%         +  ++@=  %",
        "@:        +-##    :@",
        "@*      -*#  +    *@",
        "@@#  +*   +  +   #@@",
        "@@@@%     +  +.#@@@@",
        "@@@@@@@%**%*%@@@@@@@",
    ];

    private const string Title = "MicroCode by Fabs";

    /// <summary>
    /// Renders the ASCII logo with title.
    /// </summary>
    public static void PrintLogo()
    {
        for (int i = 0; i < Logo.Length; i++)
        {
            var message = i == Logo.Length - 1 ? "  " + Title : "";
            Console.WriteLine(Logo[i] + message);
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Prints the model selection menu and returns the user's choice.
    /// </summary>
    public static void PrintModelSelector(IList<OllamaSharp.Models.Model> models)
    {
        foreach (var (model, i) in models.Select((value, index) => (value, index)))
        {
            Console.WriteLine($"{i}: {model.ModelName}");
        }
        Console.Write("Enter a number to select a model: ");
    }

    /// <summary>
    /// Prints thinking output in gray.
    /// </summary>
    public static void PrintThinking(string? thoughts)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(thoughts);
        Console.ResetColor();
    }

    /// <summary>
    /// Prints a tool call notification in magenta.
    /// </summary>
    public static void PrintToolCall(ToolCall call)
    {
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        var args = string.Join(", ", call.Function?.Arguments?.Select(a => $"{a.Key}: {a.Value}") ?? []);
        Console.WriteLine($"\n[tool call] {call.Function?.Name}({args})");
        Console.ResetColor();
    }

    /// <summary>
    /// Prints a tool result notification in green.
    /// </summary>
    public static void PrintToolResult(OllamaSharp.Tools.ToolResult result)
    {
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"[{result.Tool}] {result.Result}");
        Console.ResetColor();
    }

    /// <summary>
    /// Prints the user input prompt.
    /// </summary>
    public static void PrintUserPrompt()
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write("You: ");
        Console.ResetColor();
    }

    /// <summary>
    /// Prints the model response prompt.
    /// </summary>
    public static void PrintModelPrompt(string modelName)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"{ExtractModelBaseName(modelName)}: ");
        Console.ForegroundColor = ConsoleColor.White;
    }

    /// <summary>
    /// Prints a message in the specified color.
    /// </summary>
    public static void PrintColored(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Prints an info message in cyan.
    /// </summary>
    public static void PrintInfo(string message)
    {
        PrintColored(message, ConsoleColor.Cyan);
    }

    /// <summary>
    /// Prints an error message in red.
    /// </summary>
    public static void PrintError(string message)
    {
        PrintColored(message, ConsoleColor.Red);
    }

    /// <summary>
    /// Extracts the base model name from a full model identifier.
    /// </summary>
    public static string ExtractModelBaseName(string input)
    {
        var afterSlash = input[(input.LastIndexOf('/') + 1)..];
        var colonIndex = afterSlash.IndexOf(':');
        return colonIndex >= 0 ? afterSlash[..colonIndex] : afterSlash;
    }
}
