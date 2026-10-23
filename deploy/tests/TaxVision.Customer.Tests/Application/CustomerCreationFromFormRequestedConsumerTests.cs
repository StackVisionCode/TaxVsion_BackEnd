using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using Outcomes = BuildingBlocks.Messaging.ClientFormsIntegrationEvents.CustomerCreationFromFormOutcomes;

namespace TaxVision.Customer.Tests.Application;

public sealed class CustomerCreationFromFormRequestedConsumerTests
{
    [Fact]
    public async Task Creates_the_customer_records_its_origin_and_publishes_completed()
    {
        var harness = new CustomerFormCreationHarness();
        var command = harness.Command();

        await harness.SendAsync(command);

        var customer = Assert.Single(harness.Repository.All);
        Assert.Equal("Ada Lovelace", customer.DisplayName);
        Assert.False(customer.ContactPolicy.SmsEnabled);
        Assert.Equal(new DateOnly(1985, 12, 10), customer.DateOfBirth);
        Assert.Equal(CustomerFormCreationHarness.Staff, customer.CreatedByUserId);
        Assert.Equal(Outcomes.ClientFormsSourceService, customer.Origin!.SourceService);
        Assert.Equal(command.SourceReferenceId, customer.Origin.SourceReferenceId);
        Assert.Single(harness.Bus.Published.OfType<CustomerCreatedIntegrationEvent>());
        var completed = Assert.Single(
            harness.Bus.Published.OfType<CustomerCreationFromFormCompletedIntegrationEvent>()
        );
        Assert.Equal(customer.Id, completed.CustomerId);
        Assert.Equal(command.RequestId, completed.RequestId);
        Assert.Equal(command.SourceReferenceId, completed.SourceReferenceId);
        Assert.False(completed.Replayed);
        Assert.Empty(completed.Warnings);
    }

