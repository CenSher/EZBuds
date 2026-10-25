using System;
using System.IO;
using Microsoft.Win32;

namespace FreeClipControl.Shell;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FreeClipControl";

    internal static string ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    internal static string BuildCommand(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable)
            || executable.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0
            || !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("无法确定应用的完整启动路径。");
        string command = "\"" + executable + "\" --background";
        // Windows documents a 260-character limit for Run command lines.
        if (command.Length > 260) throw new InvalidOperationException("应用路径过长，请移到较短的目录后重试。");
        return command;
    }

    internal static string CurrentCommand => BuildCommand(Environment.ProcessPath);

    internal static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            string command = CurrentCommand;
            if (!File.Exists(Environment.ProcessPath)) throw new IOException("启动文件不存在。");
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
            if (!string.Equals(ReadCommand(), command, StringComparison.Ordinal))
                throw new IOException("开机自启动设置未能保存。");
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(ValueName, false);
            if (ReadCommand() != null) throw new IOException("开机自启动设置未能移除。");
        }
    }
}
