
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Editor;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Editor.Highlighting;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using System.Drawing;
using System.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

using OllamaSharp;

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
    private readonly TextField _inputField;
    private readonly Scheme _userMessageScheme = CreateUserMessageScheme();
    private readonly IApplication _app;
    private int _nextMessageY;
    private OllamaApiClient _ollama;
    private Chat chat;

    public Microcode(IApplication app)
    {
        _app = app;
        _ollama = new OllamaApiClient("http://192.168.1.24:11434");
        chat = new Chat(_ollama)
        {
            Model = "dolphin-llama3:latest"
        };
        Title = "Microcode";

        Border.Settings |= BorderSettings.TerminalTitle; // pushes title to terminal titlebar
        Border.Settings &= ~BorderSettings.Title;        // don't render title in the frame
        Border.Thickness = new Thickness(0);             // removes the border line

        _messageViewport = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 3, // leave room for input row + borders
            CanFocus = false,
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar
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

        _inputField = new TextField()
        {
            X = 0,
            Y = Pos.Bottom(_messageViewport),
            Width = Dim.Fill(),
            Height = 3,
            CanFocus = true,
            Border =
            {
                Thickness = new Thickness(0, 1, 0, 1), // top & bottom only
                LineStyle = LineStyle.Single
            }
        };

        _inputField.Accepting += (s, e) =>
        {
            var text = _inputField.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            AddMessageEditor(text, isUser: true);
            _inputField.Text = "";
            _ = SendMessageAsync(text);
        };

        _messageViewport.ViewportChanged += (s, e) => ReflowMessages();

        Add(_messageViewport, _inputField);
    }

    private async Task SendMessageAsync(string text)
    {
        var assistantEditor = AddMessageEditor("", isUser: false);

        try
        {
            await foreach (var chunk in chat.SendAsync(text))
            {
                AppendToMessageEditor(assistantEditor, chunk);
            }
        }
        catch (Exception ex)
        {
            AppendToMessageEditor(assistantEditor, $"{Environment.NewLine}Error: {ex.Message}");
        }
    }

    private Editor AddMessageEditor(string text, bool isUser)
    {
        var state = new MessageViewState()
        {
            IsUser = isUser
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

        if (isUser)
        {
            editor.SetScheme(_userMessageScheme);
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
        _app.Invoke(() =>
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
            _messageViewport.ScrollVertical(delta);
        }

        _messageViewport.SetNeedsDraw();
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
}
