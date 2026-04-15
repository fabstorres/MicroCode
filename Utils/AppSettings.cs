using System.Text.Json;
namespace MicroCode.Utils;

/// <summary>
/// Application settings loaded from the user config file or generate 
/// a new settings.json in the OS' special e.g in Linux '~/.config/MicroCode'
/// or in the project directory for developer mode
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Ollama specific settings
    /// </summary>
    public OllamaSettings Ollama { get; set; } = new();

    /// <summary>
    /// Loads or creates default user settings
    /// </summary>
    /// <param name="devEnv">If true, loads from the current directory instead.</param>
    /// <returns>AppSettings</returns>
    public static AppSettings Load(bool devEnv = false)
    {
        var configDir = devEnv
            ? Environment.CurrentDirectory
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MicroCode");

        var configPath = Path.Combine(configDir, "settings.json");

        if (!File.Exists(configPath))
        {
            Directory.CreateDirectory(configDir);
            File.WriteAllText(configPath, """
                {
                  "Ollama": {
                    "Host": "http://localhost:11434",
                    "FavoriteModel": ""
                  }
                }
                """);
            Console.WriteLine($"Config created at: {configPath}");
            Console.WriteLine("Edit it to customize your settings.");
        }

        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(configPath))
            ?? new AppSettings();
    }
}

/// <summary>Settings for the Ollama API client.</summary>
public class OllamaSettings
{
    /// <summary>Gets or sets the Ollama host URL.</summary>
    public string Host { get; set; } = "http://localhost:11434";
    /// <summary>Gets or sets the preferred model name.</summary>
    public string FavoriteModel { get; set; } = "";
}