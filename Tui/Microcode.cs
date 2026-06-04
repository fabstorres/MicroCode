
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Drawing;
using Terminal.Gui.Editor;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Editor.Highlighting;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using System.Drawing;
using System.Text;
using MicroCode.Cli;
using MicroCode.Skills;
using MicroCode.Utils;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using OllamaSharp.Tools;

namespace MicroCode.Tui;

class Microcode : Window
{
    private sealed class MessageViewState
    {
        public required bool IsUser { get; init; }
        public StringBuilder Text { get; } = new();
    }

    private readonly View _messageViewport;
    private readonly View _messageStack;
    private readonly View _inputContainer;
    private readonly Label _statusLabel;
    private readonly Label _inputPrompt;
    private readonly TextField _inputField;
    private readonly CommandPreviewOverlay _commandPreviewOverlay;
    private readonly Scheme _userMessageScheme = CreateUserMessageScheme();
    private readonly Scheme _thinkingMessageScheme = CreateThinkingMessageScheme();
    private readonly Scheme _toolMessageScheme = CreateToolMessageScheme();
    private readonly Scheme _inputScheme = CreateInputScheme();
    private readonly Scheme _commandPreviewScheme = CreateCommandPreviewScheme();
    private readonly IApplication _app;
    private readonly AppSettings _settings;
    private readonly OllamaApiClient _ollama;
    private readonly CommandRegistry _commands = new();
    private List<SlashCommandInfo> _filteredCommands = [];
    private int _selectedCommandIndex;
    private bool _isCommandPreviewVisible;
    private int _nextMessageY;
    private SkillRegistry? _skills;
    private MicroCodeSession? _session;
    private List<Model> _models = [];
    private Editor? _activeAssistantEditor;
    private Editor? _activeThinkingEditor;
    private bool _isSending;
    private bool _isReady;

    public Microcode(IApplication app, AppSettings settings, OllamaApiClient ollama)
    {
        _app = app;
        _settings = settings;
        _ollama = ollama;
        Title = "Microcode";

        Border.Settings |= BorderSettings.TerminalTitle; // pushes title to terminal titlebar
        Border.Settings &= ~BorderSettings.Title;        // don't render title in the frame
        Border.Thickness = new Thickness(0);             // removes the border line

        _messageViewport = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 4,
            CanFocus = false,
            ViewportSettings = ViewportSettingsFlags.None
        };

