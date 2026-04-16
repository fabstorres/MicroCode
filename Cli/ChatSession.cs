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
    public ChatSession(OllamaApiClient client, string modelName, string systemPrompt, SkillRegistry skills)
    {
        _chat = new Chat(client, systemPrompt)
        {
            Model = modelName,
            Think = (ThinkValue)true,
        };
        _skills = skills;

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

        var response = _chat.SendAsync(augmented, [new UnsafeBashTool()]);
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
