// using MicroCode.Cli;
// using MicroCode.Utils;
// using OllamaSharp;

// Console.Title = "MicroCode";

// var settings = AppSettings.Load(args.Contains("--dev"));
// var ollama = new OllamaApiClient(new Uri(settings.Ollama.Host));

// var repl = new Repl(settings, ollama);
// await repl.RunAsync();

using MicroCode.Tui;

using Terminal.Gui.App;

using IApplication app = Application.Create();

app.Init();

app.Run(new Microcode(app));
