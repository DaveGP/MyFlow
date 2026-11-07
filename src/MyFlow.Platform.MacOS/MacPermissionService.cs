using System.Runtime.InteropServices;
using MyFlow.Core;

namespace MyFlow.Platform.MacOS;

/// <summary>
/// Accessibility is what lets MyFlow see the Globe key and paste into other apps.
/// Microphone permission is requested by macOS automatically the first time we record.
/// </summary>
public sealed partial class MacPermissionService : IPermissionService
{
    public bool IsAccessibilityTrusted() => AXIsProcessTrusted();

    public void RequestAccessibility()
    {
        // AXIsProcessTrustedWithOptions({kAXTrustedCheckOptionPrompt: true}) shows the system dialog.
        var key = CFStringCreateWithCString(IntPtr.Zero, "AXTrustedCheckOptionPrompt", 0x08000100 /* UTF8 */);
        var trueValue = Marshal.ReadIntPtr(NativeLibrary.GetExport(
            NativeLibrary.Load(CoreFoundation), "kCFBooleanTrue"));
        var keys = new[] { key };
        var values = new[] { trueValue };
        var dict = CFDictionaryCreate(IntPtr.Zero, keys, values, 1, IntPtr.Zero, IntPtr.Zero);
        try
        {
            AXIsProcessTrustedWithOptions(dict);
        }
        finally
        {
            CFRelease(dict);
            CFRelease(key);
        }
    }

    public void OpenAccessibilitySettings() =>
        _ = MacTools.OpenUrlAsync("x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility");

    public void OpenMicrophoneSettings() =>
        _ = MacTools.OpenUrlAsync("x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone");

    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [LibraryImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool AXIsProcessTrusted();

    [LibraryImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool AXIsProcessTrustedWithOptions(IntPtr options);

    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr alloc, string str, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values,
        nint numValues, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr cf);
}
