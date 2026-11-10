using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace ChudType.Mac;

internal static class MacSystemIntegration
{
    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";
    private const string Label = "ai.chudtype.desktop";
    private static string AgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "LaunchAgents", Label + ".plist");

    public static void SetDockVisible(bool visible)
    {
        nint applicationClass = objc_getClass("NSApplication");
        nint application = IntPtrMessage(applicationClass, sel_registerName("sharedApplication"));
        LongMessage(application, sel_registerName("setActivationPolicy:"), visible ? 0 : 1);
    }

    public static void SetLaunchAtLogin(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AgentPath)!);
        string domain = "gui/" + geteuid();
        RunLaunchCtl("bootout", domain + "/" + Label, ignoreFailure: true);
        if (!enabled)
        {
            if (File.Exists(AgentPath)) File.Delete(AgentPath);
            return;
        }

        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The ChudType executable path is unavailable.");
        string xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict>
              <key>Label</key><string>{Label}</string>
              <key>ProgramArguments</key><array><string>{SecurityElement.Escape(executable)}</string></array>
              <key>RunAtLoad</key><true/>
              <key>ProcessType</key><string>Interactive</string>
            </dict></plist>
            """;
        string temporary = AgentPath + ".new";
        File.WriteAllText(temporary, xml);
        File.Move(temporary, AgentPath, overwrite: true);
        RunLaunchCtl("bootstrap", domain, AgentPath);
    }

    private static void RunLaunchCtl(string action, string target, string? path = null, bool ignoreFailure = false)
    {
        var start = new ProcessStartInfo { FileName = "/bin/launchctl", RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(action);
        start.ArgumentList.Add(target);
        if (path is not null) start.ArgumentList.Add(path);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("launchctl could not start.");
        process.WaitForExit();
        if (!ignoreFailure && process.ExitCode != 0)
            throw new InvalidOperationException(process.StandardError.ReadToEnd().Trim());
    }

    [DllImport(ObjectiveC)] private static extern nint objc_getClass(string name);
    [DllImport(ObjectiveC)] private static extern nint sel_registerName(string name);
    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")] private static extern nint IntPtrMessage(nint receiver, nint selector);
    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")] private static extern bool LongMessage(nint receiver, nint selector, long value);
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern uint geteuid();
}
