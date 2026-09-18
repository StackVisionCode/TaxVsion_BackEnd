namespace TaxVision.Campaigns.Domain.Runs;

/// <summary>
/// Estado de una ejecución (<c>State_Machines.md §2</c>). Slice 2: materialización síncrona
/// (Created→Dispatching directo; <c>Materializing</c>/paginado y <c>Cancelling</c> son fases
/// posteriores).
/// </summary>
public enum CampaignRunStatus
{
    Created = 0,
    Dispatching = 1,
    Completed = 2,
    PartiallyFailed = 3,
    Failed = 4,
    Cancelled = 5,
    Rejected = 6,
}

/// <summary>
/// Estado de una unidad destinatario/canal (<c>State_Machines.md §3</c>). <c>Accepted</c> ≠
/// <c>Delivered</c>; <c>Unknown</c> = timeout reconciliable (no <c>Failed</c>).
/// </summary>
public enum DispatchState
{
    Pending = 0,
    Dispatched = 1,
    Accepted = 2,
    Delivered = 3,
    Failed = 4,
    Skipped = 5,
    Unknown = 6,
}

/// <summary>Resultado que reporta el ejecutor de canal en <c>campaign.dispatch.result.v1</c>.</summary>
public enum DispatchOutcome
{
    Accepted = 0,
    Delivered = 1,
    Failed = 2,
    Skipped = 3,
    Unknown = 4,
}
