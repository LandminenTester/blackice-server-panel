using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;
public interface IServerBackend : IAsyncDisposable
{
    ServerStatus Status();
    Task StartAsync();
    Task StopAsync(bool force = false);
    Task SendAsync(string command);
    string[] ConsoleSnapshot();
    string[] LogSnapshot();
    int? ExitCode() => null;
}
