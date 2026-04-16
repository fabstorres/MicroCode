using MicroCode.Skills;
using MicroCode.Utils;
using OllamaSharp;

namespace MicroCode.Cli;

/// <summary>
/// The core REPL loop orchestrator.
/// </summary>
public class Repl
{
    private readonly AppSettings _settings;
    private readonly OllamaApiClient _ollama;
    private readonly CommandRegistry _commands;
    private ChatSession? _session;
    private SkillRegistry? _skills;
    private IList<OllamaSharp.Models.Model> _models = [];

    /// <summary>
    /// Creates a new REPL instance.
    /// </summary>
    public Repl(AppSettings settings, OllamaApiClient ollama)
    {
        _settings = settings;
        _ollama = ollama;
        _commands = new CommandRegistry();
    }

    /// <summary>
    /// Runs the main REPL loop.
    /// </summary>
    public async Task RunAsync()
    {
        _models = (await _ollama.ListLocalModelsAsync()).ToList();

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

        _session = new ChatSession(_ollama, selectedModel.ModelName!, systemPrompt, _skills);

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
            var enabled = _session.ToggleThink();
            ConsoleDisplay.PrintInfo($"Thinking mode: {(enabled ? "ON" : "OFF")}");
            return true;
        });

        _commands.Register("model", "Show current model or switch (/model <name>)", args =>
        {
            if (_session is null) return true;

            if (args.Length == 0)
            {
                ConsoleDisplay.PrintInfo($"Current model: {_session.ModelName}");
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

            _session.SetModel(targetModel.ModelName!);
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
