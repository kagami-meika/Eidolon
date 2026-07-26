namespace Eidolon.Core;

public sealed class Tile
{
    public const int DefaultSize = 256;

    public Tile(int size)
    {
        Size = size;
        Pixels = new ColorRgba8[size * size];
    }

    public int Size { get; }
    public ColorRgba8[] Pixels { get; }
    public uint Version { get; set; }

    public Tile Clone()
    {
        var t = new Tile(Size);
        Array.Copy(Pixels, t.Pixels, Pixels.Length);
        t.Version = Version;
        return t;
    }
}

public sealed class TileSurface
{
    private readonly Dictionary<long, Tile> _tiles = new();

    public TileSurface(int width, int height, int tileSize = Tile.DefaultSize)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        TileSize = tileSize;
    }

    public int Width { get; }
    public int Height { get; }
    public int TileSize { get; }

    public IReadOnlyDictionary<long, Tile> Tiles => _tiles;

    public static long Key(int tx, int ty) => ((long)tx << 32) | (uint)ty;

    public bool TryGetTile(int tx, int ty, out Tile tile) =>
        _tiles.TryGetValue(Key(tx, ty), out tile!);

    public Tile GetOrCreateTile(int tx, int ty)
    {
        var k = Key(tx, ty);
        if (_tiles.TryGetValue(k, out var tile))
            return tile;
        tile = new Tile(TileSize);
        _tiles[k] = tile;
        return tile;
    }

    public ColorRgba8 GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return ColorRgba8.Transparent;
        int ts = TileSize;
        int tx = x / ts;
        int ty = y / ts;
        int lx = x - tx * ts;
        int ly = y - ty * ts;
        if (!_tiles.TryGetValue(Key(tx, ty), out var tile))
            return ColorRgba8.Transparent;
        return tile.Pixels[ly * ts + lx];
    }

    public void SetPixel(int x, int y, ColorRgba8 color)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return;
        int ts = TileSize;
        int tx = x / ts;
        int ty = y / ts;
        int lx = x - tx * ts;
        int ly = y - ty * ts;
        var tile = GetOrCreateTile(tx, ty);
        tile.Pixels[ly * ts + lx] = color;
        tile.Version++;
    }

    public void Clear()
    {
        _tiles.Clear();
    }

    public IntRect? GetDirtyBoundsHint()
    {
        if (_tiles.Count == 0) return null;
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        int ts = TileSize;
        foreach (var (key, _) in _tiles)
        {
            int tx = (int)(key >> 32);
            int ty = (int)(key & 0xFFFFFFFF);
            minX = Math.Min(minX, tx * ts);
            minY = Math.Min(minY, ty * ts);
            maxX = Math.Max(maxX, tx * ts + ts - 1);
            maxY = Math.Max(maxY, ty * ts + ts - 1);
        }
        minX = Math.Clamp(minX, 0, Width - 1);
        minY = Math.Clamp(minY, 0, Height - 1);
        maxX = Math.Clamp(maxX, 0, Width - 1);
        maxY = Math.Clamp(maxY, 0, Height - 1);
        return IntRect.FromMinMax(minX, minY, maxX, maxY);
    }

    public Dictionary<long, Tile> SnapshotTiles(IEnumerable<long> keys)
    {
        var map = new Dictionary<long, Tile>();
        foreach (var k in keys)
        {
            if (_tiles.TryGetValue(k, out var t))
                map[k] = t.Clone();
            else
                map[k] = new Tile(TileSize); // empty snapshot
        }
        return map;
    }

    public void RestoreTiles(Dictionary<long, Tile> snapshot)
    {
        foreach (var (k, tile) in snapshot)
        {
            bool empty = true;
            for (int i = 0; i < tile.Pixels.Length; i++)
            {
                if (tile.Pixels[i].A != 0) { empty = false; break; }
            }
            if (empty)
                _tiles.Remove(k);
            else
                _tiles[k] = tile.Clone();
        }
    }

    public void CopyToBgra(Span<byte> bgra, int destStride, IntRect rect)
    {
        rect = rect.ClampTo(Width, Height);
        if (rect.IsEmpty) return;
        int ts = TileSize;
        for (int y = rect.Y; y < rect.Bottom; y++)
        {
            int ty = y / ts;
            int ly = y - ty * ts;
            for (int x = rect.X; x < rect.Right; x++)
            {
                int tx = x / ts;
                int lx = x - tx * ts;
                ColorRgba8 c = ColorRgba8.Transparent;
                if (_tiles.TryGetValue(Key(tx, ty), out var tile))
                    c = tile.Pixels[ly * ts + lx];
                int i = y * destStride + x * 4;
                bgra[i] = c.B;
                bgra[i + 1] = c.G;
                bgra[i + 2] = c.R;
                bgra[i + 3] = c.A;
            }
        }
    }

    public byte[] FlattenToBgra()
    {
        var data = new byte[Width * Height * 4];
        // white-clear not needed; transparent default
        CopyToBgra(data, Width * 4, new IntRect(0, 0, Width, Height));
        return data;
    }
}
