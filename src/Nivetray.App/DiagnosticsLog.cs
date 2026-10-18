namespace Nivetray.App;

internal sealed class DiagnosticsLog(string directory)
{
    private readonly object sync = new();
    private readonly Dictionary<string, DateTime> recent = [];
    public void Write(string message)
    {
        lock (sync)
        {
            var now = DateTime.UtcNow;
            if (recent.TryGetValue(message, out var previous) && now - previous < TimeSpan.FromMinutes(1)) return;
            if (recent.Count > 64) recent.Clear();
            recent[message] = now;
            try
            {
                Directory.CreateDirectory(directory);
                var file = Path.Combine(directory, "diagnostics.log");
                if (File.Exists(file) && new FileInfo(file).Length > 256 * 1024) File.Move(file, file + ".previous", true);
                File.AppendAllText(file, $"{now:O} {message}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
