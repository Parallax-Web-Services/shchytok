using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Parallax.Station;

// The OS vault is separate from workspace journals and browser profiles.
internal static class DeviceCredential
{
    private const string Prefix = "Parallax.Station/Slipstream/";
    public static void Save(string origin, string token)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(token);
        var credential = new Credential { Type = 1, TargetName = Prefix + origin, UserName = "Station device", Persist = 2, BlobSize = (uint)bytes.Length, Blob = Marshal.AllocHGlobal(bytes.Length) };
        try { Marshal.Copy(bytes, 0, credential.Blob, bytes.Length); if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
        finally { Marshal.FreeHGlobal(credential.Blob); Array.Clear(bytes); }
    }
    public static string? Load(string origin)
    {
        if (!CredRead(Prefix + origin, 1, 0, out var pointer))
        { if (Marshal.GetLastWin32Error() == 1168) return null; throw new Win32Exception(Marshal.GetLastWin32Error()); }
        try { var credential = Marshal.PtrToStructure<Credential>(pointer); return Marshal.PtrToStringUni(credential.Blob, (int)credential.BlobSize / 2); }
        finally { CredFree(pointer); }
    }
    public static void Delete(string origin)
    { if (!CredDelete(Prefix + origin, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
}
