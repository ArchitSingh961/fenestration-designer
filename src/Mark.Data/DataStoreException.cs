namespace Mark.Data;

/// <summary>
/// The local database cannot be used as asked: it is damaged, belongs to another application, was written by a newer
/// version, or an item is missing. The message is written for the user; nothing was changed when this is thrown.
/// </summary>
public sealed class DataStoreException : Exception
{
    public DataStoreException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}

/// <summary>
/// A library change was refused because it would leave the data inconsistent (e.g. deleting a product that saved
/// projects still reference). <see cref="Reasons"/> lists every blocker; nothing was changed.
/// </summary>
public sealed class LibraryOperationException : InvalidOperationException
{
    public LibraryOperationException(string message, IReadOnlyList<string> reasons) : base(message)
    {
        Reasons = reasons;
    }

    public IReadOnlyList<string> Reasons { get; }
}
