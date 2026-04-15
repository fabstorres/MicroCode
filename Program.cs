using MicroCode.Tools;
using OllamaSharp;

Console.Title = "MicroCode";

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

var systemPrompt = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Prompts", "system.txt"));

var chat = new Chat(ollama, systemPrompt)
{
    Model = selectedModel.ModelName!,
    Think = true,
};

chat.OnThink += (_, thoughts) =>
{
    Console.ForegroundColor = ConsoleColor.Gray;
    Console.Write(thoughts);
    Console.ResetColor();
};

chat.OnToolCall += (_, call) =>
{
    Console.ForegroundColor = ConsoleColor.DarkMagenta;
    Console.WriteLine($"\n[tool call] {call.Function?.Name}({string.Join(", ", call.Function?.Arguments?.Select(a => $"{a.Key}: {a.Value}") ?? [])})");
    Console.ResetColor();
};

chat.OnToolResult += (_, result) =>
{
    Console.ForegroundColor = ConsoleColor.DarkGreen;
    Console.WriteLine($"[{result.Tool.ToString()}] {result.Result}");
    Console.ResetColor();
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

    Console.ForegroundColor = ConsoleColor.White;
    var response = chat.SendAsync(input, [new UnsafeBashTool()]);
    await foreach (var message in response)
    {
        Console.Write(message);
    }
    Console.WriteLine();
    Console.ResetColor();
}

string ExtractModelBaseName(string input)
{
    var afterSlash = input[(input.LastIndexOf('/') + 1)..];
    var result = afterSlash[..afterSlash.IndexOf(':')];
    return result;
}