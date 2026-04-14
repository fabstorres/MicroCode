using MicroCode.Tools;
using OllamaSharp;

var ollama = new OllamaApiClient(new Uri("http://localhost:11434"));

var models = (await ollama.ListLocalModelsAsync()).ToList();

if (models.Count == 0)
{
    Console.WriteLine("No models installed. Please install a model first.");
    return;
}

OllamaSharp.Models.Model? selectedModel = null;

do
{
    Console.Clear();

    var logo = new[]
    {
        "@@@@@@@%**%#%@@@@@@@",
        "@@@@#     +  +:#@@@@",
        "@@%       +  +   %@@",
        "@#        +  +    #@",
        "@:        +  +   :%@",
        "%         +  ++@=  %",
        "@:        +-##    :@",
        "@*      -*#  +    *@",
        "@@#  +*   +  +   #@@",
        "@@@@%     +  +.#@@@@",
        "@@@@@@@%**%*%@@@@@@@",
    };
    var title = "MicroCode by Fabs";

    for (int i = 0; i < logo.Length; i++)
    {
        var message = i == logo.Length - 1 ? "  " + title : "";
        Console.WriteLine(logo[i] + message);
    }
    Console.WriteLine();
    foreach (var (model, i) in models.Select((value, index) => (value, index)))
    {
        Console.WriteLine($"{i}: {model.ModelName}");
    }
    Console.Write("Enter a number to select a model: ");
    if (!int.TryParse(Console.ReadLine(), out int number)) continue;
    selectedModel = models.ElementAtOrDefault(number);
    Console.WriteLine(selectedModel);
} while (selectedModel is null);

Console.Clear();
//TODO: Make sure model is compatiable with settings
var chat = new Chat(ollama)
{
    Model = selectedModel.ModelName!,
    Think = true,
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
    Console.Write($"{ExtractModelBaseName(selectedModel.ModelName!)}: ");
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