    [Fact]
    public async Task The_same_command_twice_creates_one_customer_and_repeats_the_result()
    {
        var harness = new CustomerFormCreationHarness();
        var command = harness.Command();

        await harness.SendAsync(command);
        var detectionsAfterFirst = harness.Duplicates.Calls;
        await harness.SendAsync(command);

        var customer = Assert.Single(harness.Repository.All);
        var results = harness.Bus.Published.OfType<CustomerCreationFromFormCompletedIntegrationEvent>().ToList();
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(customer.Id, r.CustomerId));
        Assert.True(results[1].Replayed);
        Assert.Equal(detectionsAfterFirst, harness.Duplicates.Calls);
        Assert.Single(harness.Bus.Published.OfType<CustomerCreatedIntegrationEvent>());
    }

    [Fact]
    public async Task A_new_request_id_for_the_same_form_request_replays_without_reevaluating()
    {
        var harness = new CustomerFormCreationHarness();
        var formRequestId = Guid.NewGuid();
        await harness.SendAsync(harness.Command(formRequestId));
        var directoryReads = harness.Employees.Reads;
        harness.Grants.Revoke(CustomerFormCreationHarness.Tenant, CustomerFormCreationHarness.Staff);

        var retry = harness.Command(formRequestId);
        await harness.SendAsync(retry);

        Assert.Single(harness.Repository.All);
        var replayed = harness.Bus.Published.OfType<CustomerCreationFromFormCompletedIntegrationEvent>().Last();
        Assert.Equal(retry.RequestId, replayed.RequestId);
        Assert.True(replayed.Replayed);
        Assert.Equal(directoryReads, harness.Employees.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_late_replay_finds_the_customer_after_its_email_changed_or_it_was_archived(bool archive)
    {
        var harness = new CustomerFormCreationHarness();
        var command = harness.Command();
        await harness.SendAsync(command);
        var customer = harness.Repository.All[0];
        customer.ChangePrimaryEmail(
            EmailAddress.Create("ada.new@example.com").Value,
            CustomerFormCreationHarness.Staff
        );
        if (archive)
            customer.Archive(CustomerFormCreationHarness.Staff);

        await harness.SendAsync(command);

        Assert.Single(harness.Repository.All);
        var replayed = harness.Bus.Published.OfType<CustomerCreationFromFormCompletedIntegrationEvent>().Last();
        Assert.Equal(customer.Id, replayed.CustomerId);
        Assert.True(replayed.Replayed);
    }

    [Theory]
    [InlineData("Email")]
    [InlineData("Phone+Name")]
    [InlineData("Name+DOB")]
    public async Task A_duplicate_is_rejected_with_the_existing_customer_and_the_matching_rule(string matchedBy)
    {
        var harness = new CustomerFormCreationHarness();
        var existingId = Guid.NewGuid();
        harness.Duplicates.Match = new DuplicateMatch(0, existingId, "Ada Lovelace", matchedBy);

        await harness.SendAsync(harness.Command());

        Assert.Empty(harness.Repository.All);
        var rejected = Assert.Single(harness.Bus.Published.OfType<CustomerCreationFromFormRejectedIntegrationEvent>());
        Assert.Equal(Outcomes.DuplicateFound, rejected.ErrorCode);
        Assert.Equal(existingId, rejected.ExistingCustomerId);
        Assert.Equal(matchedBy, rejected.MatchedBy);
        Assert.Empty(harness.Bus.Published.OfType<CustomerUpdatedIntegrationEvent>());
        Assert.Equal(0, harness.UnitOfWork.Saves);
    }

    [Fact]
    public async Task The_lock_is_taken_before_the_duplicate_detector_and_never_names_the_email()
    {
        var harness = new CustomerFormCreationHarness();

        await harness.SendAsync(
            harness.Command(values: CustomerFormCreationHarness.IndividualValues("Ada@Example.com"))
        );

        Assert.Equal(["lock", "detect", "detect", "create"], harness.Steps);
        var resource = Assert.Single(harness.Lock.Resources);
        Assert.DoesNotContain("example", resource, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CustomerFormCreationHarness.Tenant.ToString("N"), resource.Split(':')[1]);
    }

    [Fact]
    public async Task A_creator_missing_from_the_directory_is_rejected()
    {
        var harness = new CustomerFormCreationHarness();

        await harness.SendAsync(harness.Command(requestedBy: Guid.NewGuid()));

        AssertRejected(harness, Outcomes.CreatorNotInDirectory);
    }

    [Fact]
    public async Task A_creator_from_another_tenant_is_treated_as_missing_from_the_directory()
    {
        var harness = new CustomerFormCreationHarness();
        var outsider = Guid.NewGuid();
        harness.Employees.Add(outsider, Guid.NewGuid(), "TenantEmployee");

        await harness.SendAsync(harness.Command(requestedBy: outsider));

        AssertRejected(harness, Outcomes.CreatorNotInDirectory);
    }

    [Fact]
    public async Task An_offboarded_creator_is_not_eligible()
    {
        var harness = new CustomerFormCreationHarness();
        var former = Guid.NewGuid();
        harness.Employees.Add(former, CustomerFormCreationHarness.Tenant, "TenantEmployee").MarkOffboarded();
        harness.Grants.Grant(CustomerFormCreationHarness.Tenant, former, "customers.manage");

        await harness.SendAsync(harness.Command(requestedBy: former));

        AssertRejected(harness, Outcomes.CreatorNotEligible);
    }

    [Fact]
    public async Task A_creator_without_customers_manage_in_the_projection_is_not_eligible()
    {
        var harness = new CustomerFormCreationHarness();
        harness.Grants.Grant(CustomerFormCreationHarness.Tenant, CustomerFormCreationHarness.Staff, "customers.view");

        await harness.SendAsync(harness.Command());

        AssertRejected(harness, Outcomes.CreatorNotEligible);
    }

    [Fact]
    public async Task A_tenant_admin_creates_without_an_explicit_grant_and_without_auto_assignment()
    {
        var harness = new CustomerFormCreationHarness();
        var admin = Guid.NewGuid();
        harness.Employees.Add(admin, CustomerFormCreationHarness.Tenant, "TenantAdmin");

        await harness.SendAsync(harness.Command(requestedBy: admin));

        var customer = Assert.Single(harness.Repository.All);
        Assert.Null(customer.AssignedPreparerUserId);
    }

    [Fact]
    public async Task A_payload_bound_to_another_command_is_discarded_with_a_security_log()
    {
        var harness = new CustomerFormCreationHarness();
        var original = harness.Command();
        var tampered = harness.Command() with { Payload = original.Payload };

        var exception = await Record.ExceptionAsync(() => harness.SendAsync(tampered));

        Assert.Null(exception);
        Assert.Empty(harness.Repository.All);
        Assert.Empty(harness.Bus.Published);
        Assert.Contains(harness.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.StartsWith("Security:"));
    }

    [Fact]
    public async Task A_command_from_an_unknown_source_service_is_discarded()
    {
        var harness = new CustomerFormCreationHarness();

        await harness.SendAsync(harness.Command() with { SourceService = "tasks.other" });

        Assert.Empty(harness.Bus.Published);
        Assert.Contains(harness.Logger.Entries, e => e.Message.StartsWith("Security:"));
    }

    [Fact]
    public async Task A_validation_failure_of_the_existing_create_is_published_as_rejected()
    {
        var harness = new CustomerFormCreationHarness();
        var values = CustomerFormCreationHarness.IndividualValues("not-an-email");

        await harness.SendAsync(harness.Command(values: values));

        AssertRejected(harness, "Email.Invalid");
    }

    [Fact]
    public async Task A_retired_or_unknown_target_is_rejected_as_not_supported()
    {
        var harness = new CustomerFormCreationHarness();
        var values = CustomerFormCreationHarness.IndividualValues();
        values.Add(new ClientFormTargetValue("customer.fiscal.tax_identifier", 1, "123456789"));

        await harness.SendAsync(harness.Command(values: values));

        AssertRejected(harness, Outcomes.FormTargetNotSupported);
    }

    [Fact]
    public async Task A_valid_address_is_saved_in_the_same_unit_of_work()
    {
        var harness = new CustomerFormCreationHarness();
        var values = CustomerFormCreationHarness.IndividualValues();
        values.Add(Address("customer.address.home", "10 Main St"));

        await harness.SendAsync(harness.Command(values: values));

        var address = Assert.Single(harness.Repository.All[0].Addresses);
        Assert.Equal(AddressKind.Home, address.Kind);
        Assert.True(address.IsPrimary);
        var completed = Assert.Single(
            harness.Bus.Published.OfType<CustomerCreationFromFormCompletedIntegrationEvent>()
        );
        Assert.Empty(completed.Warnings);
    }

    [Fact]
    public async Task An_invalid_address_keeps_the_customer_and_completes_with_a_warning()
    {
        var harness = new CustomerFormCreationHarness();
        var values = CustomerFormCreationHarness.IndividualValues();
        values.Add(Address("customer.address.mailing", " "));

        await harness.SendAsync(harness.Command(values: values));

        var customer = Assert.Single(harness.Repository.All);
        Assert.Empty(customer.Addresses);
        var completed = Assert.Single(
            harness.Bus.Published.OfType<CustomerCreationFromFormCompletedIntegrationEvent>()
        );
        Assert.Equal([Outcomes.AddressNotSavedWarning], completed.Warnings);
    }

    private static ClientFormTargetValue Address(string targetKey, string line1) =>
        new(targetKey, 1, Address: new ClientFormAddressValue(line1, null, "Miami", "FL", "33101", "us"));

    private static void AssertRejected(CustomerFormCreationHarness harness, string errorCode)
    {
        Assert.Empty(harness.Repository.All);
        Assert.Empty(harness.Bus.Published.OfType<CustomerCreationFromFormCompletedIntegrationEvent>());
        var rejected = Assert.Single(harness.Bus.Published.OfType<CustomerCreationFromFormRejectedIntegrationEvent>());
        Assert.Equal(errorCode, rejected.ErrorCode);
        Assert.Null(rejected.ExistingCustomerId);
    }
}
