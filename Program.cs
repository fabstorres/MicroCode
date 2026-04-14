using MicroCode.Tools;
using OllamaSharp;

var ollama = new OllamaApiClient(new Uri("http://localhost:11434"));

var models = await ollama.ListLocalModelsAsync();
var selectedModel = "";

do
{
    Console.Clear();

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write("=== ");
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("MicroCode");
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(" ===");

    Console.ResetColor();
    foreach (var (model, i) in models.Select((value, index) => (value, index)))
    {
        Console.WriteLine($"{i}: {model.ModelName}");
    }
    Console.Write("Enter to select a model: ");
    if (!int.TryParse(Console.ReadLine(), out int number)) continue;
    var choice = models.ElementAtOrDefault(number);
    selectedModel = choice?.ModelName ?? "";
    Console.WriteLine(selectedModel);
} while (string.IsNullOrWhiteSpace(selectedModel));

Console.Clear();
//TODO: Make sure model is compatiable with settings
var chat = new Chat(ollama)
{
    Model = selectedModel,

};
while (true)
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.Write("You: ");
    Console.ResetColor();
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) continue;
    if (input == "exit" || input == "quit")
    {
        break;
    }
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write($"{ExtractModelBaseName(selectedModel)}: ");
    Console.ResetColor();
    var response = chat.SendAsync(input);
    await foreach (var message in response)
    {
        Console.Write(message);
    }
    Console.WriteLine();
}

string ExtractModelBaseName(string input)
{
    var afterSlash = input[(input.LastIndexOf('/') + 1)..];
    var result = afterSlash[..afterSlash.IndexOf(':')];
    return result;
}