using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Eidolon.Core;
using Eidolon.IO;

namespace Eidolon.App;

public enum ExportFormat
{
    Png,
    Jpeg,
    Webp,
    Bmp,
    Psd
}

public static class ImageExport
{
    public static void Export(Document doc, string path, ExportFormat format, AppSettings settings)
    {
        bool wantAlpha = settings.ExportPreserveTransparency
                         && format is ExportFormat.Png or ExportFormat.Webp or ExportFormat.Psd;

        switch (format)
        {
            case ExportFormat.Png:
                EidolonFileStore.ExportPng(doc, path, wantAlpha);
                return;
            case ExportFormat.Psd:
            {
                int w = doc.Width, h = doc.Height, stride = w * 4;
                var bgra = Composite(doc, wantAlpha);
                PsdWriter.Write(doc, path, bgra, stride);
                return;
            }
            case ExportFormat.Jpeg:
            {
                var bgra = Composite(doc, withAlpha: false);
                WriteWpf(bgra, doc.Width, doc.Height, doc.Width * 4, path,
                    new JpegBitmapEncoder
                    {
                        QualityLevel = settings.JpegCompress ? Math.Clamp(settings.JpegQuality, 1, 100) : 100
                    });
                return;
            }
            case ExportFormat.Bmp:
            {
                var bgra = Composite(doc, withAlpha: false);
                WriteWpf(bgra, doc.Width, doc.Height, doc.Width * 4, path, new BmpBitmapEncoder());
                return;
            }
            case ExportFormat.Webp:
            {
                var bgra = Composite(doc, wantAlpha);
                WriteWebpViaFfmpeg(bgra, doc.Width, doc.Height, doc.Width * 4, path, settings, wantAlpha);
                return;
            }
            default:
                throw new NotSupportedException(format.ToString());
        }
    }

    public static ExportFormat DetectFormat(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => ExportFormat.Png,
            ".jpg" or ".jpeg" => ExportFormat.Jpeg,
            ".webp" => ExportFormat.Webp,
            ".bmp" => ExportFormat.Bmp,
            ".psd" => ExportFormat.Psd,
            _ => ExportFormat.Png
        };
    }

    private static byte[] Composite(Document doc, bool withAlpha)
    {
        int w = doc.Width, h = doc.Height, stride = w * 4;
        var bgra = new byte[stride * h];
        var old = doc.Background.Kind;
        if (!withAlpha && doc.Background.Kind == DocumentBackgroundKind.Transparent)
            doc.Background.Kind = DocumentBackgroundKind.White;
        Compositor.CompositeToBgra(doc, bgra, stride);
        doc.Background.Kind = old;
        return bgra;
    }

    private static void WriteWpf(byte[] bgra, int w, int h, int stride, string path, BitmapEncoder encoder)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, stride);
        bmp.Freeze();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static void WriteWebpViaFfmpeg(byte[] bgra, int w, int h, int stride, string path, AppSettings settings, bool alpha)
    {
        string? ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
            throw new InvalidOperationException("WebP export requires ffmpeg.exe (place next to Eidolon or in tools/).");

        string tmpPng = Path.Combine(Path.GetTempPath(), "eidolon_export_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            // reuse PNG encoder in IO
            WriteWpf(bgra, w, h, stride, tmpPng, new PngBitmapEncoder());
            string q = settings.WebpLossless
                ? "-lossless 1"
                : $"-quality {Math.Clamp(settings.WebpQuality, 1, 100)}";
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-y -hide_banner -loglevel error -i \"{tmpPng}\" -c:v libwebp {q} \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg");
            string err = proc.StandardError.ReadToEnd();
            proc.WaitForExit(120000);
            if (proc.ExitCode != 0 || !File.Exists(path))
                throw new InvalidOperationException("ffmpeg WebP failed: " + err);
        }
        finally
        {
            try { File.Delete(tmpPng); } catch { /* ignore */ }
        }
    }

    private static string? FindFfmpeg()
    {
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        foreach (var name in new[] { "ffmpeg.exe", "ffmpeg" })
        {
            string p = Path.Combine(appDir, name);
            if (File.Exists(p)) return p;
        }
        string tools = Path.Combine(Directory.GetCurrentDirectory(), "tools", "ffmpeg.exe");
        if (File.Exists(tools)) return tools;
        try
        {
            var psi = new ProcessStartInfo("ffmpeg", "-version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                proc.WaitForExit(3000);
                if (proc.ExitCode == 0) return "ffmpeg";
            }
        }
        catch { }
        return null;
    }
}
