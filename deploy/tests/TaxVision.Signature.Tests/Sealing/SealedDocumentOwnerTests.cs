using TaxVision.Signature.Application.Sealing;

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>
/// Fase 2 — SealedDocumentOwner: un sellado pertenece al cliente firmante cuando hay
/// exactamente uno mapeado; con 0 o varios cae al dueno de firma (sin regresion).
/// </summary>
public sealed class SealedDocumentOwnerTests
{
    [Fact]
    public void Single_mapped_customer_owns_the_document()
    {
        var customerId = Guid.NewGuid();
        var fallback = Guid.NewGuid();

        var (ownerType, ownerId) = SealedDocumentOwner.Resolve([customerId, null], fallback);

        Assert.Equal("Customer", ownerType);
        Assert.Equal(customerId, ownerId);
    }

    [Fact]
    public void Duplicate_of_the_same_customer_still_resolves_to_that_customer()
    {
        var customerId = Guid.NewGuid();
        var fallback = Guid.NewGuid();

        var (ownerType, ownerId) = SealedDocumentOwner.Resolve([customerId, customerId], fallback);

        Assert.Equal("Customer", ownerType);
        Assert.Equal(customerId, ownerId);
    }

    [Fact]
    public void No_mapped_customer_falls_back_to_signature_owner()
    {
        var fallback = Guid.NewGuid();

        var (ownerType, ownerId) = SealedDocumentOwner.Resolve([null, null], fallback);

        Assert.Equal("Signature", ownerType);
        Assert.Equal(fallback, ownerId);
    }

    [Fact]
    public void Multiple_distinct_customers_fall_back_to_signature_owner()
    {
        var fallback = Guid.NewGuid();

        var (ownerType, ownerId) = SealedDocumentOwner.Resolve([Guid.NewGuid(), Guid.NewGuid()], fallback);

        Assert.Equal("Signature", ownerType);
        Assert.Equal(fallback, ownerId);
    }
}