        _messageStack = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Auto(DimAutoStyle.Content),
            CanFocus = false
        };

        _messageViewport.Add(_messageStack);

        _inputContainer = new View()
        {
            X = 0,
            Y = Pos.Bottom(_messageViewport),
            Width = Dim.Fill(),
            Height = 4,
            CanFocus = true,
            Border =
            {
                Thickness = new Thickness(0, 1, 0, 1), // top & bottom only
                LineStyle = LineStyle.Single
            }
        };
        _inputContainer.SetScheme(_inputScheme);

        _statusLabel = new Label()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            Text = "Starting...",
            CanFocus = false
        };
        _statusLabel.SetScheme(_inputScheme);

        _inputPrompt = new Label()
        {
            X = 0,
            Y = 1,
            Width = 1,
            Height = 1,
            Text = ">",
            CanFocus = false
        };
        _inputPrompt.SetScheme(_inputScheme);

        _inputField = new TextField()
        {
            X = 2,
            Y = 1,
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = true
        };
        _inputField.SetScheme(_inputScheme);
        _inputContainer.Add(_statusLabel, _inputPrompt, _inputField);

        _commandPreviewOverlay = new CommandPreviewOverlay()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            Visible = false,
            CanFocus = false,
            Border =
            {
                Thickness = new Thickness(0)
            }
        };
        _commandPreviewOverlay.SetScheme(_commandPreviewScheme);

        _inputField.ValueChanged += (s, e) => RefreshCommandPreview();
        _inputField.KeyDown += OnInputFieldKeyDown;
        _inputField.Accepting += (s, e) =>
        {
            var text = _inputField.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            _inputField.Text = "";
            _ = SendMessageAsync(text);
        };

        _messageViewport.ViewportChanged += (s, e) => ReflowMessages();
        _messageViewport.MouseEvent += OnMessageViewportMouseEvent;

        Add(_messageViewport, _inputContainer, _commandPreviewOverlay);
        _inputField.Enabled = false;
        _inputField.SetFocus();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _models = [.. await _ollama.ListLocalModelsAsync()];
            if (_models.Count == 0)
            {
                RunOnUiThread(() =>
                {
                    ShowMessageDialog("Models", "No local Ollama models are installed.");
                    _app.RequestStop();
                });
                return;
            }

            var selectedModel = SelectInitialModel();
            if (selectedModel is null)
            {
                selectedModel = await InvokeAsync(() =>
                {
                    var picked = ShowModelPicker();
                    if (picked is null)
                    {
                        _app.RequestStop();
                    }

                    return picked;
                });
            }

            if (selectedModel is null)
            {
                return;
            }

            var systemPrompt = await LoadSystemPromptAsync();
            _skills = SkillRegistry.Load(Environment.CurrentDirectory);
            var skillsSection = _skills.BuildSystemPromptSection();
            if (!string.IsNullOrEmpty(skillsSection))
            {
                systemPrompt = systemPrompt.TrimEnd() + "\n\n" + skillsSection;
            }

            var modelInfo = await _ollama.ShowModelAsync(selectedModel.ModelName!);
            if (modelInfo is null)
            {
                RunOnUiThread(() =>
                {
                    ShowMessageDialog("Model", $"Could not retrieve info for model: {selectedModel.ModelName}");
                    _app.RequestStop();
                });
                return;
            }

            _session = new MicroCodeSession(_ollama, selectedModel, modelInfo, systemPrompt, _skills);
            WireSessionEvents(_session);
            RegisterCommands();

            RunOnUiThread(() =>
            {
                _isReady = true;
                _inputField.Enabled = true;
                UpdateStatus();
                _inputField.SetFocus();
            });
        }
        catch (Exception ex)
        {
            RunOnUiThread(() =>
            {
                ShowMessageDialog("Startup Error", ex.Message);
                _app.RequestStop();
            });
        }
    }

    private Model? SelectInitialModel()
    {
        if (string.IsNullOrWhiteSpace(_settings.Ollama.FavoriteModel))
        {
            return null;
        }

        return _models.FirstOrDefault(m =>
            m.ModelName?.Contains(_settings.Ollama.FavoriteModel, StringComparison.OrdinalIgnoreCase) == true);
    }

    private Task<T> InvokeAsync<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>();
        _app.Invoke(() =>
        {
            try
            {
                tcs.SetResult(action());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    private static async Task<string> LoadSystemPromptAsync()
    {
        return await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Prompts", "system.txt"));
    }

    private void WireSessionEvents(MicroCodeSession session)
    {
        session.ContentReceived += (_, content) =>
        {
            RunOnUiThread(() =>
            {
                _activeAssistantEditor ??= AddMessageEditor("", MessageKind.Assistant);
                AppendToMessageEditor(_activeAssistantEditor, content);
            });
        };
        session.ThinkingReceived += (_, thinking) =>
        {
            RunOnUiThread(() =>
            {
                _activeThinkingEditor ??= AddMessageEditor("", MessageKind.Thinking);
                AppendToMessageEditor(_activeThinkingEditor, thinking);
            });
        };
        session.ToolCallReceived += (_, toolCall) =>
        {
            RunOnUiThread(() => AddMessageEditor($"Tool call: {StringifyToolCall(toolCall)}", MessageKind.Tool));
        };
        session.ToolResultReceived += (_, toolResult) =>
        {
            RunOnUiThread(() => AddMessageEditor($"Tool result:\n{toolResult.Result}", MessageKind.Tool));
        };
        session.TokenUsageUpdated += (_, _) => RunOnUiThread(UpdateStatus);
        session.InfoReceived += (_, message) => RunOnUiThread(() => AddMessageEditor(message, MessageKind.Tool));
    }

    private async Task SendMessageAsync(string text)
    {
        if (!_isReady || _session is null)
        {
            ShowMessageDialog("Not Ready", "MicroCode is still starting.");
            return;
        }

        if (_isSending)
        {
            ShowMessageDialog("Busy", "A response is already streaming.");
            return;
        }

        if (await TryExecuteCommandAsync(text))
        {
            return;
        }

        AddMessageEditor(text, MessageKind.User);
        _activeAssistantEditor = AddMessageEditor("", MessageKind.Assistant);
        _activeThinkingEditor = null;
        _isSending = true;
        _inputField.Enabled = false;

        try
        {
            await _session.SendAsync(text);
        }
        catch (Exception ex)
        {
            RunOnUiThread(() => AddMessageEditor($"Error: {ex.Message}", MessageKind.Tool));
        }
        finally
        {
            RunOnUiThread(() =>
            {
                _isSending = false;
                _inputField.Enabled = true;
                _activeAssistantEditor = null;
                _activeThinkingEditor = null;
                UpdateStatus();
                _inputField.SetFocus();
            });
        }
    }

    private enum MessageKind
    {
        User,
        Assistant,
        Thinking,
        Tool
    }

    private Editor AddMessageEditor(string text, MessageKind kind)
    {
        var state = new MessageViewState()
        {
            IsUser = kind == MessageKind.User
        };
        state.Text.Append(text);

        var editor = new Editor()
        {
            WordWrap = true,
            ReadOnly = true,
            GutterOptions = GutterOptions.None,
            Document = new TextDocument(text),
            HighlightingDefinition = HighlightingManager.Instance.GetDefinitionByExtension(".md"),
            CanFocus = false
        };

        editor.Data = state;

        if (kind == MessageKind.User)
        {
            editor.SetScheme(_userMessageScheme);
        }
        else if (kind == MessageKind.Thinking)
        {
            editor.SetScheme(_thinkingMessageScheme);
        }
        else if (kind == MessageKind.Tool)
        {
            editor.SetScheme(_toolMessageScheme);
        }

        _messageStack.Add(editor);
        ReflowMessages();
        ScrollMessagesToBottom();

        return editor;
    }

    private void ResizeMessageEditor(Editor editor)
    {
        editor.Height = EstimateEditorHeight(editor);
        ReflowMessages();
    }

    private void AppendToMessageEditor(Editor editor, string text)
    {
        if (editor.Data is MessageViewState state)
        {
            state.Text.Append(text);
            editor.Document?.Insert(editor.Document.TextLength, text);
        }
        else
        {
            editor.Document?.Insert(editor.Document.TextLength, text);
        }

        editor.Viewport = new Rectangle(0, 0, Math.Max(1, editor.Frame.Width), Math.Max(1, editor.Frame.Height));
        ResizeMessageEditor(editor);
        editor.SetNeedsDraw();
        ScrollMessagesToBottom();
    }

    private void RunOnUiThread(Action action)
    {
        _app.Invoke(action);
    }

    private async Task<bool> TryExecuteCommandAsync(string input)
    {
        var (wasCommand, shouldContinue) = await _commands.TryExecuteAsync(input);
        if (!shouldContinue)
        {
            RunOnUiThread(() => _app.RequestStop());
        }

        return wasCommand;
    }

    private void RegisterCommands()
    {
        _commands.Register("help", "Show available commands", _ =>
        {
            ShowHelpDialog();
            return true;
        });
        _commands.Register("exit", "Exit the TUI", _ => false);
        _commands.Register("quit", "Exit the TUI", _ => false);
        _commands.Register("clear", "Clear messages", _ =>
        {
            ClearMessages();
            return true;
        });
        _commands.Register("skills", "List loaded skills", _ =>
        {
            ShowSkillsDialog();
            return true;
        });
        _commands.Register("think", "Toggle thinking mode", _ =>
        {
            ToggleThinking();
            return true;
        });
        _commands.Register("reasoning", "Set reasoning level", args =>
        {
            if (args.Length == 0)
            {
                ShowReasoningDialog();
                return true;
            }

            SetReasoning(args[0]);
            return true;
        });
        _commands.Register("model", "Show or switch model", async args =>
        {
            if (args.Length == 0)
            {
                var selected = ShowModelPicker();
                if (selected is not null)
                {
                    await SetModelAsync(selected);
                }
                return true;
            }

            var targetName = string.Join(" ", args);
            var target = _models.FirstOrDefault(m =>
                m.ModelName?.Contains(targetName, StringComparison.OrdinalIgnoreCase) == true);
            if (target is null)
            {
                ShowMessageDialog("Model", $"Model not found: {targetName}");
                return true;
            }

            await SetModelAsync(target);
            return true;
        });
    }

    private void ReflowMessages()
    {
        var viewportWidth = Math.Max(1, _messageViewport.Viewport.Width);
        var y = 0;

        foreach (var view in _messageStack.SubViews)
        {
            if (view is not Editor editor) continue;

            var isUser = editor.Data is MessageViewState state && state.IsUser;
            var width = GetMessageWidth(viewportWidth);
            var height = EstimateEditorHeight(editor, width);

            editor.X = 0;
            editor.Y = y;
            editor.Width = width;
            editor.Height = height;
            editor.SetContentSize(new Size(width, height));
            editor.Viewport = new Rectangle(0, 0, width, height);

            y += Math.Max(1, height);
        }

        _nextMessageY = y;
        var contentHeight = Math.Max(_messageViewport.Viewport.Height, _nextMessageY);
        _messageStack.Height = contentHeight;
        _messageStack.SetContentSize(new Size(viewportWidth, contentHeight));
        _messageViewport.SetContentSize(new Size(viewportWidth, contentHeight));
        _messageStack.SetNeedsDraw();
        _messageViewport.SetNeedsDraw();
    }

    private void ScrollMessagesToBottom()
    {
        var overflow = Math.Max(0, _nextMessageY - _messageViewport.Viewport.Height);
        var currentY = _messageViewport.Viewport.Y;
        var delta = overflow - currentY;

        if (delta != 0)
        {
            ScrollMessageViewport(delta);
        }

        _messageViewport.SetNeedsDraw();
    }

    private void OnMessageViewportMouseEvent(object? sender, Mouse mouse)
    {
        if (!mouse.IsWheel)
        {
            return;
        }

        var flags = mouse.Flags;

        if (flags.HasFlag(MouseFlags.WheeledUp))
        {
            ScrollMessageViewport(-3);
            mouse.Handled = true;
            return;
        }

        if (flags.HasFlag(MouseFlags.WheeledDown))
        {
            ScrollMessageViewport(3);
            mouse.Handled = true;
            return;
        }

        mouse.Handled = true;
    }

    private void ScrollMessageViewport(int rows)
    {
        if (_messageViewport.ScrollVertical(rows) == true)
        {
            _messageViewport.SetNeedsDraw();
        }
    }

    private void ClearMessages()
    {
        _messageStack.RemoveAll();
        _nextMessageY = 0;
        ReflowMessages();
        _inputField.SetFocus();
    }

    private void UpdateStatus()
    {
        if (_session is null)
        {
            _statusLabel.Text = "Starting...";
        }
        else
        {
            _statusLabel.Text = $"{_session.TokensIn}\u2191 {_session.TokensOut}\u2193 {_session.Model.Name} reasoning: ({_session.ReasoningLevel})";
        }

        _statusLabel.SetNeedsDraw();
    }

    private void ToggleThinking()
    {
        if (_session is null)
        {
            return;
        }

        var next = _session.ReasoningLevel == "none" ? "medium" : "none";
        SetReasoning(next);
    }

    private void SetReasoning(string level)
    {
        if (_session is null)
        {
            return;
        }

        var validLevels = new[] { "none", "low", "medium", "high", "xhigh" };
        level = level.ToLowerInvariant();
        if (!validLevels.Contains(level))
        {
            ShowMessageDialog("Reasoning", $"Invalid reasoning level: {level}");
            return;
        }

        var supportsThinking = _session.ModelCapabilities.Capabilities?.Any(c => c.Contains("thinking")) == true;
        if (!supportsThinking && level != "none")
        {
            ShowMessageDialog("Reasoning", $"Current model ({_session.Model.Name}) does not support reasoning.");
            return;
        }

        _session.SetReasoningLevel(level);
        UpdateStatus();
    }

    private async Task SetModelAsync(Model model)
    {
        if (_session is null)
        {
            return;
        }

        var modelInfo = await _ollama.ShowModelAsync(model.ModelName!);
        if (modelInfo is null)
        {
            RunOnUiThread(() => ShowMessageDialog("Model", $"Could not retrieve info for model: {model.ModelName}"));
            return;
        }

        RunOnUiThread(() =>
        {
            _session.SetModel(model, modelInfo);
            UpdateStatus();
        });
    }

    private void ShowHelpDialog()
    {
        var text = string.Join(Environment.NewLine, SlashCommandCatalog.All.Select(c =>
            c.Usage is null
                ? $"/{c.Name} - {c.Description}"
                : $"/{c.Name} - {c.Description} ({c.Usage})"));
        ShowMessageDialog("Help", text);
    }

    private void ShowSkillsDialog()
    {
        if (_skills is null || _skills.Skills.Count == 0)
        {
            ShowMessageDialog("Skills", "No skills loaded.");
            return;
        }

        var sb = new StringBuilder();
        foreach (var skill in _skills.Skills)
        {
            var source = skill.Source == SkillSource.Local ? "local" : "global";
            sb.Append(skill.Name).Append(" [").Append(source).AppendLine("]");
            if (!string.IsNullOrWhiteSpace(skill.Description))
            {
                sb.AppendLine(skill.Description);
            }
            sb.AppendLine();
        }

        ShowMessageDialog("Skills", sb.ToString().TrimEnd());
    }

    private Model? ShowModelPicker()
    {
        if (_models.Count == 0)
        {
            ShowMessageDialog("Models", "No local models are available.");
            return null;
        }

        var names = _models.Select(m => m.ModelName ?? m.Name).ToArray();
        var current = _session?.Model.ModelName;
        var selectedIndex = Math.Max(0, Array.FindIndex(names, n => string.Equals(n, current, StringComparison.Ordinal)));
        var selected = ShowSelectionDialog("Select Model", names, selectedIndex);
        return selected < 0 ? null : _models[selected];
    }

    private void ShowReasoningDialog()
    {
        if (_session is null)
        {
            return;
        }

        var levels = new[] { "none", "low", "medium", "high", "xhigh" };
        var selectedIndex = Math.Max(0, Array.IndexOf(levels, _session.ReasoningLevel.ToString()));
        var selected = ShowSelectionDialog("Reasoning", levels, selectedIndex);
        if (selected >= 0)
        {
            SetReasoning(levels[selected]);
        }
    }

    private int ShowSelectionDialog(string title, IReadOnlyList<string> items, int selectedIndex)
    {
        var dialog = new SelectionDialog(title, items, selectedIndex, () => _app.RequestStop());
        _app.Run(dialog);
        return dialog.WasAccepted ? dialog.SelectedIndex : -1;
    }

    private void ShowMessageDialog(string title, string message)
    {
        var dialog = new Dialog()
        {
            Title = title,
            Width = Dim.Percent(80),
            Height = Dim.Percent(70)
        };

        var text = new Editor()
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(3),
            ReadOnly = true,
            WordWrap = true,
            GutterOptions = GutterOptions.None,
            Document = new TextDocument(message),
            CanFocus = false
        };
        var ok = new Button()
        {
            Text = "OK",
            X = Pos.Center(),
            Y = Pos.Bottom(text) + 1,
            IsDefault = true
        };
        ok.Accepting += (_, e) =>
        {
            e.Handled = true;
            _app.RequestStop();
        };

        dialog.Add(text, ok);
        _app.Run(dialog);
    }

    private static string StringifyToolCall(Message.ToolCall toolCall)
    {
        return $"{toolCall.Function?.Name ?? "(unnamed tool)"}({string.Join(", ", toolCall.Function?.Arguments?.Select(kvp => $"{kvp.Key}: {kvp.Value}") ?? [])})";
    }

    private void OnInputFieldKeyDown(object? sender, Key key)
    {
        if (!_isCommandPreviewVisible)
        {
            return;
        }

        var keyCode = key.KeyCode;

        if (keyCode == KeyCode.CursorUp)
        {
            MoveCommandSelection(-1);
            key.Handled = true;
            return;
        }

        if (keyCode == KeyCode.CursorDown)
        {
            MoveCommandSelection(1);
            key.Handled = true;
            return;
        }

        if (keyCode == KeyCode.PageUp)
        {
            MoveCommandSelection(-GetCommandPreviewHeight());
            key.Handled = true;
            return;
        }

        if (keyCode == KeyCode.PageDown)
        {
            MoveCommandSelection(GetCommandPreviewHeight());
            key.Handled = true;
            return;
        }

        if (keyCode == KeyCode.Tab || keyCode == KeyCode.Enter)
        {
            InsertSelectedCommand();
            key.Handled = true;
            return;
        }

        if (keyCode == KeyCode.Esc)
        {
            HideCommandPreview();
            key.Handled = true;
        }
    }

    private void RefreshCommandPreview()
    {
        var input = _inputField.Text?.ToString() ?? string.Empty;

        if (!TryGetSlashCommandQuery(input, out var query))
        {
            HideCommandPreview();
            return;
        }

        var matches = FilterCommands(query);
        if (matches.Count == 0)
        {
            HideCommandPreview();
            return;
        }

        var previousCommand = _filteredCommands.ElementAtOrDefault(_selectedCommandIndex)?.Name;
        _filteredCommands = matches;
        _selectedCommandIndex = previousCommand is null
            ? 0
            : Math.Max(0, _filteredCommands.FindIndex(c => c.Name == previousCommand));

        if (_selectedCommandIndex >= _filteredCommands.Count)
        {
            _selectedCommandIndex = 0;
        }

        ShowCommandPreview();
    }

    private static bool TryGetSlashCommandQuery(string input, out string query)
    {
        query = string.Empty;

        if (string.IsNullOrEmpty(input) || !input.StartsWith('/'))
        {
            return false;
        }

        var commandText = input[1..];
        if (commandText.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        query = commandText;
        return true;
    }

    private static List<SlashCommandInfo> FilterCommands(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return [.. SlashCommandCatalog.All];
        }

        var prefixMatches = SlashCommandCatalog.All
            .Where(c => c.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (prefixMatches.Count > 0)
        {
            return prefixMatches;
        }

        return SlashCommandCatalog.All
            .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void ShowCommandPreview()
    {
        var height = GetCommandPreviewHeight();

        _commandPreviewOverlay.Commands = _filteredCommands;
        _commandPreviewOverlay.SelectedIndex = _selectedCommandIndex;
        _commandPreviewOverlay.Height = height + 2;
        _commandPreviewOverlay.Y = Pos.Top(_inputContainer) - (height + 2);
        _commandPreviewOverlay.Visible = true;
        _isCommandPreviewVisible = true;

        _commandPreviewOverlay.SetNeedsDraw();
        _inputContainer.SetNeedsDraw();
    }

    private void HideCommandPreview()
    {
        if (!_isCommandPreviewVisible)
        {
            return;
        }

        _commandPreviewOverlay.Visible = false;
        _isCommandPreviewVisible = false;
        _filteredCommands = [];
        _selectedCommandIndex = 0;
        SetNeedsDraw();
    }

    private int GetCommandPreviewHeight()
    {
        var availableHeight = Math.Max(1, Frame.Height - _inputContainer.Frame.Height - 2);
        return Math.Max(1, Math.Min(Math.Min(6, _filteredCommands.Count), availableHeight));
    }

    private void MoveCommandSelection(int delta)
    {
        if (_filteredCommands.Count == 0)
        {
            return;
        }

        _selectedCommandIndex = Math.Clamp(_selectedCommandIndex + delta, 0, _filteredCommands.Count - 1);
        _commandPreviewOverlay.SelectedIndex = _selectedCommandIndex;
        _commandPreviewOverlay.SetNeedsDraw();
    }

    private void InsertSelectedCommand()
    {
        var command = _filteredCommands.ElementAtOrDefault(_selectedCommandIndex);
        if (command is null)
        {
            HideCommandPreview();
            return;
        }

        var text = "/" + command.Name + " ";
        _inputField.Text = text;
        _inputField.InsertionPoint = text.Length;
        HideCommandPreview();
        _inputField.SetFocus();
    }

    private int GetMessageWidth(int viewportWidth)
    {
        return Math.Max(1, viewportWidth);
    }

    private int EstimateEditorHeight(Editor editor)
    {
        var width = Math.Max(1, editor.Frame.Width);
        return EstimateEditorHeight(editor, width);
    }

    private int EstimateEditorHeight(Editor editor, int width)
    {
        var text = editor.Document?.Text ?? string.Empty;
        if (text.Length == 0) return 1;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var height = 0;

        foreach (var line in lines)
        {
            height += Math.Max(1, (line.Length + Math.Max(1, width) - 1) / Math.Max(1, width));
        }

        return Math.Max(1, height);
    }

    private static Scheme CreateUserMessageScheme()
    {
        var attribute = new Attribute(Color.White, Color.DarkGray);

        return new Scheme()
        {
            Normal = attribute,
            Focus = attribute,
            Active = attribute,
            Editable = attribute,
            ReadOnly = attribute
        };
    }

    private static Scheme CreateThinkingMessageScheme()
    {
        var attribute = new Attribute(Color.DarkGray, Color.None);

        return new Scheme()
        {
            Normal = attribute,
            Focus = attribute,
            Active = attribute,
            Editable = attribute,
            ReadOnly = attribute
        };
    }

    private static Scheme CreateToolMessageScheme()
    {
        var attribute = new Attribute(Color.BrightCyan, Color.None);

        return new Scheme()
        {
            Normal = attribute,
            Focus = attribute,
            Active = attribute,
            Editable = attribute,
            ReadOnly = attribute
        };
    }

    private static Scheme CreateInputScheme()
    {
        var attribute = new Attribute(Color.White, Color.None);

        return new Scheme()
        {
            Normal = attribute,
            Focus = attribute,
            Active = attribute,
            Editable = attribute,
            ReadOnly = attribute
        };
    }

    private static Scheme CreateCommandPreviewScheme()
    {
        var normal = new Attribute(Color.White, Color.None);
        var selected = new Attribute(Color.BrightCyan, Color.None);

        return new Scheme()
        {
            Normal = normal,
            Focus = selected,
            Active = selected,
            Editable = normal,
            ReadOnly = normal
        };
    }

    private sealed class CommandPreviewOverlay : View
    {
        public IReadOnlyList<SlashCommandInfo> Commands { get; set; } = [];
        public int SelectedIndex { get; set; }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            base.OnDrawingContent(context);

            var width = Math.Max(1, Viewport.Width);
            var height = Math.Min(Viewport.Height, Commands.Count);
            var firstVisible = GetFirstVisibleIndex(height);

            for (var row = 0; row < height; row++)
            {
                var commandIndex = firstVisible + row;
                if (commandIndex >= Commands.Count)
                {
                    break;
                }

                var command = Commands[commandIndex];
                var isSelected = commandIndex == SelectedIndex;
                SetAttribute(isSelected ? GetScheme().Active : GetScheme().Normal);
                Move(0, row);
                AddStr(FormatCommand(command, width));
            }

            return true;
        }

        private int GetFirstVisibleIndex(int height)
        {
            if (height <= 0 || SelectedIndex < height)
            {
                return 0;
            }

            return Math.Min(SelectedIndex - height + 1, Math.Max(0, Commands.Count - height));
        }

        private static string FormatCommand(SlashCommandInfo command, int width)
        {
            var name = "/" + command.Name;
            var descriptionStart = 22;
            var row = width > descriptionStart
                ? name.PadRight(descriptionStart) + command.Description
                : name;

            if (row.Length > width)
            {
                row = row[..width];
            }

            return row.PadRight(width);
        }
    }

    private sealed class SelectionDialog : Dialog
    {
        private readonly IReadOnlyList<string> _items;
        private readonly View _list;
        private readonly Action _requestStop;

        public bool WasAccepted { get; private set; }
        public int SelectedIndex { get; private set; }

        public SelectionDialog(string title, IReadOnlyList<string> items, int selectedIndex, Action requestStop)
        {
            _items = items;
            _requestStop = requestStop;
            SelectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, items.Count - 1));
            Title = title;
            Width = Dim.Percent(80);
            Height = Dim.Percent(80);

            _list = new SelectionListView(this)
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(2),
                Height = Dim.Fill(3),
                CanFocus = true
            };

            var ok = new Button()
            {
                Text = "OK",
                X = Pos.Center() - 8,
                Y = Pos.Bottom(_list) + 1,
                IsDefault = true
            };
            var cancel = new Button()
            {
                Text = "Cancel",
                X = Pos.Right(ok) + 2,
                Y = Pos.Top(ok)
            };

            ok.Accepting += (_, e) =>
            {
                e.Handled = true;
                WasAccepted = true;
                _requestStop();
            };
            cancel.Accepting += (_, e) =>
            {
                e.Handled = true;
                WasAccepted = false;
                _requestStop();
            };

            Add(_list, ok, cancel);
            _list.SetFocus();
        }

        private void MoveSelection(int delta)
        {
            if (_items.Count == 0)
            {
                return;
            }

            SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, _items.Count - 1);
            _list.SetNeedsDraw();
        }

        private sealed class SelectionListView : View
        {
            private readonly SelectionDialog _owner;

            public SelectionListView(SelectionDialog owner)
            {
                _owner = owner;
            }

            protected override bool OnDrawingContent(DrawContext? context)
            {
                base.OnDrawingContent(context);
                var width = Math.Max(1, Viewport.Width);
                var height = Math.Min(Viewport.Height, _owner._items.Count);
                var firstVisible = GetFirstVisibleIndex(height);

                for (var row = 0; row < height; row++)
                {
                    var itemIndex = firstVisible + row;
                    if (itemIndex >= _owner._items.Count)
                    {
                        break;
                    }

                    var selected = itemIndex == _owner.SelectedIndex;
                    SetAttribute(selected ? GetScheme().Active : GetScheme().Normal);
                    Move(0, row);
                    AddStr(FormatItem(_owner._items[itemIndex], width));
                }

                return true;
            }

            protected override bool OnKeyDown(Key key)
            {
                if (key.KeyCode == KeyCode.CursorUp)
                {
                    _owner.MoveSelection(-1);
                    return true;
                }

                if (key.KeyCode == KeyCode.CursorDown)
                {
                    _owner.MoveSelection(1);
                    return true;
                }

                if (key.KeyCode == KeyCode.PageUp)
                {
                    _owner.MoveSelection(-Math.Max(1, Viewport.Height));
                    return true;
                }

                if (key.KeyCode == KeyCode.PageDown)
                {
                    _owner.MoveSelection(Math.Max(1, Viewport.Height));
                    return true;
                }

                if (key.KeyCode == KeyCode.Enter)
                {
                    _owner.WasAccepted = true;
                    _owner._requestStop();
                    return true;
                }

                if (key.KeyCode == KeyCode.Esc)
                {
                    _owner.WasAccepted = false;
                    _owner._requestStop();
                    return true;
                }

                return base.OnKeyDown(key);
            }

            private int GetFirstVisibleIndex(int height)
            {
                if (height <= 0 || _owner.SelectedIndex < height)
                {
                    return 0;
                }

                return Math.Min(_owner.SelectedIndex - height + 1, Math.Max(0, _owner._items.Count - height));
            }

            private static string FormatItem(string item, int width)
            {
                if (item.Length > width)
                {
                    item = item[..width];
                }

                return item.PadRight(width);
            }
        }
    }
}
