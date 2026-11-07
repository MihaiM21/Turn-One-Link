using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Turn_One_Link.Services;

/// <summary>
/// LF-18 "Report a problem": one zip with what support needs — app/OS info, the debug log, settings,
/// the recent-sessions list and, only if the user agrees, the newest recorded laps. Never the sign-in
/// state (authstate.bin) or the per-user overrides file.
/// </summary>
public static class DiagnosticsBundle
{
    /// <summary>Newest session folders from the outbox included when telemetry is allowed.</summary>
    public const int MaxSessions = 2;

    /// <summary>Only the end of the debug log goes in; that's where the problem is.</summary>
    public const int MaxLogBytes = 4 * 1024 * 1024;

    private const string LogFile = "turnone_debug.log";
    private static readonly string[] DataFiles = { "settings.json", "sessions.json" };

    // Things the log may hold that must never leave the PC: JWTs, bearer headers, the LAN PIN.
    private static readonly Regex[] Secrets =
    {
        new(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*", RegexOptions.Compiled),
        new(@"(?i)(bearer\s+)[^\s""']+", RegexOptions.Compiled),
        new(@"(?i)(pin\s*[:=]\s*)\d+", RegexOptions.Compiled),
    };

    /// <summary>Masks secrets in a log line or text block.</summary>
    public static string Redact(string text)
    {
        foreach (var re in Secrets)
            text = re.Replace(text, m => m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value + "[redacted]" : "[redacted]");
        return text;
    }

    /// <summary>Writes the zip into <paramref name="targetDir"/> and returns its path.</summary>
    public static string Create(string dataDir, string targetDir, bool includeTelemetry, string? extraInfo = null)
    {
        Directory.CreateDirectory(targetDir);
        var path = Path.Combine(targetDir, $"TurnOneLink-report-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        var info = zip.CreateEntry("diagnostics.txt");
        using (var writer = new StreamWriter(info.Open(), new UTF8Encoding(false)))
        {
            writer.WriteLine($"Turn One Link {AppInfo.Version}");
            writer.WriteLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            writer.WriteLine($".NET: {RuntimeInformation.FrameworkDescription}");
            writer.WriteLine($"Created: {DateTimeOffset.Now:O}");
            writer.WriteLine($"Telemetry included: {(includeTelemetry ? "yes" : "no")}");
            if (!string.IsNullOrWhiteSpace(extraInfo))
            {
                writer.WriteLine();
                writer.WriteLine(extraInfo);
            }
        }

        var log = Path.Combine(dataDir, LogFile);
        if (File.Exists(log)) AddLog(zip, log);

        foreach (var name in DataFiles)
        {
            var file = Path.Combine(dataDir, name);
            if (File.Exists(file)) AddFile(zip, file, name);
        }

        if (includeTelemetry)
        {
            var outbox = Path.Combine(dataDir, "outbox");
            if (Directory.Exists(outbox))
            {
                var newest = Directory.GetDirectories(outbox)
                    .OrderByDescending(Directory.GetLastWriteTimeUtc)
                    .Take(MaxSessions);
                foreach (var session in newest)
                {
                    foreach (var file in Directory.GetFiles(session, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(dataDir, file).Replace('\\', '/');
                        AddFile(zip, file, relative);
                    }
                }
            }
        }

        return path;
    }

    private static void AddLog(ZipArchive zip, string file)
    {
        try
        {
            using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var truncated = source.Length > MaxLogBytes;
            if (truncated) source.Seek(-MaxLogBytes, SeekOrigin.End);
            using var reader = new StreamReader(source, Encoding.UTF8);
            if (truncated) reader.ReadLine(); // drop the partial first line
            var text = Redact(reader.ReadToEnd());

            using var writer = new StreamWriter(zip.CreateEntry(LogFile, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            if (truncated) writer.WriteLine($"[log truncated to the last {MaxLogBytes / (1024 * 1024)} MB]");
            writer.Write(text);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void AddFile(ZipArchive zip, string file, string entryName)
    {
        try
        {
            // Shared read: the app may still be writing the log.
            using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = zip.CreateEntry(entryName, CompressionLevel.Optimal).Open();
            source.CopyTo(target);
        }
        catch (IOException) { /* skip a file we can't read rather than fail the whole report */ }
        catch (UnauthorizedAccessException) { }
    }
}
