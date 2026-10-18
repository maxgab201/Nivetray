namespace Nivetray.Core;

public static class TrayPolicy
{
    public const int MaximumRecent = 8;
    public static IReadOnlyList<HiddenWindow> Recent(IReadOnlyList<HiddenWindow> windows) =>
        windows.TakeLast(MaximumRecent).Reverse().ToArray();
}
