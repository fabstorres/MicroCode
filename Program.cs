using MicroCode.Tools;
using OllamaSharp;

var ollama = new OllamaApiClient(new Uri("http://localhost:11434"), "nexusriot/qwen3.5-opus-distil:9b");
var chat = new Chat(ollama);

while (true) {
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.Write("You: ");
    Console.ResetColor();
    var input = Console.ReadLine();
    if (input == "exit" || input == "quit") {
        break;
    }
    Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("Ollama: ");
        Console.ResetColor();
        var response = chat.SendAsync(input!, [new GetWeatherTool()]);
        await foreach (var message in response) {
            Console.Write(message);
        }
    Console.WriteLine();
}
        