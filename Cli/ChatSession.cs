using MicroCode.Tools;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace MicroCode.Cli;

/// <summary>
/// Wraps OllamaSharp Chat setup and model lifecycle.
/// </summary>
public class ChatSession
{
    private readonly Chat _chat;
    private bool _thinkEnabled = true;

    /// <summary>
    /// Gets the current model name.
    /// </summary>
    public string ModelName => _chat.Model;

    /// <summary>
    /// Gets whether thinking mode is enabled.
    /// </summary>
    public bool ThinkEnabled => _thinkEnabled;

    /// <summary>
    /// Creates a new chat session with the specified client, model, and system prompt.
    /// </summary>
    public ChatSession(OllamaApiClient client, string modelName, string systemPrompt)
    {
        _chat = new Chat(client, systemPrompt)
        {
            Model = modelName,
            Think = (ThinkValue)true,
        };

        WireEvents();
    }

    private void WireEvents()
    {
        _chat.OnThink += (_, thoughts) => ConsoleDisplay.PrintThinking(thoughts);
        _chat.OnToolCall += (_, call) => ConsoleDisplay.PrintToolCall(call);
        _chat.OnToolResult += (_, result) => ConsoleDisplay.PrintToolResult(result);
    }

    /// <summary>
    /// Sends a message and streams the response to the console.
    /// </summary>
    public async Task SendAsync(string input)
    {
        ConsoleDisplay.PrintModelPrompt(ModelName);

        var response = _chat.SendAsync(input, [new UnsafeBashTool()]);
        await foreach (var message in response)
        {
            Console.Write(message);
        }
        Console.WriteLine();
        Console.ResetColor();
    }

    /// <summary>
    /// Toggles thinking mode on/off.
    /// </summary>
    /// <returns>The new state of thinking mode.</returns>
    public bool ToggleThink()
    {
        _thinkEnabled = !_thinkEnabled;
        _chat.Think = (ThinkValue)_thinkEnabled;
        return _thinkEnabled;
    }

    /// <summary>
    /// Switches to a different model.
    /// </summary>
    public void SetModel(string modelName)
    {
        _chat.Model = modelName;
    }
}
