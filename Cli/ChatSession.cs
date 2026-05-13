using System.Text;
using MicroCode.Skills;
using MicroCode.Tools;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using OllamaSharp.Tools;

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

        var tools = _supportsTools ? new[] { new UnsafeBashTool() } : [];
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

/// <summary>
/// Manages an interactive session with an Ollama model, handling conversation history,
/// tool execution, reasoning levels, and skill hint augmentation.
/// </summary>
public class MicroCodeSession
{
    private readonly OllamaApiClient _client;
    private readonly SkillRegistry _skills;
    private Message[] _conversation = [];
    private Model _model;
    private ShowModelResponse _modelCapabilities;
    private bool _supportsTools;
    /// <summary>
    /// Total prompt tokens consumed in the current session.
    /// </summary>
    public int _tokensIn = 0;
    /// <summary>
    /// Total response tokens generated in the current session.
    /// </summary>
    public int _tokensOut = 0;
    private string _reasoningLevel = "none";

    /// <summary>
    /// Gets the total number of prompt tokens consumed.
    /// </summary>
    public int TokensIn => _tokensIn;

    /// <summary>
    /// Gets the total number of response tokens generated.
    /// </summary>
    public int TokensOut => _tokensOut;

    /// <summary>
    /// Gets the current model being used for this session.
    /// </summary>
    public Model Model => _model;

    /// <summary>
    /// Gets the model capabilities information (supports tools, thinking, etc.).
    /// </summary>
    public ShowModelResponse ModelCapabilities => _modelCapabilities;

    /// <summary>
    /// Gets the current reasoning/thinking level.
    /// </summary>
    public ThinkValue ReasoningLevel => _reasoningLevel;

    /// <summary>
    /// Initializes a new session with the given model and system prompt.
    /// </summary>
    /// <param name="client">The Ollama API client.</param>
    /// <param name="model">The model to use for this session.</param>
    /// <param name="modelInfo">Capabilities and metadata for the selected model.</param>
    /// <param name="systemPrompt">The system prompt to seed the conversation.</param>
    /// <param name="skills">The skill registry used for per-message skill hint injection.</param>
    public MicroCodeSession(OllamaApiClient client, Model model, ShowModelResponse modelInfo, string systemPrompt, SkillRegistry skills)
    {
        _client = client;
        _model = model;
        _modelCapabilities = modelInfo;
        _skills = skills;
        _supportsTools = modelInfo.Capabilities?.Any(c => c.Contains("tools")) == true;
        _conversation = [.. _conversation.Append(new Message { Role = "system", Content = systemPrompt })];
    }

    /// <summary>
    /// Sends a user message and streams the model response, handling tool calls
    /// and multi-turn tool execution automatically.
    /// </summary>
    /// <param name="input">The user input message.</param>
    public async Task SendAsync(string input)
    {
        var augmented = AugmentWithSkillHints(input);
        _conversation = [.. _conversation.Append(new Message { Role = ChatRole.User, Content = augmented })];

        var tools = _supportsTools ? new[] { new UnsafeBashTool() } : [];
        var invoker = new DefaultToolInvoker();

        while (true)
        {
            var request = new ChatRequest
            {
                Model = _model.Name,
                Messages = _conversation,
                Stream = true,
                Think = _reasoningLevel != "none" ? _reasoningLevel : (ThinkValue)false,
                Tools = tools.Length > 0 ? tools : null,
            };

            var messageBuilder = new MessageBuilder();
            await foreach (var chunk in _client.ChatAsync(request))
            {
                if (chunk?.Message.Thinking is not null)
                {
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(chunk.Message.Thinking);
                    Console.ResetColor();
                }
                if (chunk?.Message.Content is not null)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write(chunk.Message.Content);
                    Console.ResetColor();
                }
                if (chunk is ChatDoneResponseStream done)
                {
                    _tokensIn = done.PromptEvalCount;
                    _tokensOut = done.EvalCount;
                }

                messageBuilder.Append(chunk);
            }

            if (messageBuilder.HasValue)
            {
                var assistantMessage = messageBuilder.ToMessage();
                _conversation = [.. _conversation.Append(assistantMessage)];

                if (assistantMessage.ToolCalls?.Any() == true)
                {
                    foreach (var toolCall in assistantMessage.ToolCalls)
                    {
                        ConsoleDisplay.PrintToolCall(toolCall);
                        var toolResult = await invoker.InvokeAsync(toolCall, tools, default);
                        ConsoleDisplay.PrintToolResult(toolResult);

                        var resultContent = $"Tool: {StringifyToolCall(toolCall)}:\nResult: {toolResult.Result}";
                        _conversation = [.. _conversation.Append(new Message(ChatRole.Tool, resultContent))];
                    }
                    continue;
                }
            }

            break;
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

    private static string StringifyToolCall(Message.ToolCall toolCall)
    {
        return $"{toolCall.Function?.Name ?? "(unnamed tool)"}({string.Join(", ", toolCall.Function?.Arguments?.Select(kvp => $"{kvp.Key}: {kvp.Value}") ?? [])})";
    }

    /// <summary>
    /// Sets the reasoning/thinking level for the session.
    /// </summary>
    /// <param name="level">The reasoning level identifier (e.g. "none", "low", "medium", "high").</param>
    /// <returns><c>true</c> if reasoning is now enabled; <c>false</c> if set to "none".</returns>
    public bool SetReasoningLevel(string level)
    {
        // TODO: replace type string with ReasoningLevel enum
        _reasoningLevel = level;
        return _reasoningLevel != "none";
    }

    /// <summary>
    /// Switches to a different model and updates tool and reasoning support accordingly.
    /// If the new model does not support reasoning, the reasoning level is automatically reset.
    /// </summary>
    /// <param name="model">The new model to use.</param>
    /// <param name="modelCapabilities">Capabilities and metadata for the new model.</param>
    public void SetModel(Model model, ShowModelResponse modelCapabilities)
    {
        _model = model;
        _modelCapabilities = modelCapabilities;
        _supportsTools = modelCapabilities.Capabilities?.Any(c => c.Contains("tools")) == true;

        var supportsThinking = modelCapabilities.Capabilities?.Any(c => c.Contains("thinking")) == true;
        if (!supportsThinking && _reasoningLevel != "none")
        {
            _reasoningLevel = "none";
            ConsoleDisplay.PrintInfo("Model does not support reasoning. Reasoning level reset to none.");
        }
    }
}
