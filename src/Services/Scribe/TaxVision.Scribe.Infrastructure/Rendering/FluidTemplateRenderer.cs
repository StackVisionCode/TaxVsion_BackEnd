using System.Diagnostics;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Fluid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Application.EventMappings;
using TaxVision.Scribe.Application.Layouts;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Application.Templates;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Infrastructure.Observability;

namespace TaxVision.Scribe.Infrastructure.Rendering;

/// <summary>
/// Motor de render: resuelve EventKey→TemplateKey, carga la version Published + su layout pinned,
/// y renderiza con Fluid (Liquid sandboxed). Cache de 2 niveles para evitar el round-trip a
/// CloudStorage en cada envío: L1 (MemoryCache, in-proc, guarda el <see cref="IFluidTemplate"/> ya
/// parseado) y L2 (Redis, guarda el texto fuente crudo — ver <see cref="ITemplateSourceCache"/>).
/// El layout SÍ se parsea y renderiza con Fluid (a diferencia de la primera versión de Fase 4): la
/// Fase 4.6 reveló que el layout necesita variables propias (current_year, tenant_name,
/// tenant_logo_missing) además de {{ body }}, así que un replace por regex no alcanza. El body ya
/// renderizado se inyecta como variable <c>body</c> y el layout debe imprimirlo con
/// <c>{{ body | raw }}</c> (filtro Liquid estándar) para no doble-escaparlo — el resto de las
/// variables del layout SÍ se auto-escapan normalmente.
/// </summary>
public sealed partial class FluidTemplateRenderer(
    EventTemplateResolver eventTemplateResolver,
    IEmailTemplateRepository templateRepository,
    IEmailLayoutRepository layoutRepository,
    ICloudStorageClient cloudStorageClient,
    ILogoResolver logoResolver,
    IMemoryCache l1Cache,
    ITemplateSourceCache l2Cache,
    ILogger<FluidTemplateRenderer> logger
) : IEmailRenderer
{
    private static readonly FluidParser Parser = new();
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(5);
    private static readonly TemplateOptions Options = new() { MaxRecursion = 100, MaxSteps = 10000 };

    public async Task<Result<RenderedContent>> RenderAsync(RenderRequest request, CancellationToken ct = default)
    {
        var templateKey = await eventTemplateResolver.ResolveAsync(
            request.EventKey,
            request.TenantId,
            request.Locale,
            ct
        );
        if (templateKey is null)
            return Result.Failure<RenderedContent>(
                new Error("EmailRenderer.NoMapping", $"No template is mapped for event '{request.EventKey.Value}'.")
            );

        var templateResult = await templateRepository.GetByKeyAsync(templateKey, request.TenantId, ct);
        if (templateResult.IsFailure)
            return Result.Failure<RenderedContent>(templateResult.Error);

        var version = templateResult.Value.Versions.FirstOrDefault(v => v.Status == EmailVersionStatus.Published);
        if (version is null)
            return Result.Failure<RenderedContent>(
                new Error(
                    "EmailRenderer.NoPublishedVersion",
                    $"Template '{templateKey.Value}' has no published version."
                )
            );

        var layoutResult = await layoutRepository.GetByIdAsync(version.LayoutId, ct);
        if (layoutResult.IsFailure)
            return Result.Failure<RenderedContent>(layoutResult.Error);

        var layoutVersion = layoutResult.Value.Versions.FirstOrDefault(v =>
            v.VersionNumber == version.LayoutVersionNumber
        );
        if (layoutVersion is null)
            return Result.Failure<RenderedContent>(
                new Error(
                    "EmailRenderer.LayoutVersionNotFound",
                    $"Layout version {version.LayoutVersionNumber} was not found for layout '{layoutResult.Value.LayoutKey.Value}'."
                )
            );

        logger.LogDebug(
            "Rendering event {EventKey} as template {TemplateKey} v{VersionNumber}.",
            request.EventKey.Value,
            templateKey.Value,
            version.VersionNumber
        );

        return await RenderVersionAsync(
            templateKey.Value,
            templateResult.Value.Scope.ToString(),
            version,
            layoutResult.Value.LayoutKey.Value,
            layoutVersion,
            templateResult.Value.TenantId,
            request.Locale?.Value,
            request.LogoScope,
            request.Variables,
            ct
        );
    }

    public async Task<Result<RenderedContent>> PreviewAsync(
        Guid versionId,
        IReadOnlyDictionary<string, object?> sampleVariables,
        CancellationToken ct = default
    )
    {
        var versionResult = await templateRepository.GetVersionByIdAsync(versionId, ct);
        if (versionResult.IsFailure)
            return Result.Failure<RenderedContent>(versionResult.Error);
        var (template, version) = versionResult.Value;

        var layoutResult = await layoutRepository.GetByIdAsync(version.LayoutId, ct);
        if (layoutResult.IsFailure)
            return Result.Failure<RenderedContent>(layoutResult.Error);

        var layoutVersion = layoutResult.Value.Versions.FirstOrDefault(v =>
            v.VersionNumber == version.LayoutVersionNumber
        );
        if (layoutVersion is null)
            return Result.Failure<RenderedContent>(
                new Error(
                    "EmailRenderer.LayoutVersionNotFound",
                    $"Layout version {version.LayoutVersionNumber} was not found for layout '{layoutResult.Value.LayoutKey.Value}'."
                )
            );

        var logoScope = template.TenantId is null ? LogoScope.System : LogoScope.Tenant;

        return await RenderVersionAsync(
            template.TemplateKey.Value,
            template.Scope.ToString(),
            version,
            layoutResult.Value.LayoutKey.Value,
            layoutVersion,
            template.TenantId,
            locale: null,
            logoScope,
            sampleVariables,
            ct
        );
    }

    /// <summary>
    /// Sube a caché (L1 parseado + L2 fuente) el body/text/layout de una versión Published sin
    /// renderizar nada — usado por <c>TemplateWarmupService</c> al arranque (Fase 6) para que el
    /// primer envío real de cada template no pague el round-trip a CloudStorage.
    /// </summary>
    public async Task<Result> WarmupAsync(
        EmailTemplateVersion version,
        string templateKeyValue,
        EmailLayoutVersion layoutVersion,
        string layoutKeyValue,
        Guid? tenantId,
        CancellationToken ct = default
    )
    {
        var tenantIdForToken = tenantId ?? PlatformTenant.Id;

        var bodyResult = await GetOrParseTemplateAsync(
            BuildCacheKey(tenantId, templateKeyValue, version.VersionNumber, "html"),
            version.HtmlFileId,
            tenantIdForToken,
            ct
        );
        if (bodyResult.IsFailure)
            return Result.Failure(bodyResult.Error);

        if (version.TextFileId is { } textFileId)
        {
            var textResult = await GetOrParseTemplateAsync(
                BuildCacheKey(tenantId, templateKeyValue, version.VersionNumber, "text"),
                textFileId,
                tenantIdForToken,
                ct
            );
            if (textResult.IsFailure)
                return Result.Failure(textResult.Error);
        }

        var layoutResult = await GetOrParseLayoutTemplateAsync(
            BuildCacheKey(tenantId, layoutKeyValue, layoutVersion.VersionNumber, "layout"),
            layoutVersion.HtmlFileId,
            tenantIdForToken,
            ct
        );
        return layoutResult.IsFailure ? Result.Failure(layoutResult.Error) : Result.Success();
    }

    private async Task<Result<RenderedContent>> RenderVersionAsync(
        string templateKeyValue,
        string scope,
        EmailTemplateVersion version,
        string layoutKeyValue,
        EmailLayoutVersion layoutVersion,
        Guid? tenantId,
        string? locale,
        LogoScope logoScope,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct
    )
    {
        using var activity = ScribeTelemetry.ActivitySource.StartActivity("scribe.render");
        activity?.SetTag("template_key", templateKeyValue);
        activity?.SetTag("scope", scope);
        activity?.SetTag("tenant", tenantId?.ToString() ?? "system");

        var stopwatch = Stopwatch.StartNew();
        var worstCacheLayer = CacheLayer.L1;
        try
        {
            var result = await RenderVersionCoreAsync(
                templateKeyValue,
                version,
                layoutKeyValue,
                layoutVersion,
                tenantId,
                locale,
                logoScope,
                variables,
                layer => worstCacheLayer = WorstOf(worstCacheLayer, layer),
                ct
            );
            activity?.SetTag("otel.status_code", result.IsSuccess ? "OK" : "ERROR");
            if (result.IsFailure)
                activity?.SetTag("error.message", result.Error.Message);
            return result;
        }
        finally
        {
            stopwatch.Stop();
            ScribeTelemetry.RecordRenderRequest(
                templateKeyValue,
                scope,
                CacheLayerName(worstCacheLayer),
                tenantId?.ToString() ?? "system"
            );
            ScribeTelemetry.RecordRenderDuration(templateKeyValue, stopwatch.Elapsed.TotalSeconds);
        }
    }

    private async Task<Result<RenderedContent>> RenderVersionCoreAsync(
        string templateKeyValue,
        EmailTemplateVersion version,
        string layoutKeyValue,
        EmailLayoutVersion layoutVersion,
        Guid? tenantId,
        string? locale,
        LogoScope logoScope,
        IReadOnlyDictionary<string, object?> variables,
        Action<CacheLayer> reportCacheLayer,
        CancellationToken ct
    )
    {
        var tenantIdForToken = tenantId ?? PlatformTenant.Id;

        if (!Parser.TryParse(version.Subject, out var subjectTemplate, out var subjectParseError))
            return Result.Failure<RenderedContent>(new Error("EmailRenderer.Parse", subjectParseError));
        var subjectResult = await RenderTemplateAsync(subjectTemplate, variables, htmlEncode: false, ct);
        if (subjectResult.IsFailure)
            return Result.Failure<RenderedContent>(subjectResult.Error);

        var bodyTemplateResult = await GetOrParseTemplateAsync(
            BuildCacheKey(tenantId, templateKeyValue, version.VersionNumber, "html"),
            version.HtmlFileId,
            tenantIdForToken,
            ct
        );
        if (bodyTemplateResult.IsFailure)
            return Result.Failure<RenderedContent>(bodyTemplateResult.Error);
        reportCacheLayer(bodyTemplateResult.Value.CacheLayer);

        var bodyHtmlResult = await RenderTemplateAsync(
            bodyTemplateResult.Value.Template,
            variables,
            htmlEncode: true,
            ct
        );
        if (bodyHtmlResult.IsFailure)
            return Result.Failure<RenderedContent>(bodyHtmlResult.Error);

        var logoAsset = await logoResolver.ResolveAsync(logoScope, tenantId, ct);

        var layoutTemplateResult = await GetOrParseLayoutTemplateAsync(
            BuildCacheKey(tenantId, layoutKeyValue, layoutVersion.VersionNumber, "layout"),
            layoutVersion.HtmlFileId,
            tenantIdForToken,
            ct
        );
        if (layoutTemplateResult.IsFailure)
            return Result.Failure<RenderedContent>(layoutTemplateResult.Error);
        reportCacheLayer(layoutTemplateResult.Value.CacheLayer);

        var layoutVariables = BuildLayoutVariables(
            variables,
            bodyHtmlResult.Value,
            logoAsset.IsFallback,
            subjectResult.Value,
            locale
        );
        var finalHtmlResult = await RenderTemplateAsync(
            layoutTemplateResult.Value.Template,
            layoutVariables,
            htmlEncode: true,
            ct
        );
        if (finalHtmlResult.IsFailure)
            return Result.Failure<RenderedContent>(finalHtmlResult.Error);

        var textResult = await ResolveTextAsync(
            version.TextFileId,
            tenantId,
            templateKeyValue,
            version.VersionNumber,
            tenantIdForToken,
            variables,
            finalHtmlResult.Value,
            reportCacheLayer,
            ct
        );
        if (textResult.IsFailure)
            return Result.Failure<RenderedContent>(textResult.Error);

        // Guid.Empty = LogoResolver no encontró un SystemAssetRef sembrado todavía (arranque en
        // frío antes de que ScribeSystemAssetSeeder termine, o seed nunca corrido). No referenciar
        // un FileId inexistente — el envío tiene que seguir funcionando sin logo, no romperse.
        var inlineAssets = new List<InlineAsset>();
        if (logoAsset.CloudStorageFileId != Guid.Empty)
            inlineAssets.Add(
                new("logo-header", logoAsset.CloudStorageFileId, logoAsset.ContentType, logoAsset.SizeBytes)
            );

        return Result.Success(
            new RenderedContent(subjectResult.Value, finalHtmlResult.Value, textResult.Value, inlineAssets)
        );
    }

    /// <summary>
    /// El layout recibe las variables del caller más las reservadas que documenta
    /// Scribe_Email_Style_Guide.md §9 — el renderer las calcula, el invocador no las declara: body (el
    /// HTML ya renderizado del template, debe imprimirse con {{ body | raw }}), subject, locale,
    /// tenant_logo_missing (bool, gobierna el banner de aviso vía {% if %}) y current_year. Si el
    /// caller manda alguna de esas claves igual se pisa.
    /// </summary>
    private static Dictionary<string, object?> BuildLayoutVariables(
        IReadOnlyDictionary<string, object?> requestVariables,
        string bodyHtml,
        bool tenantLogoMissing,
        string subject,
        string? locale
    )
    {
        var merged = new Dictionary<string, object?>(requestVariables, StringComparer.OrdinalIgnoreCase)
        {
            ["body"] = bodyHtml,
            ["subject"] = subject,
            ["locale"] = locale ?? string.Empty,
            ["tenant_logo_missing"] = tenantLogoMissing,
            ["current_year"] = DateTime.UtcNow.Year,
        };
        return merged;
    }

    private async Task<Result<string?>> ResolveTextAsync(
        Guid? textFileId,
        Guid? tenantId,
        string templateKeyValue,
        int versionNumber,
        Guid tenantIdForToken,
        IReadOnlyDictionary<string, object?> variables,
        string finalHtml,
        Action<CacheLayer> reportCacheLayer,
        CancellationToken ct
    )
    {
        if (textFileId is null)
            return Result.Success<string?>(HtmlToText(finalHtml));

        var textTemplateResult = await GetOrParseTemplateAsync(
            BuildCacheKey(tenantId, templateKeyValue, versionNumber, "text"),
            textFileId.Value,
            tenantIdForToken,
            ct
        );
        if (textTemplateResult.IsFailure)
            return Result.Failure<string?>(textTemplateResult.Error);
        reportCacheLayer(textTemplateResult.Value.CacheLayer);

        var textResult = await RenderTemplateAsync(textTemplateResult.Value.Template, variables, htmlEncode: false, ct);
        return textResult.IsFailure
            ? Result.Failure<string?>(textResult.Error)
            : Result.Success<string?>(textResult.Value);
    }

    private async Task<Result<(string Source, CacheLayer CacheLayer)>> GetOrFetchSourceAsync(
        string cacheKey,
        Guid fileId,
        Guid tenantIdForToken,
        CancellationToken ct
    )
    {
        var cached = await l2Cache.GetAsync(cacheKey, ct);
        if (cached is not null)
            return Result.Success((cached, CacheLayer.L2));

        var downloaded = await cloudStorageClient.DownloadTextAsync(fileId, tenantIdForToken, ct);
        if (downloaded.IsFailure)
            return Result.Failure<(string, CacheLayer)>(downloaded.Error);

        await l2Cache.SetAsync(cacheKey, downloaded.Value, ct);
        return Result.Success((downloaded.Value, CacheLayer.Miss));
    }

    private async Task<Result<(IFluidTemplate Template, CacheLayer CacheLayer)>> GetOrParseTemplateAsync(
        string cacheKey,
        Guid fileId,
        Guid tenantIdForToken,
        CancellationToken ct
    )
    {
        if (l1Cache.TryGetValue<IFluidTemplate>(cacheKey, out var cachedTemplate) && cachedTemplate is not null)
            return Result.Success((cachedTemplate, CacheLayer.L1));

        var sourceResult = await GetOrFetchSourceAsync(cacheKey, fileId, tenantIdForToken, ct);
        if (sourceResult.IsFailure)
            return Result.Failure<(IFluidTemplate, CacheLayer)>(sourceResult.Error);

        if (!Parser.TryParse(sourceResult.Value.Source, out var template, out var parseError))
            return Result.Failure<(IFluidTemplate, CacheLayer)>(new Error("EmailRenderer.Parse", parseError));

        l1Cache.Set(cacheKey, template, new MemoryCacheEntryOptions { Size = 1 });
        return Result.Success((template, sourceResult.Value.CacheLayer));
    }

    /// <summary>
    /// Igual que GetOrParseTemplateAsync pero valida que el HTML fuente traiga {{ body }} (con o sin
    /// | raw) antes de parsear — si un layout se publicara sin ese placeholder, el body simplemente
    /// desaparecería en silencio al renderizar; esta validación falla ruidosamente en el primer intento
    /// (cache miss) y en cada reintento posterior, ya que un layout roto nunca llega a cachearse.
    /// </summary>
    private async Task<Result<(IFluidTemplate Template, CacheLayer CacheLayer)>> GetOrParseLayoutTemplateAsync(
        string cacheKey,
        Guid fileId,
        Guid tenantIdForToken,
        CancellationToken ct
    )
    {
        if (l1Cache.TryGetValue<IFluidTemplate>(cacheKey, out var cachedTemplate) && cachedTemplate is not null)
            return Result.Success((cachedTemplate, CacheLayer.L1));

        var sourceResult = await GetOrFetchSourceAsync(cacheKey, fileId, tenantIdForToken, ct);
        if (sourceResult.IsFailure)
            return Result.Failure<(IFluidTemplate, CacheLayer)>(sourceResult.Error);

        if (!BodyPlaceholder().IsMatch(sourceResult.Value.Source))
            return Result.Failure<(IFluidTemplate, CacheLayer)>(
                new Error("EmailLayout.Body", "Layout HTML must contain a {{ body }} placeholder.")
            );

        if (!Parser.TryParse(sourceResult.Value.Source, out var template, out var parseError))
            return Result.Failure<(IFluidTemplate, CacheLayer)>(new Error("EmailRenderer.Parse", parseError));

        l1Cache.Set(cacheKey, template, new MemoryCacheEntryOptions { Size = 1 });
        return Result.Success((template, sourceResult.Value.CacheLayer));
    }

    /// <summary>Nivel de caché que sirvió un artefacto — para <c>scribe_render_requests_total{cache_layer}</c> (Fase 6). Orden = de más a menos rápido.</summary>
    private enum CacheLayer
    {
        L1,
        L2,
        Miss,
    }

    private static CacheLayer WorstOf(CacheLayer a, CacheLayer b) => (CacheLayer)Math.Max((int)a, (int)b);

    private static string CacheLayerName(CacheLayer layer) =>
        layer switch
        {
            CacheLayer.L1 => "l1",
            CacheLayer.L2 => "l2",
            _ => "miss",
        };

    private static async Task<Result<string>> RenderTemplateAsync(
        IFluidTemplate template,
        IReadOnlyDictionary<string, object?> variables,
        bool htmlEncode,
        CancellationToken ct
    )
    {
        var context = new TemplateContext(Options);
        foreach (var (key, value) in variables)
            context.SetValue(key, value ?? string.Empty);

        try
        {
            var renderTask = (
                htmlEncode ? template.RenderAsync(context, HtmlEncoder.Default) : template.RenderAsync(context)
            ).AsTask();
            var rendered = await renderTask.WaitAsync(RenderTimeout, ct);
            return Result.Success(rendered);
        }
        catch (TimeoutException)
        {
            return Result.Failure<string>(
                new Error("EmailRenderer.Timeout", "Template rendering exceeded the time budget.")
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Failure<string>(new Error("EmailRenderer.Render", ex.Message));
        }
    }

    private static string HtmlToText(string html) =>
        WebUtility.HtmlDecode(WhitespaceRegex().Replace(TagRegex().Replace(html, " "), " ")).Trim();

    private static string BuildCacheKey(Guid? tenantId, string identifier, int versionNumber, string kind) =>
        $"scribe:{tenantId?.ToString() ?? "system"}:{identifier}:{versionNumber}:{kind}";

    [GeneratedRegex(@"\{\{\s*body(\s*\|[^}]*)?\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex BodyPlaceholder();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
