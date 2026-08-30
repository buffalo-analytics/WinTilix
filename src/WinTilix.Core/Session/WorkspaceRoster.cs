using System.Collections.ObjectModel;

namespace WinTilix.Session;

/// <summary>
/// Ordered workspace tabs plus a single active selection. Used so tab
/// lifecycle can be tested without constructing WPF terminal panes.
/// </summary>
public sealed class WorkspaceRoster<T> : ObservableCollection<T> where T : class
{
    public T? Active { get; private set; }

    public void Activate(T item)
    {
        if (!Contains(item))
        {
            return;
        }

        Active = item;
    }

    public void ActivateNext()
    {
        if (Active is null || Count == 0)
        {
            return;
        }

        Activate(this[(IndexOf(Active) + 1) % Count]);
    }

    public void ActivatePrevious()
    {
        if (Active is null || Count == 0)
        {
            return;
        }

        Activate(this[(IndexOf(Active) - 1 + Count) % Count]);
    }

    public T? Close(T item, Func<T> createReplacement)
    {
        var index = IndexOf(item);
        if (index < 0)
        {
            return Active;
        }

        var wasActive = ReferenceEquals(Active, item);
        RemoveAt(index);
        if (Count == 0)
        {
            var replacement = createReplacement();
            Add(replacement);
            Activate(replacement);
            return replacement;
        }

        if (wasActive)
        {
            Activate(this[Math.Min(index, Count - 1)]);
        }

        return Active!;
    }

    public new void Clear()
    {
        base.Clear();
        Active = null;
    }
}
