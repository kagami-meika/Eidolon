using System.IO.Compression;
using System.Text.Json;
using Eidolon.Core;

namespace Eidolon.IO;

public static class EidolonFileStore
{
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Save(Document doc, string path)
    {
        var tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);

        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var meta = new DocumentMeta
            {
                FormatVersion = FormatVersion,
                Width = doc.Width,
                Height = doc.Height,
                Dpi = doc.Dpi,
                BackgroundKind = doc.Background.Kind.ToString(),
                BackgroundColor = new[] { doc.Background.Color.R, doc.Background.Color.G, doc.Background.Color.B, doc.Background.Color.A },
                ActiveLayerId = doc.ActiveLayerId,
                Layers = CaptureLayers(doc.Root)
            };

            WriteJson(zip, "document.json", meta);

            foreach (var layer in doc.EnumerateRasterLayers())
            {
                string basePath = $"layers/{layer.Id:N}/";
                foreach (var (key, tile) in layer.Surface.Tiles)
                {
                    int tx = (int)(key >> 32);
                    int ty = (int)(key & 0xFFFFFFFF);
                    bool any = false;
                    foreach (var p in tile.Pixels)
                        if (p.A != 0) { any = true; break; }
                    if (!any) continue;

                    var entry = zip.CreateEntry($"{basePath}tiles/{tx}_{ty}.bin", CompressionLevel.Fastest);
                    using var s = entry.Open();
                    WriteTile(s, tile);
                }
            }
        }

        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
        doc.FilePath = path;
        doc.IsDirty = false;
    }

    public static Document Load(string path)
    {
        using var fs = File.OpenRead(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        var metaEntry = zip.GetEntry("document.json")
            ?? throw new InvalidDataException("Missing document.json");
        DocumentMeta meta;
        using (var s = metaEntry.Open())
            meta = JsonSerializer.Deserialize<DocumentMeta>(s, JsonOptions)
                   ?? throw new InvalidDataException("Invalid document.json");

        var doc = new Document(meta.Width, meta.Height, meta.Dpi);
        doc.Root.Children.Clear();
        doc.FilePath = path;

        if (Enum.TryParse<DocumentBackgroundKind>(meta.BackgroundKind, out var bk))
            doc.Background.Kind = bk;
        if (meta.BackgroundColor is { Length: 4 } bc)
            doc.Background.Color = new ColorRgba8(bc[0], bc[1], bc[2], bc[3]);

        if (meta.Layers is null || meta.Layers.Count == 0)
        {
            doc.AddRasterLayer("Layer 1");
        }
        else
        {
            foreach (var lm in meta.Layers)
            {
                var layer = new RasterLayer(doc.Width, doc.Height, lm.Name)
                {
                    Id = lm.Id,
                    Visible = lm.Visible,
                    Opacity = lm.Opacity,
                    Blend = Enum.TryParse<BlendMode>(lm.Blend, out var b) ? b : BlendMode.Normal,
                    ClippedToBelow = lm.ClippedToBelow,
                    Locks = (LayerLocks)lm.Locks
                };
                LoadTiles(zip, layer);
                doc.Root.Children.Add(layer);
            }
        }

        doc.ActiveLayerId = meta.ActiveLayerId ?? doc.Root.Children.LastOrDefault()?.Id;
        doc.History.Clear();
        doc.IsDirty = false;
        return doc;
    }

    public static void ExportPng(Document doc, string path, bool withTransparency)
    {
        int w = doc.Width, h = doc.Height;
        int stride = w * 4;
        var bgra = new byte[stride * h];
        var old = doc.Background.Kind;
        if (!withTransparency && doc.Background.Kind == DocumentBackgroundKind.Transparent)
            doc.Background.Kind = DocumentBackgroundKind.White;
        Compositor.CompositeToBgra(doc, bgra, stride);
        doc.Background.Kind = old;

        // Write PNG via System.Drawing is not available; use WPF-less manual PNG or raw and convert in App.
        // Store BGRA bytes path helper used by App with WPF encoder.
        File.WriteAllBytes(path + ".bgra.tmp", bgra);
        // Actual PNG written by PngExport in App; this method prepares composite.
        // For library purity, write simple uncompressed PNG here.
        WritePng(path, bgra, w, h, stride);
        try { File.Delete(path + ".bgra.tmp"); } catch { /* ignore */ }
    }

    private static void WritePng(string path, byte[] bgra, int w, int h, int stride)
    {
        // Minimal PNG encoder (RGBA)
        using var ms = new MemoryStream();
        // convert BGRA -> RGBA filter none rows
        var raw = new byte[(w * 4 + 1) * h];
        for (int y = 0; y < h; y++)
        {
            int row = y * (w * 4 + 1);
            raw[row] = 0; // filter None
            for (int x = 0; x < w; x++)
            {
                int si = y * stride + x * 4;
                int di = row + 1 + x * 4;
                raw[di] = bgra[si + 2];
                raw[di + 1] = bgra[si + 1];
                raw[di + 2] = bgra[si];
                raw[di + 3] = bgra[si + 3];
            }
        }
        var compressed = ZlibCompress(raw);

        using var fs = File.Create(path);
        // signature
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        WriteChunk(fs, "IHDR", BuildIhdr(w, h));
        WriteChunk(fs, "IDAT", compressed);
        WriteChunk(fs, "IEND", Array.Empty<byte>());
    }

    private static byte[] BuildIhdr(int w, int h)
    {
        var b = new byte[13];
        WriteInt(b, 0, w);
        WriteInt(b, 4, h);
        b[8] = 8; // bit depth
        b[9] = 6; // RGBA
        return b;
    }

    private static void WriteInt(byte[] b, int o, int v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteInt(len, 0, data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc32(typeBytes, data);
        var c = new byte[4];
        WriteInt(c, 0, unchecked((int)crc));
        s.Write(c);
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        // zlib header
        ms.WriteByte(0x78);
        ms.WriteByte(0x9C);
        using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            ds.Write(data, 0, data.Length);
        // adler32
        uint adler = Adler32(data);
        ms.WriteByte((byte)(adler >> 24));
        ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >> 8));
        ms.WriteByte((byte)adler);
        return ms.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (byte d in data)
        {
            a = (a + d) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte x in type) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (byte x in data) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    private static readonly uint[] CrcTable = CreateCrcTable();
    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static void LoadTiles(ZipArchive zip, RasterLayer layer)
    {
        string prefix = $"layers/{layer.Id:N}/tiles/";
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!entry.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(entry.FullName);
            var parts = name.Split('_');
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out int tx)) continue;
            if (!int.TryParse(parts[1], out int ty)) continue;
            using var s = entry.Open();
            var tile = ReadTile(s, layer.Surface.TileSize);
            var dest = layer.Surface.GetOrCreateTile(tx, ty);
            Array.Copy(tile.Pixels, dest.Pixels, tile.Pixels.Length);
            dest.Version++;
        }
    }

    private static List<LayerMeta> CaptureLayers(GroupLayer root)
    {
        var list = new List<LayerMeta>();
        foreach (var n in root.Children)
        {
            if (n is RasterLayer r)
            {
                list.Add(new LayerMeta
                {
                    Id = r.Id,
                    Name = r.Name,
                    Kind = "Raster",
                    Visible = r.Visible,
                    Opacity = r.Opacity,
                    Blend = r.Blend.ToString(),
                    ClippedToBelow = r.ClippedToBelow,
                    Locks = (int)r.Locks
                });
            }
        }
        return list;
    }

    private static void WriteJson<T>(ZipArchive zip, string name, T obj)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        JsonSerializer.Serialize(s, obj, JsonOptions);
    }

    private static void WriteTile(Stream s, Tile tile)
    {
        var buf = new byte[tile.Pixels.Length * 4];
        for (int i = 0; i < tile.Pixels.Length; i++)
        {
            var p = tile.Pixels[i];
            int o = i * 4;
            buf[o] = p.R; buf[o + 1] = p.G; buf[o + 2] = p.B; buf[o + 3] = p.A;
        }
        s.Write(buf);
    }

    private static Tile ReadTile(Stream s, int tileSize)
    {
        var tile = new Tile(tileSize);
        var buf = new byte[tileSize * tileSize * 4];
        int read = 0;
        while (read < buf.Length)
        {
            int n = s.Read(buf, read, buf.Length - read);
            if (n == 0) break;
            read += n;
        }
        int count = read / 4;
        for (int i = 0; i < count; i++)
        {
            int o = i * 4;
            tile.Pixels[i] = new ColorRgba8(buf[o], buf[o + 1], buf[o + 2], buf[o + 3]);
        }
        return tile;
    }

    private sealed class DocumentMeta
    {
        public int FormatVersion { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public float Dpi { get; set; }
        public string? BackgroundKind { get; set; }
        public byte[]? BackgroundColor { get; set; }
        public Guid? ActiveLayerId { get; set; }
        public List<LayerMeta>? Layers { get; set; }
    }

    private sealed class LayerMeta
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "Raster";
        public bool Visible { get; set; } = true;
        public float Opacity { get; set; } = 1f;
        public string Blend { get; set; } = "Normal";
        public bool ClippedToBelow { get; set; }
        public int Locks { get; set; }
    }
}
