namespace TaxVision.Templates.Renderer;

/// <summary>
/// Resultado propio del paquete — deliberadamente NO usa BuildingBlocks.Results.Result&lt;T&gt;: este
/// paquete se publica como .nupkg standalone (Fase Scribe 7, plan §36) y no debe depender de ningún
/// otro proyecto del repo para seguir siendo consumible fuera de él.
/// </summary>
public sealed record FluidRenderResult(bool IsSuccess, string? Value, string? ErrorMessage)
{
    public static FluidRenderResult Success(string value) => new(true, value, null);

    public static FluidRenderResult Failure(string errorMessage) => new(false, null, errorMessage);
}
