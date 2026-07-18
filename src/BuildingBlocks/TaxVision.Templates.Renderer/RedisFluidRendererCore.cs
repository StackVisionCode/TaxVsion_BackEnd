using System.Text.Encodings.Web;
using Fluid;
using StackExchange.Redis;

namespace TaxVision.Templates.Renderer;

/// <summary>
/// Lee el texto fuente Liquid crudo desde el Redis compartido (mismo Redis/misma clave que la L2 de
/// Scribe — <c>ITemplateSourceCache</c>, que guarda texto, no AST: Fluid.Ast no está pensado para
/// serialización binaria) y lo parsea+renderiza localmente. Sin caché propia — cada llamada
/// re-parsea, que es barato; el costo real que evita es CloudStorage/SQL, no disponibles acá.
/// </summary>
public sealed class RedisFluidRendererCore(IConnectionMultiplexer redis) : IFluidRendererCore
{
    private static readonly FluidParser Parser = new();
    private static readonly TemplateOptions Options = new() { MaxRecursion = 100, MaxSteps = 10000 };
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(5);

    public async Task<FluidRenderResult> RenderAsync(
        string redisKey,
        IReadOnlyDictionary<string, object?> variables,
        bool htmlEncode,
        CancellationToken ct = default
    )
    {
        var source = await redis.GetDatabase().StringGetAsync(redisKey);
        if (source.IsNullOrEmpty)
            return FluidRenderResult.Failure($"No cached template source found for key '{redisKey}'.");

        if (!Parser.TryParse(source!, out var template, out var parseError))
            return FluidRenderResult.Failure(parseError);

        var context = new TemplateContext(Options);
        foreach (var (key, value) in variables)
            context.SetValue(key, value ?? string.Empty);

        try
        {
            var renderTask = (
                htmlEncode ? template.RenderAsync(context, HtmlEncoder.Default) : template.RenderAsync(context)
            ).AsTask();
            var rendered = await renderTask.WaitAsync(RenderTimeout, ct);
            return FluidRenderResult.Success(rendered);
        }
        catch (TimeoutException)
        {
            return FluidRenderResult.Failure("Template rendering exceeded the time budget.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FluidRenderResult.Failure(ex.Message);
        }
    }
}
