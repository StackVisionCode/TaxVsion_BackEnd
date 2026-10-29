using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Domain;

public sealed class CustomerOriginTests
{
    [Fact]
    public void The_origin_is_recorded_once_and_never_rewritten()
    {
        var customer = NewCustomer();
        var first = CustomerOrigin.Create("tasks.client_forms", Guid.NewGuid()).Value;
        var second = CustomerOrigin.Create("tasks.client_forms", Guid.NewGuid()).Value;

        Assert.True(customer.RecordOrigin(first).IsSuccess);
        var again = customer.RecordOrigin(second);

        Assert.Equal("Customer.OriginAlreadyRecorded", again.Error.Code);
        Assert.Equal(first, customer.Origin);
    }

    [Fact]
    public void A_customer_created_by_hand_has_no_origin()
    {
        Assert.Null(NewCustomer().Origin);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("tasks.client_forms", true)]
    public void The_origin_requires_a_source_service_and_a_reference(string sourceService, bool withReference)
    {
        var result = CustomerOrigin.Create(sourceService, withReference ? Guid.Empty : Guid.NewGuid());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void The_source_service_cannot_exceed_its_column()
    {
        var result = CustomerOrigin.Create(new string('s', CustomerOrigin.SourceServiceMaxLength + 1), Guid.NewGuid());

        Assert.Equal("CustomerOrigin.SourceService", result.Error.Code);
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
