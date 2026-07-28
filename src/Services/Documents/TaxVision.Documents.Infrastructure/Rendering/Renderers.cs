using BuildingBlocks.Results;
using Fluid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using TaxVision.Documents.Application.Abstractions;

namespace TaxVision.Documents.Infrastructure.Rendering;

/// <summary>
/// Renderiza el HTML final con Fluid (Liquid) a partir de los datos recibidos. Para el primer slice la
/// plantilla se resuelve del catálogo embebido (billing.invoice.v1); las plantillas en BD versionada
/// (DocumentTemplateVersions) son de una fase posterior — la firma no cambiará.
///
/// Fluid corre sin acceso a red ni FS (el parser solo interpreta el texto); los datos entran como
/// IDictionary&lt;string,object&gt; (acceso por clave, sin reflexión sobre tipos CLR del dominio).
/// </summary>
public sealed class TemplateDocumentRenderer(ILogger<TemplateDocumentRenderer> logger) : IDocumentTemplateRenderer
{
    private static readonly FluidParser Parser = new();

    public async Task<Result<string>> RenderHtmlAsync(
        string templateKey,
        int templateVersion,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> data,
        CancellationToken ct = default
    )
    {
        _ = tenantId;

        if (!EmbeddedDocumentTemplates.TryGet(templateKey, templateVersion, out var source))
            return Result.Failure<string>(
                new Error("Documents.Template.NotFound", $"Template '{templateKey}' v{templateVersion} was not found.")
            );

        if (!Parser.TryParse(source, out var template, out var parseError))
        {
            logger.LogError("Template {TemplateKey} v{Version} failed to parse: {Error}.", templateKey, templateVersion, parseError);
            return Result.Failure<string>(new Error("Documents.Template.ParseError", "The template could not be parsed."));
        }

        try
        {
            var context = new TemplateContext(new TemplateOptions());
            foreach (var (key, value) in data)
                context.SetValue(key, value);

            var html = await template.RenderAsync(context);
            ct.ThrowIfCancellationRequested();
            return Result.Success(html);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Template {TemplateKey} v{Version} failed to render.", templateKey, templateVersion);
            return Result.Failure<string>(new Error("Documents.Template.RenderError", "The template could not be rendered."));
        }
    }
}

/// <summary>
/// Motor HTML→PDF con Chromium headless (Microsoft.Playwright, ADR-004). Un único navegador reutilizado
/// (arranque perezoso, thread-safe) + un bulkhead de concurrencia (SemaphoreSlim): la conversión PDF
/// consume memoria y NUNCA corre con concurrencia ilimitada. Una página nueva por documento, cerrada
/// siempre. El contenedor trae Chromium (imagen playwright/dotnet); en local requiere `playwright install`.
/// </summary>
public sealed class PlaywrightHtmlToPdfConverter : IHtmlToPdfConverter, IAsyncDisposable
{
    private readonly SemaphoreSlim _concurrency;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly ILogger<PlaywrightHtmlToPdfConverter> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightHtmlToPdfConverter(IOptions<DocumentsPdfOptions> options, ILogger<PlaywrightHtmlToPdfConverter> logger)
    {
        var max = Math.Max(1, options.Value.MaxConcurrency);
        _concurrency = new SemaphoreSlim(max, max);
        _logger = logger;
    }

    public async Task<Result<byte[]>> ConvertAsync(string html, CancellationToken ct = default)
    {
        await _concurrency.WaitAsync(ct);
        try
        {
            var browser = await GetBrowserAsync(ct);
            var page = await browser.NewPageAsync();
            try
            {
                await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.NetworkIdle });
                var bytes = await page.PdfAsync(
                    new PagePdfOptions
                    {
                        Format = "A4",
                        PrintBackground = true,
                        Margin = new Margin
                        {
                            Top = "12mm",
                            Bottom = "12mm",
                            Left = "10mm",
                            Right = "10mm",
                        },
                    }
                );
                return Result.Success(bytes);
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Chromium PDF conversion failed.");
            return Result.Failure<byte[]>(new Error("Documents.Pdf.ConversionFailed", "HTML to PDF conversion failed."));
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken ct)
    {
        if (_browser is not null)
            return _browser;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_browser is null)
            {
                _playwright = await Playwright.CreateAsync();
                _browser = await _playwright.Chromium.LaunchAsync(
                    new BrowserTypeLaunchOptions { Headless = true, Args = ["--no-sandbox", "--disable-dev-shm-usage"] }
                );
            }

            return _browser;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
        _playwright?.Dispose();
        _concurrency.Dispose();
        _initLock.Dispose();
    }
}

/// <summary>Límites del motor PDF. MaxConcurrency es el bulkhead de conversiones Chromium simultáneas.</summary>
public sealed class DocumentsPdfOptions
{
    public const string SectionName = "Documents:Pdf";

    public int MaxConcurrency { get; set; } = 2;
}
