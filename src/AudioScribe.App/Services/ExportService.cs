using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using TimeSpan = System.TimeSpan;
using AudioScribe.App.Models;

namespace AudioScribe.App.Services;

/// <summary>Referenzielle Implementierung: 4 Exporte + Paragraph-Merging (Lücke &lt; 3 s).</summary>
public sealed class ExportService : IExportService
{
    /// <summary>Aufeinanderfolgende Segmente desselben Sprechers werden zusammengefasst, wenn die Lücke darunter liegt.</summary>
    public const double MergeGapThresholdSeconds = 3.0;

    public IReadOnlyList<TranscriptBlock> MergeIntoBlocks(IEnumerable<TranscriptSegment> segments)
    {
        var blocks = new List<TranscriptBlock>();
        TranscriptBlock? current = null;

        foreach (var seg in segments.OrderBy(s => s.Start))
        {
            bool merge = current is not null
                && current.SpeakerId == seg.SpeakerId
                && seg.Start - current.End < MergeGapThresholdSeconds;
            if (!merge)
            {
                current = new TranscriptBlock
                {
                    SpeakerId = seg.SpeakerId,
                    SpeakerName = ResolveName(seg),
                    Start = seg.Start,
                    End = seg.End,
                };
                blocks.Add(current);
            }

            current!.End = Math.Max(current.End, seg.End);
            var text = seg.Text.Trim();
            if (text.Length > 0) current.Sentences.Add(text);
            current.SourceSegments.Add(seg);
        }

        foreach (var b in blocks)
            b.SpeakerName = ResolveName(b.SourceSegments[0]);

        return blocks;
    }

    private static string ResolveName(TranscriptSegment seg) =>
        string.IsNullOrWhiteSpace(seg.DisplaySpeakerName) ? seg.SpeakerId : seg.DisplaySpeakerName;

