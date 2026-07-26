namespace Eidolon.Core;

public interface IDocumentCommand
{
    string Name { get; }
    void Redo(Document doc);
    void Undo(Document doc);
}

public sealed class HistoryStack
{
    private readonly List<IDocumentCommand> _undo = new();
    private readonly List<IDocumentCommand> _redo = new();

    public int Limit { get; set; } = 100;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public event EventHandler? Changed;
    /// <summary>Fires only on new operations (push/execute), not on undo/redo.</summary>
    public event EventHandler? OperationPushed;

    public void Execute(IDocumentCommand cmd, Document doc)
    {
        cmd.Redo(doc);
        _undo.Add(cmd);
        if (_undo.Count > Limit)
            _undo.RemoveAt(0);
        _redo.Clear();
        doc.IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
        OperationPushed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Command already applied (e.g. live stroke); only push to undo.</summary>
    public void PushAlreadyDone(IDocumentCommand cmd, Document doc)
    {
        _undo.Add(cmd);
        if (_undo.Count > Limit)
            _undo.RemoveAt(0);
        _redo.Clear();
        doc.IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
        OperationPushed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo(Document doc)
    {
        if (_undo.Count == 0) return;
        var cmd = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        cmd.Undo(doc);
        _redo.Add(cmd);
        doc.IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo(Document doc)
    {
        if (_redo.Count == 0) return;
        var cmd = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        cmd.Redo(doc);
        _undo.Add(cmd);
        doc.IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Stores before/after tiles for a stroke or paint operation.</summary>
public sealed class TileEditCommand : IDocumentCommand
{
    private readonly Guid _layerId;
    private readonly Dictionary<long, Tile> _before;
    private readonly Dictionary<long, Tile> _after;

    public TileEditCommand(Guid layerId, Dictionary<long, Tile> before, Dictionary<long, Tile> after, string name = "Paint")
    {
        _layerId = layerId;
        _before = before;
        _after = after;
        Name = name;
    }

    public string Name { get; }

    public void Undo(Document doc) => Apply(doc, _before);
    public void Redo(Document doc) => Apply(doc, _after);

    private void Apply(Document doc, Dictionary<long, Tile> tiles)
    {
        if (doc.FindLayer(_layerId) is not RasterLayer layer)
            return;
        layer.Surface.RestoreTiles(tiles);
    }
}

/// <summary>Vector layer stroke list snapshot (node edit / close-fill / draw).</summary>
public sealed class VectorLayerEditCommand : IDocumentCommand
{
    private readonly Guid _layerId;
    private readonly List<VectorStroke> _before;
    private readonly List<VectorStroke> _after;

    public VectorLayerEditCommand(Guid layerId, List<VectorStroke> before, List<VectorStroke> after, string name = "Vector")
    {
        _layerId = layerId;
        _before = before;
        _after = after;
        Name = name;
    }

    public string Name { get; }

    public void Undo(Document doc) => Apply(doc, _before);
    public void Redo(Document doc) => Apply(doc, _after);

    private void Apply(Document doc, List<VectorStroke> strokes)
    {
        if (doc.FindLayer(_layerId) is not VectorLayer layer)
            return;
        layer.ReplaceStrokes(strokes);
    }
}
