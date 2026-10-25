using FreeClipControl.Shell;

int checks = 0;
void Check(bool value) { if (!value) throw new Exception("Startup check failed"); checks++; }
string path = @"D:\中文目录\Headset Control\FreeClipControl.Shell.exe";
Check(StartupRegistration.BuildCommand(path) == "\"" + path + "\" --background");
foreach (string invalid in new[] { "", "relative.exe", @"C:\bad" + '"' + ".exe", "C:\\bad\n.exe", @"C:\app.dll", @"C:\" + new string('a', 260) + ".exe" })
{
    bool rejected = false;
    try { StartupRegistration.BuildCommand(invalid); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected);
}
var launch = ShellLaunchOptions.Parse(new[] { "--background" });
Check(launch.UseTray && !launch.ShowPanelOnLaunch && !launch.OpenSettings && !launch.KeepOpen);
Console.WriteLine($"{checks} startup command and launch-policy checks passed; no registry writes, UI or Bluetooth access.");
