
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using System.Collections.ObjectModel;
using System.Text;

using OllamaSharp;

namespace MicroCode.Tui;

class Microcode : Window
{
    private readonly ListView _messageList;
    private readonly TextField _inputField;
    private readonly ObservableCollection<string> _messages = new();
    private OllamaApiClient _ollama;
    private Chat chat;
    public Microcode()
    {
        _ollama = new OllamaApiClient("http://localhost:11434");
        chat = new Chat(_ollama)
        {
            Model = "gemma4-e4b-q4:latest"
        };
        Title = "Microcode";

        Border.Settings |= BorderSettings.TerminalTitle; // pushes title to terminal titlebar
        Border.Settings &= ~BorderSettings.Title;        // don't render title in the frame
        Border.Thickness = new Thickness(0);             // removes the border line

        _messageList = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 3, // leave room for input row + borders
            CanFocus = false
        };


        _inputField = new TextField()
        {
            X = 0,
            Y = Pos.Bottom(_messageList),
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
            _messages.Add(text);

            _inputField.Text = "";
            _messageList.SetSource(_messages);
            SendMessageAsync(text).GetAwaiter();
        };

        _messages.CollectionChanged += (s, e) =>
        {
            _messageList.MoveEnd();
        };

        Add(_messageList, _inputField);
    }

    private async Task SendMessageAsync(string text)
    {

        _messages.Add(""); // placeholder
        var assistantIdx = _messages.Count - 1;

        var sb = new StringBuilder();
        await foreach (var chunk in chat.SendAsync(text))
        {
            sb.Append(chunk);
            _messages[assistantIdx] = sb.ToString();
            _messageList.SetSource(_messages);
        }
    }
}
