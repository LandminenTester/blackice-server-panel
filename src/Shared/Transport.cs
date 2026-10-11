using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Net.Sockets;
namespace ReplayPanel.Shared;
// Unix endpoints are private to the service account; named pipes use Windows current-user ACLs.
public static class Transport
{
    [DllImport("kernel32.dll")] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint id);
    public static int? PeerId(Stream stream)
    {
        if(OperatingSystem.IsWindows()&&stream is NamedPipeClientStream pipe&&GetNamedPipeServerProcessId(pipe.SafePipeHandle,out var id))return (int)id;
        return stream is PeerNetworkStream peer?peer.ProcessId:null;
    }
    private sealed class PeerNetworkStream : NetworkStream
    {
        [StructLayout(LayoutKind.Sequential)] private struct Credential {public int Pid;public uint Uid;public uint Gid;}
        [DllImport("libc",SetLastError=true)] private static extern int getsockopt(int socket,int level,int option,out Credential value,ref uint length);
        public int? ProcessId {get;}
        public PeerNetworkStream(Socket socket):base(socket,true)
        {
            uint length=12;
            if(getsockopt(socket.SafeHandle.DangerousGetHandle().ToInt32(),1,17,out var credential,ref length)==0&&credential.Pid>0)ProcessId=credential.Pid;
        }
    }
    public static string SocketPath(string name) => Path.Combine(Environment.GetEnvironmentVariable("REPLAY_PANEL_RUNTIME") ?? Path.Combine(Path.GetTempPath(), "replay-panel-" + Environment.UserName), name + ".sock");
    public static async Task<Stream> Connect(string name, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows()) { var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly); try { await pipe.ConnectAsync(3000, ct); return pipe; } catch { pipe.Dispose(); throw; } }
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath(name)), ct); return new PeerNetworkStream(socket); } catch { socket.Dispose(); throw; }
    }
    public static async Task Listen(string name, Func<Stream, CancellationToken, Task> handle, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            while (!ct.IsCancellationRequested) { using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly); try { await pipe.WaitForConnectionAsync(ct); await handle(pipe, ct); } catch (IOException) {} }
            return;
        }
        var path = SocketPath(name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.SetUnixFileMode(Path.GetDirectoryName(path)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (File.Exists(path)) File.Delete(path); // Caller holds the process lock before listening.
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path)); File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); listener.Listen(16);
        try { while (!ct.IsCancellationRequested) { using var client = await listener.AcceptAsync(ct); using var stream = new NetworkStream(client, false); try { await handle(stream, ct); } catch (IOException) {} } } finally { File.Delete(path); }
    }
}
