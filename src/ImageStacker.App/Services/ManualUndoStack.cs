using ImageStacker.Core;

namespace ImageStacker.App.Services;

internal sealed class ManualUndoStack
{
    private const int MaxDepth = 50;
    private readonly List<List<SlotAssignment?>> _undo = [];
    private readonly List<List<SlotAssignment?>> _redo = [];

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void Checkpoint(IReadOnlyList<SlotAssignment?> current)
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

    public List<SlotAssignment?>? Undo(IReadOnlyList<SlotAssignment?> current)
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        _redo.Add(Clone(current));
        List<SlotAssignment?> restored = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return Clone(restored);
    }

    public List<SlotAssignment?>? Redo(IReadOnlyList<SlotAssignment?> current)
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        _undo.Add(Clone(current));
        List<SlotAssignment?> restored = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return Clone(restored);
    }

    private static List<SlotAssignment?> Clone(IReadOnlyList<SlotAssignment?> source)
    {
        var copy = new List<SlotAssignment?>(source.Count);
        foreach (SlotAssignment? slot in source)
        {
            copy.Add(slot is null ? null : slot with { });
        }

        return copy;
    }
}
