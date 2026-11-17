namespace TaxVision.Growth.SharedKernel.Buyers;

/// <summary>
/// Un comprador (una persona) para los límites de cupones y referidos: un Guid derivado del HMAC de su email normalizado.
/// Solo lo crea <see cref="IBuyerIdentityHasher"/>; nunca contiene el email. La versión sube si cambian las reglas de
/// normalización: los ids viejos siguen contando hasta que vencen sus códigos.
/// </summary>
public sealed record BuyerIdentity
{
    public const int CurrentVersion = 1;

    internal BuyerIdentity(int version, Guid id)
    {
        Version = version;
        Id = id;
    }

    public int Version { get; }
    public Guid Id { get; }

    /// <summary>Id con su versión, tal como se guarda en la cotización y en los contadores.</summary>
    public string SubjectId => $"v{Version}:{Id:N}";
}
