using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nivetray.Core;

public sealed class PreferencesStore(string directory, Action<string> log)
{
    private bool preserveOriginal;
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
            if (!Enum.IsDefined(value.Theme) || value.SchemaVersion > 2 || value.Rules is null || value.Rules.Any(r => r is null ||
                string.IsNullOrWhiteSpace(r.ExecutablePath) || !Enum.IsDefined(r.Mode)))
                throw new JsonException("Invalid application rules.");
            // v1 files have no version field: JSON defaults preserve their modes and close behavior.
            value.SchemaVersion = 2;
            return value;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            log("Settings could not be loaded: " + ex.GetType().Name);
            preserveOriginal = true;
            // Preserve the original for manual recovery; never overwrite it on load.
            return new();
        }
    }
    public void Save(Preferences preferences)
    {
        Directory.CreateDirectory(directory);
        if (preserveOriginal && File.Exists(FilePath))
        {
            File.Copy(FilePath, FilePath + ".recovered-" + Guid.NewGuid().ToString("N") + ".bak");
            preserveOriginal = false;
        }
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, Options));
        File.Move(temporary, FilePath, true);
    }
}
