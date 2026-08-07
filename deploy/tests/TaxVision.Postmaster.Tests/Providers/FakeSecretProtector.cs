using BuildingBlocks.Security;

namespace TaxVision.Postmaster.Tests.Providers;

/// <summary>Identidad (no cifra) — suficiente para probar el flujo de resolución sin depender de AES real.</summary>
internal sealed class FakeSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => plaintext;

    public bool TryUnprotect(string? protectedValue, out string plaintext, out SecretUnprotectFailure failure)
    {
        plaintext = protectedValue ?? string.Empty;
        failure = protectedValue is null ? SecretUnprotectFailure.NoValueStored : SecretUnprotectFailure.None;
        return protectedValue is not null;
    }
}
