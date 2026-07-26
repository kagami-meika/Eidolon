using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Eidolon.App.Logging;
using Eidolon.Core;

namespace Eidolon.App.Controls;

public sealed class TimelapseRecorder
{
    public bool IsRecording { get; private set; }
    public bool IsEncoding { get; private set; }
    public int FrameCount { get; private set; }
    public string OutputDir { get; private set; } = "";
    public string FileName { get; private set; } = "";
    public int Fps { get; private set; } = 30;
    public string? LastVideoPath { get; private set; }
    public bool FfmpegFound { get; private set; }
    public string LastError { get; private set; } = "";

    private string _framesDir = "";
    private Document? _doc;

    public void Start(Document doc, string outputDir, string fileName, int fps = 30)
    {
        _doc = doc;
        OutputDir = outputDir;
        FileName = fileName;
        Fps = Math.Clamp(fps, 1, 60);
        FrameCount = 0;
        LastVideoPath = null;
        LastError = "";
        FfmpegFound = false;
        IsEncoding = false;

        _framesDir = Path.Combine(outputDir, ".frames_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_framesDir);
        IsRecording = true;
        AppLog.Info($"Timelapse start dir={outputDir} name={fileName} fps={Fps}", "Timelapse");
    }

    public void BindDocument(Document doc) => _doc = doc;

    public void CaptureFrame(Document? doc = null)
    {
        var d = doc ?? _doc;
        if (!IsRecording || d is null) return;
        FrameCount++;

        int w = d.Width, h = d.Height;
        int stride = w * 4;
        var bgra = new byte[stride * h];
        Compositor.CompositeToBgra(d, bgra, stride);

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, stride);
        bmp.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));

        string path = Path.Combine(_framesDir, $"frame_{FrameCount:D6}.png");
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    /// <summary>Stop recording and encode asynchronously. Returns final path (video or frames dir).</summary>
    public async Task<string> StopAsync()
    {
        if (!IsRecording && !IsEncoding) return LastVideoPath ?? "";
        IsRecording = false;

        if (FrameCount == 0)
        {
            try { if (Directory.Exists(_framesDir)) Directory.Delete(_framesDir, true); } catch { }
            LastError = "No frames captured";
            AppLog.Info("Timelapse stop: no frames", "Timelapse");
            return "";
        }

        string videoPath = Path.Combine(OutputDir, FileName);
        if (!videoPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            videoPath += ".mp4";

        IsEncoding = true;
        try
        {
            bool encoded = await Task.Run(() => TryEncodeFfmpeg(videoPath)).ConfigureAwait(false);

            if (encoded && File.Exists(videoPath))
            {
                LastVideoPath = videoPath;
                try { Directory.Delete(_framesDir, true); } catch { }
                AppLog.Info($"Timelapse encoded -> {videoPath} frames={FrameCount}", "Timelapse");
                return videoPath;
            }

            string dirName = Path.GetFileNameWithoutExtension(FileName) + "_frames";
            string finalFramesDir = Path.Combine(OutputDir, dirName);
            if (Directory.Exists(finalFramesDir))
                finalFramesDir = Path.Combine(OutputDir, dirName + "_" + Guid.NewGuid().ToString("N"));
            try { Directory.Move(_framesDir, finalFramesDir); } catch { finalFramesDir = _framesDir; }
            LastVideoPath = finalFramesDir;
            AppLog.Info($"Timelapse frames kept -> {finalFramesDir} err={LastError}", "Timelapse");
            return finalFramesDir;
        }
        finally
        {
            IsEncoding = false;
        }
    }

    private bool TryEncodeFfmpeg(string videoPath)
    {
        string? ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
        {
            FfmpegFound = false;
            LastError = "ffmpeg not found";
            return false;
        }
        FfmpegFound = true;

        // libx264 requires even dimensions
        string pattern = Path.Combine(_framesDir, "frame_%06d.png");
        string args =
            $"-y -hide_banner -loglevel error " +
            $"-framerate {Fps} -i \"{pattern}\" " +
            $"-vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" " +
            $"-c:v libx264 -pix_fmt yuv420p -preset ultrafast -crf 18 " +
            $"\"{videoPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = _framesDir
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                LastError = "Failed to start ffmpeg";
                return false;
            }

            var stderr = new StringBuilder();
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            proc.BeginErrorReadLine();
            proc.BeginOutputReadLine();

            // Wait up to 10 minutes for large sequences
            if (!proc.WaitForExit(600_000))
            {
                try { proc.Kill(true); } catch { }
                LastError = "ffmpeg timed out";
                return false;
            }

            // Drain async readers
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                LastError = $"ffmpeg exit={proc.ExitCode}: {stderr}";
                AppLog.Error(LastError, "Timelapse");
                return false;
            }

            return File.Exists(videoPath) && new FileInfo(videoPath).Length > 0;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AppLog.Error(ex, "ffmpeg encode", "Timelapse");
            return false;
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

        string tools = Path.GetFullPath(Path.Combine(appDir, "..", "..", "..", "..", "..", "tools", "ffmpeg.exe"));
        if (File.Exists(tools)) return tools;
        string tools2 = Path.Combine(Directory.GetCurrentDirectory(), "tools", "ffmpeg.exe");
        if (File.Exists(tools2)) return tools2;

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
                proc.WaitForExit(5000);
                if (proc.ExitCode == 0) return "ffmpeg";
            }
        }
        catch { }

        string[] candidates =
        {
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe"
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return null;
    }
}
