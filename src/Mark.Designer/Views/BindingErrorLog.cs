using System.Diagnostics;
using System.IO;

namespace Mark.Designer.Views;

/// <summary>
/// Writes WPF data-binding errors (a property name that does not exist, a value that cannot be converted) to a file, so
/// every page can be checked for them. Started by <c>--binding-log &lt;file&gt;</c> on the command line.
/// </summary>
public static class BindingErrorLog
{
    private sealed class FileListener : TraceListener
    {
        private readonly string _path;
        private readonly object _gate = new();

        public FileListener(string path) => _path = path;

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            lock (_gate)
            {
                try
                {
                    File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
                }
                catch (IOException)
                {
                    // Logging must never stop the program.
                }
            }
        }
    }

    /// <summary>Starts writing binding warnings and errors to <paramref name="path"/>.</summary>
    public static void Start(string path)
    {
        PresentationTraceSources.Refresh();                  // WPF traces only with a debugger attached, unless refreshed
        var source = PresentationTraceSources.DataBindingSource;
        source.Listeners.Add(new FileListener(path));
        source.Switch.Level = SourceLevels.Warning;
    }
}
