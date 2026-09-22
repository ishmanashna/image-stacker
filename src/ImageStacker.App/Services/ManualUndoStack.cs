using ImageStacker.Core;

namespace ImageStacker.App.Services;

internal sealed record CollageUndoSnapshot(
    List<SlotAssignment?> Slots,
    bool Noise,
    bool Orton);

internal sealed class ManualUndoStack
{
    private const int MaxDepth = 50;
    private readonly List<CollageUndoSnapshot> _undo = [];
    private readonly List<CollageUndoSnapshot> _redo = [];

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void Checkpoint(CollageUndoSnapshot current)
    {
        _undo.Add(Clone(current));
        if (_undo.Count > MaxDepth)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public CollageUndoSnapshot? Undo(CollageUndoSnapshot current)
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        _redo.Add(Clone(current));
        CollageUndoSnapshot restored = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return Clone(restored);
    }

    public CollageUndoSnapshot? Redo(CollageUndoSnapshot current)
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        _undo.Add(Clone(current));
        CollageUndoSnapshot restored = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return Clone(restored);
    }

    private static CollageUndoSnapshot Clone(CollageUndoSnapshot source)
    {
        var slots = new List<SlotAssignment?>(source.Slots.Count);
        foreach (SlotAssignment? slot in source.Slots)
        {
            slots.Add(slot is null ? null : slot with { });
        }

        return new CollageUndoSnapshot(slots, source.Noise, source.Orton);
    }
}
