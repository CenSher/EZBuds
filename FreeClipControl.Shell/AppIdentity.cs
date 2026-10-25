namespace FreeClipControl.Shell;

internal static class AppIdentity
{
    internal const string Name = "EZBuds";
    internal static string VersionLabel => "版本 " + (typeof(AppIdentity).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
}
