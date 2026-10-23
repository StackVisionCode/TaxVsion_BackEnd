using BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;
using TaxVision.Customer.Application.Customers.FromForms;

namespace TaxVision.Customer.Tests.Architecture;

public sealed class ClientFormCustomerCreationFitnessTests
{
    [Fact]
    public void Every_executable_create_target_in_the_catalog_has_a_handler()
    {
        var executable = CustomerDataCollectionTargets
            .All.Where(t =>
                t.Status != DataCollectionTargetStatus.Retired
                && t.Operations.Contains(DataCollectionOperation.CreateCustomer)
            )
            .Select(t => t.TargetKey);

        Assert.All(executable, key => Assert.Contains(key, CustomerFormTargetMapper.SupportedTargetKeys));
    }

    [Fact]
    public void No_handler_exists_for_a_target_the_create_operation_does_not_declare()
    {
        var declared = CustomerDataCollectionTargets
            .All.Where(t => t.Operations.Contains(DataCollectionOperation.CreateCustomer))
            .Select(t => t.TargetKey)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(CustomerFormTargetMapper.SupportedTargetKeys, key => Assert.Contains(key, declared));
    }

    [Fact]
    public void The_consumer_creates_inline_and_never_through_the_bus()
    {
        var source = ConsumerSource();

        Assert.DoesNotContain("InvokeAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SendAsync", source, StringComparison.Ordinal);
        Assert.Contains("CreateCustomerHandler.Handle(", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("first.last+tag@sub.example.org")]
    public void No_lock_name_contains_the_email_in_clear(string email)
    {
        var resource = CustomerCreationLockResource.For(Guid.NewGuid(), email);

        Assert.DoesNotContain(email, resource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(email.Split('@')[0], resource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(email.Split('@')[1], resource, StringComparison.OrdinalIgnoreCase);
        Assert.True(resource.Length <= 255);
    }

    [Fact]
    public void The_lock_name_is_stable_per_office_and_email()
    {
        var tenant = Guid.NewGuid();

        Assert.Equal(
            CustomerCreationLockResource.For(tenant, "ada@example.com"),
            CustomerCreationLockResource.For(tenant, "ada@example.com")
        );
        Assert.NotEqual(
            CustomerCreationLockResource.For(tenant, "ada@example.com"),
            CustomerCreationLockResource.For(Guid.NewGuid(), "ada@example.com")
        );
    }

    [Fact]
    public void Customer_listens_to_its_command_queue_and_routes_the_results_to_the_fanout()
    {
        var program = File.ReadAllText(
            Path.Combine(Root(), "src", "Services", "Customer", "TaxVision.Customer.Api", "Program.cs")
        );

        Assert.Contains(
            "ListenToRabbitQueue(\"customer-client-form-commands\").UseDurableInbox()",
            program,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "PublishMessage<CustomerCreationFromFormCompletedIntegrationEvent>().ToRabbitExchange(\"taxvision-events\")",
            program,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "PublishMessage<CustomerCreationFromFormRejectedIntegrationEvent>().ToRabbitExchange(\"taxvision-events\")",
            program,
            StringComparison.Ordinal
        );
    }

    private static string ConsumerSource() =>
        File.ReadAllText(
            Path.Combine(
                Root(),
                "src",
                "Services",
                "Customer",
                "TaxVision.Customer.Application",
                "Customers",
                "Consumers",
                "CustomerCreationFromFormRequestedConsumer.cs"
            )
        );

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
