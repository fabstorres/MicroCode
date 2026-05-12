using System.Text;
using MicroCode.Skills;
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
    private readonly SkillRegistry _skills;
    private bool _thinkEnabled = true;
    private bool _supportsTools;

    /// <summary>
    /// Gets the current model name.
    /// </summary>
    public string ModelName => _chat.Model;

    /// <summary>
    /// Gets whether thinking mode is enabled.
    /// </summary>
    public bool ThinkEnabled => _thinkEnabled;

    /// <summary>
    /// Creates a new chat session with the specified client, model, system prompt,
    /// and skill registry used for per-message skill hint injection.
    /// </summary>
    public ChatSession(OllamaApiClient client, OllamaSharp.Models.ShowModelResponse modelInfo, OllamaSharp.Models.Model model, string systemPrompt, SkillRegistry skills)
    {
        _chat = new Chat(client, systemPrompt)
        {
            Model = model.ModelName!,
            Think = modelInfo.Capabilities?.Any(c => c.Contains("thinking")) == true ? ThinkValue.Medium : null,
        };
        _skills = skills;
        _thinkEnabled = _chat.Think != null;
        _supportsTools = modelInfo.Capabilities?.Any(c => c.Contains("tools")) == true;
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

        var augmented = AugmentWithSkillHints(input);

        var tools = _supportsTools ? new[] { new UnsafeBashTool() } : Array.Empty<UnsafeBashTool>();
        var response = _chat.SendAsync(augmented, tools);
        await foreach (var message in response)
        {
            Console.Write(message);
        }
        Console.WriteLine();
        Console.ResetColor();
    }

    private string AugmentWithSkillHints(string input)
    {
        var matches = _skills.Match(input);
        if (matches.Count == 0)
        {
            return input;
        }

        var sb = new StringBuilder();
        sb.AppendLine("<skill_hints>");
        sb.AppendLine("The user's message mentions the following skill(s). Read the SKILL.md before responding if relevant:");
        foreach (var skill in matches)
        {
            sb.Append("- ").Append(skill.Name).Append(": ").AppendLine(skill.FilePath);
        }
        sb.AppendLine("</skill_hints>");
        sb.AppendLine();
        sb.Append(input);
        return sb.ToString();
    }

    /// <summary>
    /// Toggles thinking mode on/off.
    /// </summary>
    /// <returns>The new state of thinking mode.</returns>
    public bool ToggleThink()
    {
        _thinkEnabled = !_thinkEnabled;
        _chat.Think = _thinkEnabled ? ThinkValue.Medium : null;
        return _thinkEnabled;
    }

    /// <summary>
    /// Switches to a different model and updates thinking support accordingly.
    /// </summary>
    public void SetModel(string modelName, OllamaSharp.Models.ShowModelResponse modelInfo)
    {
        _chat.Model = modelName;
        var supportsThinking = modelInfo.Capabilities?.Any(c => c.Contains("thinking")) == true;
        _chat.Think = supportsThinking && _thinkEnabled ? ThinkValue.Medium : null;
        _supportsTools = modelInfo.Capabilities?.Any(c => c.Contains("tools")) == true;
    }
}
