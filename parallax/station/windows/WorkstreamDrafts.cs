using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Parallax.Station;

// Drafts are device-local, scoped by origin/user/team/workstream and encrypted
// by Windows DPAPI for the current OS user. They are never operational history.
internal static class WorkstreamDrafts
{
    private static string FileName(string directory,WorkspaceIdentity workspace,long id)
    {
        if (id <= 0) throw new ArgumentException("Invalid workstream.");
        return Path.Combine(directory,"drafts",workspace.Key,id.ToString(System.Globalization.CultureInfo.InvariantCulture)+".bin");
    }
    internal static string Read(string directory,WorkspaceIdentity workspace,long id)
    {
        string path = FileName(directory,workspace,id);
        if (!File.Exists(path)) return "";
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > 200000) throw new IOException("This draft could not be read. Its existing file was preserved.");
        return Encoding.UTF8.GetString(Transform(bytes,false));
    }
    internal static void Save(string directory,WorkspaceIdentity workspace,long id,string text)
    {
        if (text.Length > 20000) throw new ArgumentException("Directions can contain at most 20,000 characters.");
        string path = FileName(directory,workspace,id);
        if (text.Length == 0) { if (File.Exists(path)) File.Delete(path); return; }
        byte[] encrypted = Transform(Encoding.UTF8.GetBytes(text),true);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(temporary,encrypted); File.Move(temporary,path,true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static byte[] Transform(byte[] bytes,bool protect)
    {
        var input = new Blob { Size=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length) };
        Blob output=default;
        try
        {
            Marshal.Copy(bytes,0,input.Data,bytes.Length);
            bool ok = protect ? CryptProtectData(ref input,null,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output)
                : CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(),"Station could not secure or restore this draft. Its existing file was preserved.");
            byte[] result=new byte[output.Size];Marshal.Copy(output.Data,result,0,result.Length);return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if(output.Data!=IntPtr.Zero)LocalFree(output.Data); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size;public IntPtr Data; }
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,uint flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,uint flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
