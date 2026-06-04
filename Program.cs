using MicroCode.Utils;
using MicroCode.Tui;
using OllamaSharp;

using Terminal.Gui.App;

Console.Title = "MicroCode";

var settings = AppSettings.Load(args.Contains("--dev"));
var ollama = new OllamaApiClient(new Uri(settings.Ollama.Host));

using IApplication app = Application.Create();

app.Init();

app.Run(new Microcode(app, settings, ollama));
