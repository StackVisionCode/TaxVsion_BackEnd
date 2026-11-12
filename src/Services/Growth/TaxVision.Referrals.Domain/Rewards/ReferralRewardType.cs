namespace TaxVision.Referrals.Domain.Rewards;

/// <summary>
/// Tipo de premio al REFERIDOR. Los tres primeros son beneficios comerciales materializados
/// por Subscription (heredados del diseño original; no representan dinero). Solo
/// <see cref="WalletCredit"/> es monetario: un porcentaje del plan pagado por el referido,
/// acreditado como saldo en el Wallet del tenant del referidor (el Wallet no conoce
/// "referidos": recibe una referencia opaca vía <c>internal/wallet/credits</c>).
/// </summary>
public enum ReferralRewardType
{
    SubscriptionFeatureGrant,
    SubscriptionTrialExtension,
    FutureSubscriptionDiscount,
    WalletCredit,
}
