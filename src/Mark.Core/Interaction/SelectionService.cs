namespace Mark.Core.Interaction;

/// <summary>
/// The current selection, as domain object Ids (frames, profiles, glass panels), never UI objects.
/// Session state only: it is not saved in the project file.
/// </summary>
public interface ISelectionService
{
    IReadOnlySet<Guid> SelectedIds { get; }

    int Count { get; }

    /// <summary>Raised once after every change that actually changed the selection.</summary>
    event Action? Changed;

    bool Contains(Guid id);

    /// <summary>Replaces the selection with one object.</summary>
    void Select(Guid id);

    /// <summary>Replaces the selection with <paramref name="ids"/>.</summary>
    void SelectMany(IEnumerable<Guid> ids);

    void Add(Guid id);

    void AddMany(IEnumerable<Guid> ids);

    void Remove(Guid id);

    /// <summary>Adds the object if unselected, removes it if selected.</summary>
    void Toggle(Guid id);

    void Clear();

    /// <summary>Drops Ids for which <paramref name="exists"/> is false (e.g. objects removed by undo). Returns how many.</summary>
    int Prune(Func<Guid, bool> exists);
}

public sealed class SelectionService : ISelectionService
{
    private readonly HashSet<Guid> _ids = new();

    public IReadOnlySet<Guid> SelectedIds => _ids;

    public int Count => _ids.Count;

    public event Action? Changed;

    public bool Contains(Guid id) => _ids.Contains(id);

    public void Select(Guid id) => SelectMany(new[] { id });

    public void SelectMany(IEnumerable<Guid> ids)
    {
        var next = ids.ToHashSet();
        if (_ids.SetEquals(next)) return;
        _ids.Clear();
        _ids.UnionWith(next);
        Changed?.Invoke();
    }

    public void Add(Guid id)
    {
        if (_ids.Add(id)) Changed?.Invoke();
    }

    public void AddMany(IEnumerable<Guid> ids)
    {
        int before = _ids.Count;
        _ids.UnionWith(ids);
        if (_ids.Count != before) Changed?.Invoke();
    }

    public void Remove(Guid id)
    {
        if (_ids.Remove(id)) Changed?.Invoke();
    }

    public void Toggle(Guid id)
    {
        if (!_ids.Remove(id)) _ids.Add(id);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_ids.Count == 0) return;
        _ids.Clear();
        Changed?.Invoke();
    }

    public int Prune(Func<Guid, bool> exists)
    {
        int removed = _ids.RemoveWhere(id => !exists(id));
        if (removed > 0) Changed?.Invoke();
        return removed;
    }
}
