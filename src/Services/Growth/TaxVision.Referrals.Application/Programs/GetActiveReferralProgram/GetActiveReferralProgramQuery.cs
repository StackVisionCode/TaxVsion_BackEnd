using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Programs.GetActiveReferralProgram;

/// <summary>
/// Términos del programa vigente, para mostrar a la oficina ("gana el 10 % del plan, máx. $50, se
/// acredita tras 30 días"). No recibe ningún Id del cliente: el programa vigente es el único activo de plataforma.
/// </summary>
public sealed record GetActiveReferralProgramQuery;

public sealed record ReferralProgramTermsResult(
    Guid ProgramId,
    string Name,
    int PolicyVersion,
    ReferralRewardType RewardType,
    int RewardBasisPoints,
    long? RewardCapCents,
    string? RewardCurrency,
    int WaitingPeriodDays,
    int MaximumRewardsPerReferrerPerCalendarYear,
    int AttributionWindowDays
);
