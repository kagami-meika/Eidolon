using System.IO;
using System.Text;
using Eidolon.Core;

namespace Eidolon.IO;

/// <summary>
/// Minimal PSD writer (RGB 8-bit). Writes merged composite as image data
/// and a single full-canvas raster layer matching the composite.
/// </summary>
public static class PsdWriter
{
    public static void Write(Document doc, string path, byte[] compositeBgra, int stride)
    {
        int w = doc.Width, h = doc.Height;
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        // ---- File Header ----
        WriteAscii(bw, "8BPS");
        WriteU16(bw, 1);
        bw.Write(new byte[6]);
        WriteU16(bw, 3); // RGB channels
        WriteU32(bw, (uint)h);
        WriteU32(bw, (uint)w);
        WriteU16(bw, 8);
        WriteU16(bw, 3); // RGB mode

        WriteU32(bw, 0); // color mode data
        WriteU32(bw, 0); // image resources

        // ---- Layer and Mask Information ----
        byte[] layerSection = BuildLayerSection(compositeBgra, w, h, stride, "Composite");
        WriteU32(bw, (uint)layerSection.Length);
        bw.Write(layerSection);

        // ---- Merged Image Data ----
        WriteU16(bw, 0); // raw compression
        WritePlanarRgb(bw, compositeBgra, w, h, stride);
    }

    private static byte[] BuildLayerSection(byte[] bgra, int w, int h, int stride, string name)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Layer info length will be written as the whole section by caller.
        // Structure: Layer count | Layer records | Channel data | Global mask

        // Layer count = 1
        WriteU16(bw, 1);

        int channelBytes = 2 + w * h; // compression + raw plane

        // Layer record
        WriteU32(bw, 0); // top
        WriteU32(bw, 0); // left
        WriteU32(bw, (uint)h); // bottom
        WriteU32(bw, (uint)w); // right
        WriteU16(bw, 3); // channels
        // R=0 G=1 B=2
        for (short ch = 0; ch < 3; ch++)
        {
            WriteI16(bw, ch);
            WriteU32(bw, (uint)channelBytes);
        }
        WriteAscii(bw, "8BIM");
        WriteAscii(bw, "norm");
        bw.Write((byte)255); // opacity
        bw.Write((byte)0);   // clipping
        bw.Write((byte)0);   // flags (visible)
        bw.Write((byte)0);   // filler

        // Extra data: mask(0) + blending(0) + name
        using var extra = new MemoryStream();
        using (var eb = new BinaryWriter(extra, Encoding.ASCII, leaveOpen: true))
        {
            WriteU32(eb, 0);
            WriteU32(eb, 0);
            var nb = Encoding.ASCII.GetBytes(name.Length > 255 ? name[..255] : name);
            eb.Write((byte)nb.Length);
            eb.Write(nb);
            int pad = (4 - ((1 + nb.Length) % 4)) % 4;
            for (int i = 0; i < pad; i++) eb.Write((byte)0);
        }
        var ex = extra.ToArray();
        WriteU32(bw, (uint)ex.Length);
        bw.Write(ex);

        // Channel image data: R, G, B planes
        WriteChannelPlane(bw, bgra, w, h, stride, 2); // R from BGRA
        WriteChannelPlane(bw, bgra, w, h, stride, 1); // G
        WriteChannelPlane(bw, bgra, w, h, stride, 0); // B

        // Global layer mask info
        WriteU32(bw, 0);

        var bytes = ms.ToArray();
        if ((bytes.Length & 1) != 0)
        {
            Array.Resize(ref bytes, bytes.Length + 1);
        }
        return bytes;
    }

    private static void WriteChannelPlane(BinaryWriter bw, byte[] bgra, int w, int h, int stride, int c)
    {
        WriteU16(bw, 0); // raw
        for (int y = 0; y < h; y++)
        {
            int row = y * stride;
            for (int x = 0; x < w; x++)
                bw.Write(bgra[row + x * 4 + c]);
        }
    }

    private static void WritePlanarRgb(BinaryWriter bw, byte[] bgra, int w, int h, int stride)
    {
        // R plane then G then B
        for (int c = 2; c >= 0; c--)
        {
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                    bw.Write(bgra[row + x * 4 + c]);
            }
        }
    }

    private static void WriteAscii(BinaryWriter bw, string s) => bw.Write(Encoding.ASCII.GetBytes(s));
    private static void WriteU16(BinaryWriter bw, ushort v)
    {
        bw.Write((byte)(v >> 8));
        bw.Write((byte)v);
    }
    private static void WriteI16(BinaryWriter bw, short v) => WriteU16(bw, unchecked((ushort)v));
    private static void WriteU32(BinaryWriter bw, uint v)
    {
        bw.Write((byte)(v >> 24));
        bw.Write((byte)(v >> 16));
        bw.Write((byte)(v >> 8));
        bw.Write((byte)v);
    }
}
