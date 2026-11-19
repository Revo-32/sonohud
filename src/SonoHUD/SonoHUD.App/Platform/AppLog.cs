using System.Text.Json;
namespace SonoHUD.App.Platform;

internal sealed class AppLog : IDisposable
{
    readonly StreamWriter writer; readonly object gate = new(); bool disposed;
    internal AppLog(string directory) { Directory.CreateDirectory(directory); writer = new(new FileStream(Path.Combine(directory, $"app-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true }; Write("started", new { pid = Environment.ProcessId, version = typeof(AppLog).Assembly.GetName().Version?.ToString() }); }
    internal void Write(string kind, object? data = null) { lock (gate) { if (disposed) return; try { writer.WriteLine(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, kind, data })); } catch (IOException) { } } }
    internal void Error(string context, Exception e) => Write("error", new { context, type = e.GetType().Name, code = e.HResult });
    public void Dispose() { lock (gate) { if (disposed) return; Write("stopped"); disposed = true; writer.Dispose(); } }
}
