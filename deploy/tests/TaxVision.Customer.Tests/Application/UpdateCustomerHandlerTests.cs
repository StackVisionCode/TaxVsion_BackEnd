using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Commands.Update;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Customers;
using static TaxVision.Customer.Tests.Application.CustomerAvatarTestDoubles;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Bug latente (análisis de avatares §2): el PATCH general escribía la foto siempre, así que cualquier
/// edición del CRM que no la mandara la borraba. La foto salió del comando y tiene su propio endpoint.
/// </summary>
public sealed class UpdateCustomerHandlerTests
{
    private static readonly Guid Editor = Guid.NewGuid();

    [Fact]
    public async Task Editing_a_customer_keeps_its_profile_picture()
    {
        var customer = WithPicture(out var photo);
        var bus = new FakeMessageBus();

        var result = await UpdateCustomerHandler.Handle(
            new UpdateCustomerCommand(
                TenantId: TenantId,
                CustomerId: customer.Id,
                ModifiedByUserId: Editor,
                Language: Language.Es,
                OccupationId: null,
                PrimaryEmail: customer.PrimaryEmail.Value,
                PrimaryPhone: null,
                FirstName: "Ana Maria",
                MiddleName: null,
                LastName: null,
                Prefix: null,
                Suffix: null,
                DateOfBirth: null,
                LegalName: null,
                Dba: null,
                BusinessStructure: null,
                FormationDate: null,
                PrincipalBusinessActivityId: null,
                CallerCanViewAllCustomers: true
            ),
            Repo(customer),
            new NoDuplicates(),
            new RecordingUnitOfWork(bus),
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(bus, Repo(customer), customer);

        Assert.True(result.IsSuccess);
        Assert.Equal(photo, customer.ProfilePictureFileId);
        Assert.Equal(photo, result.Value.AvatarFileId);
        Assert.Equal(photo, Assert.Single(bus.Published.OfType<CustomerUpdatedIntegrationEvent>()).AvatarFileId);
    }

    /// <summary>CUS-7: el evento lleva la política nueva; el canal que no viene en el PATCH se conserva.</summary>
    [Fact]
    public async Task Turning_sms_back_on_publishes_the_new_policy()
    {
        var customer = NewCustomer();
        customer.ChangeContactPolicy(emailEnabled: false, smsEnabled: false, Editor);
        var bus = new FakeMessageBus();

        var result = await UpdateCustomerHandler.Handle(
            new UpdateCustomerCommand(
                TenantId: TenantId,
                CustomerId: customer.Id,
                ModifiedByUserId: Editor,
                Language: customer.Language,
                OccupationId: null,
                PrimaryEmail: customer.PrimaryEmail.Value,
                PrimaryPhone: null,
                FirstName: null,
                MiddleName: null,
                LastName: null,
                Prefix: null,
                Suffix: null,
                DateOfBirth: null,
                LegalName: null,
                Dba: null,
                BusinessStructure: null,
                FormationDate: null,
                PrincipalBusinessActivityId: null,
                CallerCanViewAllCustomers: true,
                SmsEnabled: true
            ),
            Repo(customer),
            new NoDuplicates(),
            new RecordingUnitOfWork(bus),
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(bus, Repo(customer), customer);

        Assert.True(result.IsSuccess);
        var published = Assert.Single(bus.Published.OfType<CustomerUpdatedIntegrationEvent>());
        Assert.True(published.SmsEnabled);
        Assert.False(published.EmailEnabled);
    }

    // E-1: el email no se quita; un PATCH sin email conserva el que tenía.
    [Fact]
    public async Task An_edit_without_email_keeps_the_current_email()
    {
        var customer = NewCustomer();
        var email = customer.PrimaryEmail!.Value;

        var result = await UpdateAsync(customer, email: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(email, customer.PrimaryEmail!.Value);
    }

    // D-3: el email que llega después se guarda y sale en CustomerUpdated.
    [Fact]
    public async Task An_email_added_to_a_client_without_one_is_published()
    {
        var customer = TaxVision
            .Customer.Domain.Customers.Customer.Register(
                TenantId,
                CustomerKind.Individual,
                TaxVision.Customer.Domain.Customers.ValueObjects.PersonalName.Create("No", "Email").Value,
                null,
                null,
                TaxVision.Customer.Domain.Customers.ValueObjects.PhoneNumber.Create("+13055551234").Value,
                Language.En,
                Editor
            )
            .Value;
        customer.ClearDomainEvents();
        var bus = new FakeMessageBus();

        var result = await UpdateAsync(customer, email: "late@example.com", phone: "+13055551234", bus: bus);
        await DomainEventReplay.DispatchIntoAsync(bus, Repo(customer), customer);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(
            "late@example.com",
            Assert.Single(bus.Published.OfType<CustomerUpdatedIntegrationEvent>()).PrimaryEmail
        );
    }

    private static Task<BuildingBlocks.Results.Result<TaxVision.Customer.Application.Customers.CustomerResponse>> UpdateAsync(
        TaxVision.Customer.Domain.Customers.Customer customer,
        string? email,
        string? phone = null,
        FakeMessageBus? bus = null
    ) =>
        UpdateCustomerHandler.Handle(
            new UpdateCustomerCommand(
                TenantId: TenantId,
                CustomerId: customer.Id,
                ModifiedByUserId: Editor,
                Language: customer.Language,
                OccupationId: null,
                PrimaryEmail: email,
                PrimaryPhone: phone,
                FirstName: null,
                MiddleName: null,
                LastName: null,
                Prefix: null,
                Suffix: null,
                DateOfBirth: null,
                LegalName: null,
                Dba: null,
                BusinessStructure: null,
                FormationDate: null,
                PrincipalBusinessActivityId: null,
                CallerCanViewAllCustomers: true
            ),
            Repo(customer),
            new NoDuplicates(),
            new RecordingUnitOfWork(bus ?? new FakeMessageBus()),
            CancellationToken.None
        );

    /// <summary>El CRM manda propiedades que el contrato ya no tiene (preferredChannel, profilePictureFileId).</summary>
    [Fact]
    public void Request_binding_tolerates_the_removed_profile_picture_property()
    {
        var json = """
            {
              "language": "En",
              "occupationId": null,
              "profilePictureFileId": "5b2b1a59-6f5e-4a9e-9a43-3c6b9d0e7f10",
              "preferredChannel": "Sms",
              "primaryEmail": "ana@example.com"
            }
            """;

        var request =
            System.Text.Json.JsonSerializer.Deserialize<TaxVision.Customer.Api.Requests.UpdateCustomerRequest>(
                json,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                }
            );

        Assert.NotNull(request);
        Assert.Equal("ana@example.com", request!.PrimaryEmail);
    }

    private sealed class NoDuplicates : ICustomerDuplicateDetector
    {
        public Task<IReadOnlyList<DuplicateMatch>> FindDuplicatesAsync(
            Guid tenantId,
            IReadOnlyList<ImportCustomerRow> chunk,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DuplicateMatch>>([]);

        public Task<DuplicateMatch?> FindDuplicateAsync(
            Guid tenantId,
            CustomerDuplicateCandidate candidate,
            Guid? excludeCustomerId,
            CancellationToken ct
        ) => Task.FromResult<DuplicateMatch?>(null);
    }
}
