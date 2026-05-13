using System.Text.Json;
using MicroCode.Skills;
using MicroCode.Utils;
using OllamaSharp;

namespace MicroCode.Cli;

/// <summary>
/// The core REPL loop orchestrator.
/// </summary>
public class Repl(AppSettings _settings, OllamaApiClient _ollama)
{

    private readonly CommandRegistry _commands = new();
#pragma warning disable CA1859
    private MicroCodeSession? _session;
#pragma warning restore CA1859
    private SkillRegistry? _skills;
    private List<OllamaSharp.Models.Model> _models = [];

    /// <summary>
    /// Runs the main REPL loop.
    /// </summary>
    public async Task RunAsync()
    {
        _models = [.. await _ollama.ListLocalModelsAsync()];

        if (_models.Count == 0)
        {
            ConsoleDisplay.PrintError("No models installed. Please install a model first.");
            return;
        }

        var selectedModel = await SelectModelAsync();
        if (selectedModel is null)
        {
            return;
        }

        Console.Clear();

        var systemPrompt = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Prompts", "system.txt"));

        _skills = SkillRegistry.Load(Environment.CurrentDirectory);
        var skillsSection = _skills.BuildSystemPromptSection();
        if (!string.IsNullOrEmpty(skillsSection))
        {
            systemPrompt = systemPrompt.TrimEnd() + "\n\n" + skillsSection;
        }
        var modelInfo = await _ollama.ShowModelAsync(selectedModel.ModelName!) ?? throw new NotImplementedException();
        _session = new MicroCodeSession(_ollama, selectedModel, modelInfo, systemPrompt, _skills);

        RegisterCommands();

        await RunLoopAsync();
    }

    private async Task<OllamaSharp.Models.Model?> SelectModelAsync()
    {
        var favModel = _models.FirstOrDefault(m =>
            _settings.Ollama.FavoriteModel.Contains(m.ModelName!));

        if (favModel is not null)
        {
            return favModel;
        }

        while (true)
        {
            Console.Clear();
            ConsoleDisplay.PrintLogo();
            ConsoleDisplay.PrintModelSelector(_models);

            var input = Console.ReadLine();
            if (!int.TryParse(input, out int number))
            {
                continue;
            }

            var selected = _models.ElementAtOrDefault(number);
            if (selected is not null)
            {
                return selected;
            }
        }
    }

    private void RegisterCommands()
    {
        _commands.Register("help", "Show available commands", _ =>
        {
            _commands.PrintHelp();
            return true;
        });

        _commands.Register("exit", "Exit the REPL", _ => false);
        _commands.Register("quit", "Exit the REPL", _ => false);

        _commands.Register("clear", "Clear the console", _ =>
        {
            Console.Clear();
            return true;
        });

        _commands.Register("think", "Toggle thinking mode on/off", _ =>
        {
            if (_session is null) return true;
            var enabled = _session.SetReasoningLevel(_session.ReasoningLevel == "none" ? "medium" : "none");
            ConsoleDisplay.PrintInfo($"Thinking mode: {(enabled ? "ON" : "OFF")}");
            return true;
        });

        _commands.Register("reasoning", "Set reasoning level (/reasoning <none|low|medium|high|xhigh>)", args =>
        {
            if (_session is null) return true;

            var validLevels = new[] { "none", "low", "medium", "high", "xhigh" };

            if (args.Length == 0)
            {
                ConsoleDisplay.PrintInfo($"Current reasoning level: {_session.ReasoningLevel}");
                ConsoleDisplay.PrintInfo("Usage: /reasoning <none|low|medium|high|xhigh>");
                return true;
            }

            var level = args[0].ToLowerInvariant();
            if (!validLevels.Contains(level))
            {
                ConsoleDisplay.PrintError($"Invalid reasoning level: {level}. Valid levels: none, low, medium, high, xhigh");
                return true;
            }

            var supportsThinking = _session.ModelCapabilities.Capabilities?.Any(c => c.Contains("thinking")) == true;
            if (!supportsThinking && level != "none")
            {
                ConsoleDisplay.PrintError($"Current model ({_session.Model.Name}) does not support reasoning.");
                return true;
            }

            _session.SetReasoningLevel(level);
            ConsoleDisplay.PrintInfo($"Reasoning level set to: {level}");
            return true;
        });

        _commands.Register("model", "Show current model or switch (/model <name>)", async args =>
        {
            if (_session is null) return true;

            if (args.Length == 0)
            {
                ConsoleDisplay.PrintInfo($"Current model: {_session.Model.ModelName}");
                ConsoleDisplay.PrintInfo("Available models:");
                foreach (var model in _models)
                {
                    Console.WriteLine($"  - {model.ModelName}");
                }
                return true;
            }

            var targetName = string.Join(" ", args);
            var targetModel = _models.FirstOrDefault(m =>
                m.ModelName!.Contains(targetName, StringComparison.OrdinalIgnoreCase));

            if (targetModel is null)
            {
                ConsoleDisplay.PrintError($"Model not found: {targetName}");
                return true;
            }

            var targetModelInfo = await _ollama.ShowModelAsync(targetModel.ModelName!);
            if (targetModelInfo is null)
            {
                ConsoleDisplay.PrintError($"Could not retrieve info for model: {targetModel.ModelName}");
                return true;
            }

            _session.SetModel(targetModel, targetModelInfo);
            ConsoleDisplay.PrintInfo($"Switched to model: {targetModel.ModelName}");
            return true;
        });

        _commands.Register("skills", "List loaded skills (global + local)", _ =>
        {
            if (_skills is null || _skills.Skills.Count == 0)
            {
                ConsoleDisplay.PrintInfo("No skills loaded.");
                return true;
            }

            ConsoleDisplay.PrintInfo($"Loaded {_skills.Skills.Count} skill(s):");
            foreach (var skill in _skills.Skills)
            {
                Console.Write("  - ");
                Console.Write(skill.Name);
                Console.Write(' ');
                var tagColor = skill.Source == SkillSource.Local
                    ? ConsoleColor.Green
                    : ConsoleColor.Blue;
                var tag = skill.Source == SkillSource.Local ? "[local]" : "[global]";
                Console.ForegroundColor = tagColor;
                Console.Write(tag);
                Console.ResetColor();
                Console.WriteLine();
                if (!string.IsNullOrWhiteSpace(skill.Description))
                {
                    Console.WriteLine($"      {skill.Description}");
                }
            }
            return true;
        });
    }

    private async Task RunLoopAsync()
    {
        while (true)
        {
            ConsoleDisplay.PrintColored(_session!.TokensIn + "\u2191 " + _session.TokensOut + "\u2193 " + _session.Model.Name + " reasoning: (" + _session.ReasoningLevel + ") \nType /help to see available commands", ConsoleColor.Gray);
            ConsoleDisplay.PrintUserPrompt();
            var input = Console.ReadLine();

            if (input is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            var (wasCommand, shouldContinue) = await _commands.TryExecuteAsync(input);
            if (!shouldContinue)
            {
                break;
            }

            if (wasCommand)
            {
                continue;
            }

            if (_session is not null)
            {
                await _session.SendAsync(input);
            }
        }
    }
}
