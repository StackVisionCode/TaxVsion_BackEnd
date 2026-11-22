namespace TaxVision.Auth.Application.Abstractions;

/// <summary>Sesiones vivas sin ningún refresh vigente: el barrido de vencidas las cierra.</summary>
public interface IExpiredSessionQuery
{
    Task<IReadOnlyList<Guid>> FindExpiredSessionIdsAsync(DateTime now, int take, CancellationToken ct = default);
}
