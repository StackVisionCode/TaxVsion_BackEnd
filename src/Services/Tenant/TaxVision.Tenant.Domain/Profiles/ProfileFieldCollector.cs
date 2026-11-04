using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.Profiles;

/// <summary>
/// Junta los campos validados. Estricto (edición): recuerda el primer error y nada se aplica. Tolerante
/// (importación de datos viejos): descarta solo el campo inválido y anota por qué.
/// </summary>
internal sealed class ProfileFieldCollector
{
    private readonly List<Error>? _dropped;

    private ProfileFieldCollector(List<Error>? dropped) => _dropped = dropped;

    public Error? FirstError { get; private set; }

    public bool IsLenient => _dropped is not null;

    public static ProfileFieldCollector Strict() => new(null);

    public static ProfileFieldCollector Lenient(List<Error> dropped) => new(dropped);

    public T Take<T>(Result<T> result, T fallback)
    {
        if (result.IsSuccess)
            return result.Value;

        Drop(result.Error);
        return fallback;
    }

    public void Drop(Error error)
    {
        if (_dropped is not null)
            _dropped.Add(error);
        else
            FirstError ??= error;
    }
}
