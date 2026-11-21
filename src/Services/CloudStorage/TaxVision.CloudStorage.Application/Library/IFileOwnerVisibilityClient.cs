using BuildingBlocks.Contracts.Files;

namespace TaxVision.CloudStorage.Application.Library;

/// <summary>Servicio dueño de un origen por fila.</summary>
public enum FileOwnerService
{
    Tasks,
    Correspondence,
    Communication,
    Notes,
}

/// <param name="Available">false = el dueño no respondió: sus filas se omiten (falla cerrado).</param>
/// <param name="Visible">Archivos que quien mira ve en el módulo, por id.</param>
public sealed record OwnerVisibility(bool Available, IReadOnlyDictionary<Guid, VisibleFile> Visible)
{
    public static OwnerVisibility Unavailable { get; } = new(false, new Dictionary<Guid, VisibleFile>());
}

/// <param name="Available">false = Communication no respondió: sus orígenes se omiten en esta petición.</param>
public sealed record OwnerScope(bool Available, CommunicationFileScope Scope)
{
    public static OwnerScope Unavailable { get; } = new(false, Empty);

    public static CommunicationFileScope Empty => new([], [], [], []);
}

/// <summary>Pregunta al servicio dueño, con la identidad de quien mira, qué archivos ve.</summary>
public interface IFileOwnerVisibilityClient
{
    /// <summary>Conversaciones, llamadas y reuniones de quien mira; se pide una vez por búsqueda.</summary>
    Task<OwnerScope> GetCommunicationScopeAsync(CancellationToken ct);

    Task<OwnerVisibility> CheckAsync(
        FileOwnerService owner,
        IReadOnlyCollection<Guid> fileIds,
        string purpose,
        CancellationToken ct
    );
}
