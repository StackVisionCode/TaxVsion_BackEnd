using System.Diagnostics;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.Options;

namespace TaxVision.FinancialInsights.Infrastructure.Processing;

/// <summary>Extracts text locally. Only pages whose OCR remains unreadable retain image bytes.</summary>
public sealed class LocalStatementExtractor(IOptions<FinancialProcessingOptions> options)
{
    public async Task<IReadOnlyList<ExtractedPage>> ExtractAsync(byte[] bytes, string mediaType, CancellationToken ct)
    {
        var settings = options.Value;
        if (bytes.Length is 0 or > 25 * 1024 * 1024)
            throw new DocumentProcessingException("FileSize", "File exceeds the processing size limit.");
        IReadOnlyList<ExtractedPage> pages = mediaType.ToLowerInvariant() switch
        {
            "text/csv" or "application/csv" => [new ExtractedPage(1, DecodeText(bytes))],
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ExtractWorkbook(bytes, settings.MaxTextCharacters),
            "application/pdf" => await ExtractPdfAsync(bytes, settings, ct),
            "image/png" or "image/jpeg" => [await ExtractImageAsync(bytes, mediaType, ct)],
            _ => throw new DocumentProcessingException("FileType", "Unsupported statement format.")
        };
        if (pages.Count == 0 || pages.Count > settings.MaxPages || pages.Sum(x => x.Text.Length) > settings.MaxTextCharacters)
            throw new DocumentProcessingException("DocumentLimit", "Statement exceeds page or text limits.");
        if (pages.Count(x => x.ImageBytes is not null) > settings.MaxVisionPages)
            throw new DocumentProcessingException("VisionPageLimit", "Too many pages require image review.");
        if (pages.All(x => string.IsNullOrWhiteSpace(x.Text) && x.ImageBytes is null))
            throw new DocumentProcessingException("NoReadableContent", "No text or readable page image was found.");
        return pages;
    }

