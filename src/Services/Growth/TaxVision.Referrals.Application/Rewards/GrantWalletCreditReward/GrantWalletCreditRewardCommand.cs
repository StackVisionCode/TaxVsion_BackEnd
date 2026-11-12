namespace TaxVision.Referrals.Application.Rewards.GrantWalletCreditReward;

/// <summary>
/// PIDE al Wallet acreditar al referidor un premio <c>WalletCredit</c> ya elegible. Lo dispara el worker de premios, por
/// caso y con el tenant del referidor como contexto. El resultado llega por evento
/// (<see cref="ConfirmWalletCreditRewardCommand"/>). <paramref name="ActorUserId"/> es el actor de sistema.
/// </summary>
public sealed record GrantWalletCreditRewardCommand(Guid TenantId, Guid RewardCaseId, Guid ActorUserId);

/// <summary>El Wallet respondió que acreditó el premio (evento <c>wallet.credit.applied.v1</c> exitoso).</summary>
public sealed record ConfirmWalletCreditRewardCommand(Guid TenantId, Guid RewardCaseId, Guid LedgerEntryId, Guid ActorUserId);

/// <summary>Resultado de pedir el crédito: qué pasó y, si hay que hablar con el Wallet, la solicitud a publicar.</summary>
public sealed record GrantWalletCreditRewardResult(
    GrantWalletCreditRewardOutcome Outcome,
    TaxVision.Referrals.Application.Abstractions.WalletCreditRequest? Request
);

public enum GrantWalletCreditRewardOutcome
{
    /// <summary>La solicitud de crédito se publicó al Wallet; el caso queda <c>PendingGrant</c> hasta su respuesta.</summary>
    Requested,

    /// <summary>El caso ya estaba <c>Granted</c>/<c>Vested</c>: reintento sin efecto.</summary>
    AlreadyGranted,
}

public enum ConfirmWalletCreditRewardOutcome
{
    /// <summary>Se confirmó: caso <c>Granted</c> y beneficio registrado.</summary>
    Granted,

    /// <summary>Ya estaba confirmado (reentrega del evento): sin efecto.</summary>
    AlreadyGranted,
}
