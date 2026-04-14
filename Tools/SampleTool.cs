using OllamaSharp;

namespace MicroCode.Tools;

/// <summary>
/// Sample tools exposed to the Ollama chat session.
/// </summary>
public static class SampleTools
{
	/// <summary>
	/// Get the current weather for a city
	/// </summary>
	/// <param name="city">Name of the city</param>
	/// <param name="unit">Temperature unit for the weather</param>
	[OllamaTool]
	public static string GetWeather(string city, Unit unit = Unit.Celsius) => $"It's cold at only 6° {unit} in {city}.";

    /// <summary>
    /// Supported temperature units.
    /// </summary>
    public enum Unit
    {
        /// <summary>
        /// Degrees Celsius.
        /// </summary>
        Celsius,
        /// <summary>
        /// Degrees Fahrenheit.
        /// </summary>
        Fahrenheit
    }
}