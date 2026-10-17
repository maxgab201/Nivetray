using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nivetray.Core;

public sealed class PreferencesStore(string directory, Action<string> log)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string FilePath => Path.Combine(directory, "settings.json");
    public Preferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var value = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath), Options)
                ?? throw new JsonException("Empty settings.");
            if (value.Rules is null || value.Rules.Any(r => r is null ||
                string.IsNullOrWhiteSpace(r.ExecutablePath) || !Enum.IsDefined(r.Mode)))
                throw new JsonException("Invalid application rules.");
            return value;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            log("Settings could not be loaded: " + ex.GetType().Name);
            // Preserve the original for manual recovery; never overwrite it on load.
            return new();
        }
    }
    public void Save(Preferences preferences)
    {
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, Options));
        File.Move(temporary, FilePath, true);
    }
}
