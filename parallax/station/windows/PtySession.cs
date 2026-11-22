using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Parallax.Station;

public sealed class PtySession : IDisposable
{
    private IntPtr console;
    private readonly FileStream input;
    private readonly SemaphoreSlim inputLock = new(1, 1);
    private readonly Process process;
    private int disposed;
    public int ProcessId { get; }
    public string DisplayName { get; }

    public PtySession(string shell, bool isolatedTest, int columns, int rows, Action<string> received, Action<int>? exited = null)
    {
        var launch = TerminalProfile.Resolve(shell,isolatedTest);
        DisplayName = launch.DisplayName;
        SafeFileHandle? inputRead = null, inputWrite = null, outputRead = null, outputWrite = null;
        IntPtr attributes = IntPtr.Zero; bool initialized = false;
        ProcessInformation created = default;
        try
        {
            if (!CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0) ||
                !CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0)) ThrowLastError();
            Marshal.ThrowExceptionForHR(CreatePseudoConsole(Size(columns, rows), inputRead!, outputWrite!, 0, out console));
            UIntPtr bytes = UIntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes);
            attributes = Marshal.AllocHGlobal(checked((int)bytes.ToUInt64()));
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes)) ThrowLastError();
            initialized = true;
            if (!UpdateProcThreadAttribute(attributes, 0, new IntPtr(0x00020016), console, new UIntPtr((uint)IntPtr.Size), IntPtr.Zero, IntPtr.Zero)) ThrowLastError();
            var startup = new StartupInfoEx { StartupInfo = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfoEx>() }, Attributes = attributes };
            var command = new StringBuilder("\"" + launch.Executable + "\"" + launch.Arguments);
            if (!CreateProcessW(launch.Executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x00080000,
                    IntPtr.Zero, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ref startup, out created)) ThrowLastError();
            ProcessId = (int)created.ProcessId;
            process = Process.GetProcessById(ProcessId);
            _ = Task.Run(async () =>
            {
                try { await process.WaitForExitAsync(); if (Volatile.Read(ref disposed) == 0) exited?.Invoke(process.ExitCode); }
                catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException) { }
            });
            input = new FileStream(inputWrite!, FileAccess.Write, 4096, isAsync: false); inputWrite = null;
            var output = new FileStream(outputRead!, FileAccess.Read, 4096, isAsync: false); outputRead = null;
            _ = Task.Run(async () =>
            {
                using (output)
                using (var reader = new StreamReader(output, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true))
                {
                    try
                    {
                        var buffer = new char[4096]; int count;
                        while ((count = await reader.ReadAsync(buffer)) > 0) received(new string(buffer, 0, count));
                    }
                    catch (Exception error) when (error is IOException or ObjectDisposedException) { }
                }
            });
        }
        catch
        {
            if (console != IntPtr.Zero) { IntPtr failed = console; console = IntPtr.Zero; _ = Task.Run(() => ClosePseudoConsole(failed)); }
            throw;
        }
        finally
        {
            inputRead?.Dispose(); outputWrite?.Dispose(); inputWrite?.Dispose(); outputRead?.Dispose();
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (created.Thread != IntPtr.Zero) CloseHandle(created.Thread);
            if (created.Process != IntPtr.Zero) CloseHandle(created.Process);
        }
    }

    private static Coord Size(int columns, int rows) => new() { X = (short)Math.Clamp(columns, 2, 500), Y = (short)Math.Clamp(rows, 2, 200) };
    public void Resize(int columns, int rows)
    { if (Volatile.Read(ref disposed) == 0) Marshal.ThrowExceptionForHR(ResizePseudoConsole(console, Size(columns, rows))); }

    public async Task WriteAsync(string value)
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(PtySession));
        if (process.HasExited) throw new InvalidOperationException("The shell has exited. Use Restart shell to continue.");
        await inputLock.WaitAsync();
        try
        {
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(PtySession));
            await input.WriteAsync(Encoding.UTF8.GetBytes(value)); await input.FlushAsync();
        }
        finally { inputLock.Release(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        IntPtr closing = Interlocked.Exchange(ref console, IntPtr.Zero);
        // Closing ConPTY can emit a final frame. Keep the independent output
        // pump draining, and never block the WPF dispatcher during teardown.
        _ = Task.Run(() =>
        {
            if (closing != IntPtr.Zero) ClosePseudoConsole(closing);
            input.Dispose(); process.Dispose();
        });
    }

    private static void ThrowLastError() => throw new Win32Exception(Marshal.GetLastWin32Error());
    [StructLayout(LayoutKind.Sequential)] private struct Coord { public short X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo
    {
        public uint Size; public IntPtr Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, ReservedBytes; public IntPtr ReservedData, StandardInput, StandardOutput, StandardError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, uint size);
    [DllImport("kernel32.dll")] private static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);
    [DllImport("kernel32.dll")] private static extern int ResizePseudoConsole(IntPtr console, Coord size);
    [DllImport("kernel32.dll")] private static extern void ClosePseudoConsole(IntPtr console);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref UIntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, UIntPtr size, IntPtr previous, IntPtr returnedSize);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