    private static string DecodeText(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    private static IReadOnlyList<ExtractedPage> ExtractWorkbook(byte[] bytes, int maxChars)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var book = new XLWorkbook(stream);
            var text = new StringBuilder();
            foreach (var sheet in book.Worksheets)
            {
                text.AppendLine($"Sheet: {sheet.Name}");
                foreach (var row in sheet.RowsUsed())
                {
                    text.AppendLine(string.Join('\t', row.CellsUsed().Select(cell => cell.GetFormattedString())));
                    if (text.Length > maxChars)
                        throw new DocumentProcessingException("DocumentLimit", "Spreadsheet exceeds text limit.");
                }
            }
            return [new ExtractedPage(1, text.ToString())];
        }
        catch (DocumentProcessingException) { throw; }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or NotSupportedException)
        { throw new DocumentProcessingException("InvalidSpreadsheet", "Spreadsheet could not be parsed."); }
    }

    private static async Task<ExtractedPage> ExtractImageAsync(byte[] bytes, string mediaType, CancellationToken ct)
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, mediaType == "image/png" ? "page.png" : "page.jpg");
            await File.WriteAllBytesAsync(path, bytes, ct);
            var (text, confidence) = await RunOcrAsync(path, ct);
            return IsReadable(text) && confidence >= 70
                ? new ExtractedPage(1, text) : new ExtractedPage(1, text, bytes, mediaType);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<IReadOnlyList<ExtractedPage>> ExtractPdfAsync(byte[] bytes,
        FinancialProcessingOptions settings, CancellationToken ct)
    {
        var directory = CreateTempDirectory();
        try
        {
            var pdf = Path.Combine(directory, "statement.pdf");
            await File.WriteAllBytesAsync(pdf, bytes, ct);
            var info = await RunAsync("pdfinfo", [pdf], 16_000, ct);
            var pageLine = info.Split('\n').FirstOrDefault(x => x.StartsWith("Pages:", StringComparison.OrdinalIgnoreCase));
            if (pageLine is null || !int.TryParse(pageLine[6..].Trim(), out var count) || count is < 1 or > 1000)
                throw new DocumentProcessingException("InvalidPdf", "PDF page count could not be read.");
            if (count > settings.MaxPages)
                throw new DocumentProcessingException("DocumentLimit", "PDF has too many pages.");
            var raw = await RunAsync("pdftotext", ["-layout", "-f", "1", "-l", count.ToString(), pdf, "-"],
                settings.MaxTextCharacters + 5_000, ct);
            var extracted = raw.Split('\f');
            var pages = new List<ExtractedPage>(count);
            for (var index = 1; index <= count; index++)
            {
                var text = index <= extracted.Length ? extracted[index - 1].Trim() : string.Empty;
                if (IsReadable(text)) { pages.Add(new ExtractedPage(index, text)); continue; }
                var prefix = Path.Combine(directory, $"page-{index}");
                await RunAsync("pdftoppm", ["-f", index.ToString(), "-l", index.ToString(),
                    "-singlefile", "-scale-to", "1800", "-png", pdf, prefix], 4_000, ct);
                var imagePath = prefix + ".png";
                var (ocr, confidence) = await RunOcrAsync(imagePath, ct);
                if (IsReadable(ocr) && confidence >= 70) pages.Add(new ExtractedPage(index, ocr));
                else
                {
                    var image = await File.ReadAllBytesAsync(imagePath, ct);
                    if (image.Length > 4 * 1024 * 1024)
                        throw new DocumentProcessingException("VisionPageLimit", "Rendered page is too large.");
                    pages.Add(new ExtractedPage(index, ocr, image, "image/png"));
                }
            }
            return pages;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static bool IsReadable(string text) => text.Count(char.IsLetterOrDigit) >= 80
        && text.Count(ch => ch == '\uFFFD') < 3;

    private static async Task<(string Text, double Confidence)> RunOcrAsync(string imagePath, CancellationToken ct)
    {
        var tsv = await RunAsync("tesseract", [imagePath, "stdout", "-l", "eng", "tsv"], 100_000, ct);
        var text = new StringBuilder();
        double weightedConfidence = 0;
        var characters = 0;
        foreach (var line in tsv.Split('\n').Skip(1))
        {
            var cells = line.TrimEnd('\r').Split('\t', 12);
            if (cells.Length != 12 || string.IsNullOrWhiteSpace(cells[11])
                || !double.TryParse(cells[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence)
                || confidence < 0) continue;
            text.Append(cells[11]).Append(' ');
            var weight = cells[11].Length;
            weightedConfidence += confidence * weight;
            characters += weight;
        }
        return (text.ToString(), characters == 0 ? 0 : weightedConfidence / characters);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "taxvision-financial", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> RunAsync(string executable, IReadOnlyList<string> arguments,
        int maxCharacters, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        using var process = new Process();
        var started = false;
        process.StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            started = process.Start();
            var stdout = ReadLimitedAsync(process.StandardOutput, maxCharacters, timeout.Token);
            var stderr = ReadLimitedAsync(process.StandardError, 4_000, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            _ = await stderr; // Do not log OCR output or document paths.
            if (process.ExitCode != 0)
                throw new DocumentProcessingException("LocalOcrFailed", $"{executable} could not process the document.");
            return output;
        }
        catch (System.ComponentModel.Win32Exception)
        { throw new DocumentProcessingException("LocalOcrUnavailable", $"{executable} is not installed."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new DocumentProcessingException("LocalOcrTimeout", $"{executable} timed out."); }
        finally { if (started && !process.HasExited) process.Kill(entireProcessTree: true); }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, int maxCharacters, CancellationToken ct)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer, ct);
            if (read == 0) return text.ToString();
            text.Append(buffer, 0, read);
            if (text.Length > maxCharacters)
                throw new DocumentProcessingException("DocumentLimit", "Local extraction exceeded output limit.");
        }
    }
}
