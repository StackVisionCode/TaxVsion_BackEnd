namespace TaxVision.Growth.SharedKernel.Buyers;

/// <summary>Calcula el comprador de un email normalizado (HMAC con el pepper de Growth).</summary>
public interface IBuyerIdentityHasher
{
    BuyerIdentity Hash(NormalizedEmail email);
}

/// <summary>Correos desechables: no pueden usar un beneficio (cupón o premio), aunque sí registrarse.</summary>
public interface IDisposableEmailDomains
{
    bool IsDisposable(NormalizedEmail email);
}
