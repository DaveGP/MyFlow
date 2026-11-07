using System.Runtime.InteropServices;
using System.Text;
using MyFlow.Core;

namespace MyFlow.Platform.MacOS;

/// <summary>
/// Stores API keys in the user's login Keychain (service "MyFlow"), via Security.framework.
/// Keys are never written to settings files or logs.
/// </summary>
public sealed partial class KeychainSecretStore(string service = "MyFlow") : ISecretStore
{
    private const int ErrSecItemNotFound = -25300;

    public string? Get(string name)
    {
        var s = Encoding.UTF8.GetBytes(service);
        var a = Encoding.UTF8.GetBytes(name);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a,
            out var length, out var data, out var item);
        if (status == ErrSecItemNotFound) return null;
        Check(status, "read");
        try
        {
            return Marshal.PtrToStringUTF8(data, (int)length);
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    public void Set(string name, string value)
    {
        var s = Encoding.UTF8.GetBytes(service);
        var a = Encoding.UTF8.GetBytes(name);
        var v = Encoding.UTF8.GetBytes(value);

        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a,
            out _, out var oldData, out var item);
        if (status == 0)
        {
            SecKeychainItemFreeContent(IntPtr.Zero, oldData);
            try
            {
                Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)v.Length, v), "update");
            }
            finally
            {
                CFRelease(item);
            }
            return;
        }
        if (status != ErrSecItemNotFound) Check(status, "read");

        Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a,
            (uint)v.Length, v, IntPtr.Zero), "save");
    }

    public void Delete(string name)
    {
        var s = Encoding.UTF8.GetBytes(service);
        var a = Encoding.UTF8.GetBytes(name);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a,
            out _, out var data, out var item);
        if (status == ErrSecItemNotFound) return;
        Check(status, "read");
        SecKeychainItemFreeContent(IntPtr.Zero, data);
        try
        {
            Check(SecKeychainItemDelete(item), "delete");
        }
        finally
        {
            CFRelease(item);
        }
    }

    private static void Check(int status, string action)
    {
        if (status != 0) throw new InvalidOperationException($"Keychain couldn't {action} the key (OSStatus {status}).");
    }

    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // These classic Keychain calls are deprecated but still supported, and much simpler to
    // call from C# than the SecItem* dictionary APIs.
    [LibraryImport(Security)]
    private static partial int SecKeychainFindGenericPassword(IntPtr keychainOrArray,
        uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainAddGenericPassword(IntPtr keychain,
        uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        uint passwordLength, byte[] passwordData, IntPtr itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemDelete(IntPtr itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr cf);
}
