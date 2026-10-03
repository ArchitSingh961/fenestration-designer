using System.IO;
using Mark.Core.Library;
using Mark.Data;
using Mark.Designer.ViewModels;

namespace Mark.Owner.ViewModels;

/// <summary>
/// Edits a library with the Library Manager on a temporary working-copy database in the Owner's work folder (deleted
/// afterwards). Used for a company's own items.
/// </summary>
public sealed class LibraryWorkingCopy
{
    private readonly ICatalogueEditorHost _host;
    private readonly string _workFolder;

    public LibraryWorkingCopy(ICatalogueEditorHost host, string workFolder)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _workFolder = workFolder;
    }

    /// <summary>Opens the Library Manager on <paramref name="start"/>; returns the library as it was closed, or null when nothing changed.</summary>
    /// <exception cref="InvalidOperationException">The working copy could not be created.</exception>
    public ProductLibrary? Edit(ProductLibrary start)
    {
        Directory.CreateDirectory(_workFolder);
        string path = Path.Combine(_workFolder, $"own-items-{Guid.NewGuid():N}.db");
        try
        {
            var store = LocalStore.Open(path);
            store.Library.Import(start);
            string before = LibrarySerializer.Serialize(store.Library.Current);
            _host.ShowLibraryManager(new LibraryManagerViewModel(store.Library, dialogs: _host.Dialogs));
            var after = store.Library.Current;
            return LibrarySerializer.Serialize(after) == before ? null : LibrarySerializer.Deserialize(LibrarySerializer.Serialize(after));
        }
        catch (Exception ex) when (ex is DataStoreException or IOException)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string file in new[] { path, path + "-wal", path + "-shm" })
            {
                try
                {
                    if (File.Exists(file)) File.Delete(file);
                }
                catch (IOException)
                {
                    // A leftover working copy in the Owner's own folder is harmless.
                }
            }
        }
    }
}
