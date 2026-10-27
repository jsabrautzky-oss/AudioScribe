using AudioScribe.App.Models;

namespace AudioScribe.App.Services;

/// <summary>Export-Formate inkl. Paragraph-Merging (gleicher Sprecher, Lücke &lt; 3 s).</summary>
public enum ExportFormat
{
    Markdown,
    Text,
    SubRip,
    Json,
}

/// <summary>Metadaten für YAML-Frontmatter / JSON-Kopf.</summary>
public sealed record TranscriptMetadata(
    string Title,
    string SourceFile,
    DateTimeOffset CreatedAt,
    double DurationSeconds,
    string Device,
    string Model);

public readonly record struct ExportBundle(
    IReadOnlyList<TranscriptSegment> Segments,
    IReadOnlyList<SpeakerModel> Speakers,
    TranscriptMetadata Metadata);

/// <summary>Erzeugt Agent-Ready-Ausgaben: Markdown, TXT, SRT und volles Session-JSON.</summary>
public interface IExportService
{
    /// <summary>Aufeinanderfolgende Segmente desselben Sprechers mit Lücke &lt; 3 s zu Blöcken zusammenfassen.</summary>
    IReadOnlyList<TranscriptBlock> MergeIntoBlocks(IEnumerable<TranscriptSegment> segments);

    string BuildMarkdown(in ExportBundle bundle);
    string BuildText(in ExportBundle bundle);
    string BuildSubRip(in ExportBundle bundle);
    string BuildSessionJson(in ExportBundle bundle);

    /// <summary>Markdown (merged) in die Zwischenablage ("Kopieren für LLM-Agent").</summary>
    void CopyMarkdownToClipboard(in ExportBundle bundle);

    /// <summary>Export in den angegebenen Dateipfad; gibt geschriebenen Pfad zurück.</summary>
    string SaveToFile(ExportFormat format, string filePath, in ExportBundle bundle);
}

/// <summary>Ein zu einem Absatz zusammengefasster Redebeitrag eines Sprechers.</summary>
public sealed class TranscriptBlock
{
    public string SpeakerId { get; init; } = string.Empty;
    public string SpeakerName { get; set; } = string.Empty;
    public double Start { get; set; }
    public double End { get; set; }
    public List<string> Sentences { get; init; } = [];
    public List<TranscriptSegment> SourceSegments { get; init; } = [];
}
