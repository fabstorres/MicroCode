# MicroCode

MicroCode is a small C# console app that connects to a local Ollama server and runs an interactive chat session with tool support.

## Requirements

- .NET 10 SDK
- [Ollama](https://ollama.com/) instance running, defaulted to `http://localhost:11434`
- Model with thinking and tool calling.

## Run

```bash
dotnet run
```

Type your prompt at the `You:` prompt. Use slash commands (see below) to control the session.

## Commands

| Command         | Description                                      |
| --------------- | ------------------------------------------------ |
| `/help`         | Show all available commands                      |
| `/model`        | Display current model and list available models  |
| `/model <name>` | Switch to a different model (partial name match) |
| `/think`        | Toggle thinking mode on/off                      |
| `/clear`        | Clear the console                                |
| `/exit`         | Exit the REPL                                    |
| `/quit`         | Exit the REPL                                    |
| `/skills`       | Show all skills loaded during runtime            |

## Example Outputs

The following images show example outputs from the application:

<p align="center">
  <img src="assets/model_select.png" alt="Model Selection" width="48%" />
  <img src="assets/working_example.png" alt="Working Example" width="48%" />
</p>

## Notes

- Currently includes an unsafe bash tool (an unrestricted bash tool with user permissions. Use at your risk).
- The app uses `OllamaSharp` to talk to a local Ollama instance.
- Settings are stored in `~/.config/MicroCode/settings.json` (or `settings.json` in the project directory with `--dev` flag).

## License

This project is licensed under the MIT License. See `LICENSE` for details.
