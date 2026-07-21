namespace TaxVision.Referrals.Domain.Programs;

/// <summary>
/// Tipo de beneficio que recibe el REFERIDO (no el que refiere) al atribuirse con un
/// código. Deliberadamente propio de Referrals.Domain, sin referenciar
/// TaxVision.Codes.Domain.CodeBenefitType — Codes y Referrals no comparten FK ni tipos
/// de dominio; la traducción entre este enum y CodeBenefitType ocurre en Growth.Api
/// (ReferralsController), el único lugar que conoce ambos bounded contexts.
/// </summary>
public enum ReferralRefereeBenefitType
{
    Percentage,
    FixedAmount,
}
