using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Customers.DomainEvents;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.Events;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// D-3 en los contratos: un cliente sin email se anuncia con el email nulo, y el aviso de cambio lleva el estado
/// para que el consumidor que crea la fila tarde (cuando llega el email) no la deje activa por error (E-3).
/// </summary>
public sealed class CustomerOptionalEmailEventsTests
{
    private static readonly Guid ByUser = Guid.NewGuid();

    [Fact]
    public async Task A_client_without_email_is_announced_with_a_null_email()
    {
        var customer = WithoutEmail();
        var created = Assert.Single(customer.DomainEvents.OfType<CustomerCreatedDomainEvent>());

        var messages = await CustomerCreatedDomainEventHandler.Handle(
            created,
            new CustomerCommandEventCountTests.AllCustomers([customer]),
            new FixedCorrelation(),
            CancellationToken.None
        );

        var evt = Assert.Single(messages.OfType<CustomerCreatedIntegrationEvent>());
        Assert.Null(evt.PrimaryEmail);
        Assert.Equal("+13055551234", evt.PrimaryPhone);
    }

    [Fact]
    public void The_update_notice_carries_the_email_added_later_and_the_status()
    {
        var customer = WithoutEmail();
        customer.Deactivate(ByUser);

        var withoutEmail = CustomerUpdatedEvents.From(customer, ByUser, "corr");
        Assert.Null(withoutEmail.PrimaryEmail);
        Assert.Equal("Inactive", withoutEmail.Status);

        customer.ChangePrimaryEmail(EmailAddress.Create("late@example.com").Value, ByUser);
        Assert.Equal("late@example.com", CustomerUpdatedEvents.From(customer, ByUser, "corr").PrimaryEmail);
    }

    private static DomainCustomer WithoutEmail() =>
        DomainCustomer
            .Register(
                Guid.NewGuid(),
                CustomerKind.Individual,
                PersonalName.Create("Ada", "Lovelace").Value,
                null,
                null,
                PhoneNumber.Create("+13055551234").Value,
                Language.En,
                ByUser
            )
            .Value;

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public string CorrelationId => "corr";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Nothing();

        private sealed class Nothing : IDisposable
        {
            public void Dispose() { }
        }
    }
}
