using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ChudType.Mac;

internal static class MacInput
{
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const ulong ShiftMask = 1UL << 17;
    private const ulong ControlMask = 1UL << 18;
    private const ulong OptionMask = 1UL << 19;
    private const ulong CommandMask = 1UL << 20;
    private const ulong KnownModifierMask = ShiftMask | ControlMask | OptionMask | CommandMask;

    public static bool AccessibilityGranted => AXIsProcessTrusted();
    public static bool ShortcutDown(MacShortcut shortcut)
    {
        ulong required = shortcut.Modifier switch
        {
            "Control" => ControlMask,
            "Command" => CommandMask,
            "Option" => OptionMask,
            "Shift" => ShiftMask,
            _ => 0
        };
        ulong actual = CGEventSourceFlagsState(1) & KnownModifierMask;
        if (actual != required) return false;
        return shortcut.Trigger switch
        {
            "Middle Mouse" => CGEventSourceButtonState(1, 2),
            "Mouse Button 4" => CGEventSourceButtonState(1, 3),
            "Mouse Button 5" => CGEventSourceButtonState(1, 4),
            _ => KeyCode(shortcut.Trigger) is ushort key && CGEventSourceKeyState(1, key)
        };
    }

    private static ushort? KeyCode(string key) => key switch
    {
        "F6" => 97, "F7" => 98, "F8" => 100, "F9" => 101,
        "F10" => 109, "F11" => 103, "F12" => 111, "Space" => 49,
        "A" => 0, "S" => 1, "D" => 2, "F" => 3, "H" => 4, "G" => 5,
        "Z" => 6, "X" => 7, "C" => 8, "V" => 9, "B" => 11,
        "Q" => 12, "W" => 13, "E" => 14, "R" => 15, "Y" => 16, "T" => 17,
        "1" => 18, "2" => 19, "3" => 20, "4" => 21, "6" => 22, "5" => 23,
        "9" => 25, "7" => 26, "8" => 28, "0" => 29,
        "O" => 31, "U" => 32, "I" => 34, "P" => 35, "L" => 37,
        "J" => 38, "K" => 40, "N" => 45, "M" => 46,
        _ => null
    };

    public static void OpenAccessibilitySettings() => Process.Start(new ProcessStartInfo
    {
        FileName = "open",
        ArgumentList = { "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility" },
        UseShellExecute = false
    });

    public static bool TypeText(string text)
    {
        if (!AccessibilityGranted || string.IsNullOrEmpty(text)) return false;
        nint down = CGEventCreateKeyboardEvent(0, 0, true);
        nint up = CGEventCreateKeyboardEvent(0, 0, false);
        if (down == 0 || up == 0)
        {
            if (down != 0) CFRelease(down);
            if (up != 0) CFRelease(up);
            return false;
        }
        try
        {
            char[] characters = text.ToCharArray();
            CGEventKeyboardSetUnicodeString(down, characters.Length, characters);
            CGEventKeyboardSetUnicodeString(up, 0, []);
            CGEventPost(1, down);
            CGEventPost(1, up);
            return true;
        }
        finally
        {
            CFRelease(down);
            CFRelease(up);
        }
    }

    [DllImport(ApplicationServices)] private static extern bool AXIsProcessTrusted();
    [DllImport(ApplicationServices)] private static extern bool CGEventSourceKeyState(int stateId, ushort keyCode);
    [DllImport(ApplicationServices)] private static extern bool CGEventSourceButtonState(int stateId, int button);
    [DllImport(ApplicationServices)] private static extern ulong CGEventSourceFlagsState(int stateId);
    [DllImport(ApplicationServices)] private static extern nint CGEventCreateKeyboardEvent(nint source, ushort keyCode, bool keyDown);
    [DllImport(ApplicationServices)] private static extern void CGEventKeyboardSetUnicodeString(nint input, nint length, char[] text);
    [DllImport(ApplicationServices)] private static extern void CGEventPost(int tap, nint input);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(nint value);
}
