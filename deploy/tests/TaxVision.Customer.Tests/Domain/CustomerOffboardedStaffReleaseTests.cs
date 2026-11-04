using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Domain;

/// <summary>
/// Soltar a un empleado retirado funciona en cualquier estado del cliente (también archivado, donde el resto
/// de las mutaciones están vedadas) y nunca asigna a nadie: solo quita su fila y la denormalización.
/// </summary>
public sealed class CustomerOffboardedStaffReleaseTests
{
    private static readonly Guid Leaver = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void Releasing_the_primary_of_an_archived_customer_clears_the_preparer()
    {
        var customer = NewCustomer();
        customer.AssignPreparer(Leaver, Actor);
        customer.GrantAccess(Other, Actor);
        customer.Archive(Actor);

        var result = customer.ReleaseOffboardedStaff(Leaver, Actor);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Null(customer.AssignedPreparerUserId);
        Assert.Equal(Other, Assert.Single(customer.Assignments).UserId);
        Assert.Equal(CustomerStatus.Archived, customer.Status);
    }

    [Fact]
    public void Releasing_an_extra_access_keeps_the_primary()
    {
        var customer = NewCustomer();
        customer.AssignPreparer(Other, Actor);
        customer.GrantAccess(Leaver, Actor);
        customer.Archive(Actor);

        var result = customer.ReleaseOffboardedStaff(Leaver, Actor);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        Assert.Equal(Other, customer.AssignedPreparerUserId);
        Assert.DoesNotContain(customer.Assignments, a => a.UserId == Leaver);
    }

    [Fact]
    public void Releasing_someone_not_assigned_fails_and_changes_nothing()
    {
        var customer = NewCustomer();
        customer.AssignPreparer(Other, Actor);

        var result = customer.ReleaseOffboardedStaff(Leaver, Actor);

        Assert.Equal("Customer.NotAssigned", result.Error.Code);
        Assert.Equal(Other, customer.AssignedPreparerUserId);
        Assert.Single(customer.Assignments);
    }

    private static DomainCustomer NewCustomer() =>
        DomainCustomer
            .Register(
                Guid.NewGuid(),
                CustomerKind.Individual,
                PersonalName.Create("Ada", "Lovelace").Value,
                null,
                EmailAddress.Create("ada@example.com").Value,
                null,
                Language.En,
                Guid.NewGuid()
            )
            .Value;
}