    public string BuildMarkdown(in ExportBundle bundle)
    {
        var sb = new StringBuilder();
        AppendYamlFrontmatter(sb, bundle);
        sb.AppendLine();
        sb.AppendLine($"# {bundle.Metadata.Title}");
        sb.AppendLine();

        foreach (var block in MergeIntoBlocks(bundle.Segments))
        {
            sb.AppendLine($"### [{FormatTimestamp(block.Start)}] {block.SpeakerName}");
            sb.AppendLine();
            sb.AppendLine(JoinSentences(block));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public string BuildText(in ExportBundle bundle)
    {
        var sb = new StringBuilder();
        sb.AppendLine(bundle.Metadata.Title);
        sb.AppendLine(new string('=', bundle.Metadata.Title.Length));
        sb.AppendLine();
        foreach (var block in MergeIntoBlocks(bundle.Segments))
        {
            sb.AppendLine($"[{FormatTimestamp(block.Start)}] {block.SpeakerName}: {JoinSentences(block)}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public string BuildSubRip(in ExportBundle bundle)
    {
        var sb = new StringBuilder();
        int i = 1;
        foreach (var seg in bundle.Segments.OrderBy(s => s.Start))
        {
            var text = seg.Text.Trim();
            if (text.Length == 0) continue;
            sb.AppendLine(i.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine($"{FormatSrtTime(seg.Start)} --> {FormatSrtTime(seg.End)}");
            sb.AppendLine($"{ResolveName(seg)}: {WrapSubtitle(text)}");
            sb.AppendLine();
            i++;
        }
        return sb.ToString();
    }

    public string BuildSessionJson(in ExportBundle bundle)
    {
        var payload = new
        {
            meta = new
            {
                title = bundle.Metadata.Title,
                source_file = Path.GetFileName(bundle.Metadata.SourceFile),
                created_at = bundle.Metadata.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                duration_seconds = Math.Round(bundle.Metadata.DurationSeconds, 2),
                device = bundle.Metadata.Device,
                model = bundle.Metadata.Model,
            },
            speakers = bundle.Speakers.Select(s => new
            {
                id = s.Id,
                display_name = s.ResolvedName,
                total_time = Math.Round(s.TotalTimeSeconds, 2),
                percent = Math.Round(s.PercentageOfTotal, 2),
                preview_file = Path.GetFileName(s.PreviewPath),
            }),
            segments = bundle.Segments.OrderBy(s => s.Start).Select(s => new
            {
                id = s.SegmentId,
                speaker_id = s.SpeakerId,
                display_name = ResolveName(s),
                start = Math.Round(s.Start, 3),
                end = Math.Round(s.End, 3),
                text = s.Text,
                avg_confidence = Math.Round(s.AverageConfidence, 4),
                words = s.Words.Select(w => new
                {
                    start = Math.Round(w.Start, 3),
                    end = Math.Round(w.End, 3),
                    word = w.Word,
                    score = Math.Round(w.Score, 3),
                }),
            }),
            merged_blocks = MergeIntoBlocks(bundle.Segments).Select(b => new
            {
                speaker_id = b.SpeakerId,
                display_name = b.SpeakerName,
                start = Math.Round(b.Start, 3),
                end = Math.Round(b.End, 3),
                text = JoinSentences(b),
            }),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    public void CopyMarkdownToClipboard(in ExportBundle bundle)
    {
        var md = BuildMarkdown(bundle);
        // Clipboard-Exceptions (CLIPRDR_STOLEN/ACCESS) abfedern, Application.Current für Unit-Tests optional.
        try
        {
            if (Application.Current is not null)
                Clipboard.SetText(md);
        }
        catch (InvalidOperationException) { }
    }

    public string SaveToFile(ExportFormat format, string filePath, in ExportBundle bundle)
    {
        var content = format switch
        {
            ExportFormat.Markdown => BuildMarkdown(bundle),
            ExportFormat.Text => BuildText(bundle),
            ExportFormat.SubRip => BuildSubRip(bundle),
            ExportFormat.Json => BuildSessionJson(bundle),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        // UTF-8 ohne BOM, atomares Schreiben (.tmp -> rename)
        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        File.Move(tmp, filePath, overwrite: true);
        return filePath;
    }

    private static void AppendYamlFrontmatter(StringBuilder sb, in ExportBundle bundle)
    {
        sb.AppendLine("---");
        sb.AppendLine($"title: \"{bundle.Metadata.Title.Replace("\"", "'")}\"");
        sb.AppendLine($"source: \"{Path.GetFileName(bundle.Metadata.SourceFile)}\"");
        sb.AppendLine($"date: {bundle.Metadata.CreatedAt:yyyy-MM-dd}");
        sb.AppendLine($"duration: \"{FormatDuration(bundle.Metadata.DurationSeconds)}\"");
        sb.AppendLine("participants:");
        foreach (var s in bundle.Speakers)
            sb.AppendLine($"  - {s.ResolvedName}");
        sb.AppendLine($"model: \"{bundle.Metadata.Model}\"");
        sb.AppendLine($"device: \"{bundle.Metadata.Device}\"");
        sb.AppendLine("engine: AudioScribe (whisperx + pyannote)");
        sb.AppendLine("---");
    }

    private static string JoinSentences(TranscriptBlock block) =>
        string.Join(" ", block.Sentences);

    private static string WrapSubtitle(string text)
    {
        if (text.Length <= 42) return text;
        var cut = text.LastIndexOf(' ', Math.Min(text.Length, 42));
        if (cut <= 0) cut = 42;
        return text[..cut] + "\n" + text[cut..].TrimStart();
    }

    public static string FormatTimestamp(double seconds)
    {
        var t = Ticks(seconds);
        long totalHours = t / TickPerHour;
        long minutes = t / TickPerMinute % 60;
        long secs = t / TickPerSecond % 60;
        return $"{totalHours:00}:{minutes:00}:{secs:00}";
    }

    private static string FormatSrtTime(double seconds)
    {
        var t = Ticks(seconds);
        long millis = t / TickPerMilli % 1000;
        long wholeSeconds = t / TickPerSecond;
        return $"{FormatTimestamp(wholeSeconds)},{millis:000}";
    }

    private static string FormatDuration(double seconds)
    {
        var t = Ticks(seconds);
        long totalHours = t / TickPerHour;
        long minutes = t / TickPerMinute % 60;
        long secs = t / TickPerSecond % 60;
        return totalHours >= 1 ? $"{totalHours}h {minutes:00}m" : $"{minutes}m {secs:00}s";
    }

    private const long TickPerMilli = 10_000L;
    private const long TickPerSecond = 10_000_000L;
    private const long TickPerMinute = 600_000_000L;
    private const long TickPerHour = 36_000_000_000L;

    private static long Ticks(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var scaled = seconds * (double)TickPerSecond;
        return (long)(scaled + 0.5);
    }
}
