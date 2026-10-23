using System.Security.Cryptography;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Commands.AddAddress;
using TaxVision.Customer.Application.Customers.Commands.Create;
using TaxVision.Customer.Application.Customers.FromForms;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using Wolverine;
using Outcomes = BuildingBlocks.Messaging.ClientFormsIntegrationEvents.CustomerCreationFromFormOutcomes;

namespace TaxVision.Customer.Application.Customers.Consumers;

/// <summary>
/// Crea el cliente pedido por un formulario enviado con el alta existente, en línea y en la misma
/// transacción que la marca de origen, el resultado y la inbox: todo se confirma junto o nada.
/// </summary>
public static class CustomerCreationFromFormRequestedConsumer
{
    private const string TenantAdminActorType = "TenantAdmin";

    public static async Task Handle(
        CustomerCreationFromFormRequestedIntegrationEvent command,
        IProtectedPayloadCodec codec,
        ICustomerRepository repository,
        ICustomerDuplicateDetector duplicates,
        ITenantEmployeeDirectoryRepository employeeDirectory,
        IUserPermissionsProjectionRepository permissions,
        ICustomerCreationLock creationLock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CustomerCreationFromFormRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(CorrelationOf(command)))
        {
            var payload = Unprotect(command, codec, logger);
            if (payload is null)
                return;

            var context = new CreationContext(
                command,
                repository,
                duplicates,
                employeeDirectory,
                unitOfWork,
                bus,
                correlation
            );
            if (await TryReplayAsync(context, ct))
                return;

            var draft = await AuthorizeAndMapAsync(context, payload, permissions, ct);
            if (draft is not null)
                await CreateAsync(context, draft, creationLock, ct);
        }
    }

    // Los rechazos de negocio se publican, no se lanzan: reintentar no los cambia.
    private static async Task<CustomerFormDraft?> AuthorizeAndMapAsync(
        CreationContext context,
        CustomerCreationFromFormPayload payload,
        IUserPermissionsProjectionRepository permissions,
        CancellationToken ct
    )
    {
        var rejection = await CheckCreatorAsync(context.Command, context.EmployeeDirectory, permissions, ct);
        if (rejection is not null)
        {
            await PublishRejectedAsync(context, rejection);
            return null;
        }

        var draft = CustomerFormTargetMapper.Map(payload.Values);
        if (draft.IsSuccess)
            return draft.Value;

        await PublishRejectedAsync(context, draft.Error.Code);
        return null;
    }

    // La operación ya ocurrió: no se reevalúan permisos ni duplicados, solo se repite el resultado.
    private static async Task<bool> TryReplayAsync(CreationContext context, CancellationToken ct)
    {
        var existingId = await context.Repository.FindIdByOriginAsync(
            context.Command.TenantId,
            context.Command.SourceService,
            context.Command.SourceReferenceId,
            ct
        );
        if (existingId is not { } customerId)
            return false;

        await PublishCompletedAsync(context, customerId, replayed: true, []);
        return true;
    }

    private static CustomerCreationFromFormPayload? Unprotect(
        CustomerCreationFromFormRequestedIntegrationEvent command,
        IProtectedPayloadCodec codec,
        ILogger logger
    )
    {
        try
        {
            if (!string.Equals(command.SourceService, Outcomes.ClientFormsSourceService, StringComparison.Ordinal))
                throw new CryptographicException("Unknown source service.");

            var binding = new ProtectedPayloadBinding(
                command.TenantId,
                command.SourceReferenceId,
                command.Participant,
                IdempotencyKey: command.IdempotencyKey
            );
            return codec.Unprotect<CustomerCreationFromFormPayload>(
                command.Payload,
                ClientFormProtectedPayloadSchemas.CustomerCreation,
                binding
            );
        }
        catch (CryptographicException)
        {
            // Reintentar no arregla un payload que no corresponde al sobre: se descarta.
            logger.LogWarning(
                "Security: discarded customer creation command {RequestId} for tenant {TenantId}; protected payload validation failed.",
                command.RequestId,
                command.TenantId
            );
            return null;
        }
    }

    // El alta por bus es diferida: el responsable pudo irse o perder el permiso desde que se fijó.
    private static async Task<string?> CheckCreatorAsync(
        CustomerCreationFromFormRequestedIntegrationEvent command,
        ITenantEmployeeDirectoryRepository employeeDirectory,
        IUserPermissionsProjectionRepository permissions,
        CancellationToken ct
    )
    {
        var creator = await employeeDirectory.GetByUserIdAsync(command.RequestedByUserId, ct);
        if (creator is null || creator.TenantId != command.TenantId)
            return Outcomes.CreatorNotInDirectory;
        if (!creator.IsEligiblePreparer)
            return Outcomes.CreatorNotEligible;
        if (creator.ActorType == TenantAdminActorType)
            return null;

        var operation = CustomerDataCollectionTargets.Operations.Single(o =>
            o.Operation == DataCollectionOperation.CreateCustomer
        );
        var projection = await permissions.GetAsync(command.TenantId, command.RequestedByUserId, ct);
        IReadOnlyList<string> granted = projection is { IsActive: true } ? projection.PermissionCodes() : [];
        var allowed = !operation.RequiresTenantAdmin && operation.RequiredPermissions.All(granted.Contains);
        return allowed ? null : Outcomes.CreatorNotEligible;
    }

    private static async Task CreateAsync(
        CreationContext context,
        CustomerFormDraft draft,
        ICustomerCreationLock creationLock,
        CancellationToken ct
    )
    {
        var email = EmailAddress.Create(draft.PrimaryEmail ?? string.Empty);
        if (email.IsFailure)
        {
            await PublishRejectedAsync(context, email.Error.Code);
            return;
        }

        await creationLock.AcquireAsync(context.Command.TenantId, email.Value.NormalizedValue, ct);

        var match = await FindDuplicateAsync(context, draft, email.Value, ct);
        if (match is not null)
        {
            await PublishRejectedAsync(context, Outcomes.DuplicateFound, match.ExistingCustomerId, match.MatchedBy);
            return;
        }

        var created = await RegisterAsync(context, draft, ct);
        if (created.IsFailure)
        {
            await PublishRejectedAsync(context, created.Error.Code);
            return;
        }

        await RecordOriginAsync(context, created.Value.Id, ct);
        var warnings = await AddAddressesAsync(context, created.Value.Id, draft, ct);
        await PublishCompletedAsync(context, created.Value.Id, replayed: false, warnings);
    }

    // En línea, nunca por el bus: su SaveChanges y sus eventos entran en la transacción y el outbox del consumer.
    private static Task<Result<CustomerResponse>> RegisterAsync(
        CreationContext context,
        CustomerFormDraft draft,
        CancellationToken ct
    ) =>
        CreateCustomerHandler.Handle(
            draft.ToCommand(context.Command.TenantId, context.Command.RequestedByUserId),
            context.Repository,
            context.Duplicates,
            context.EmployeeDirectory,
            context.UnitOfWork,
            context.Bus,
            context.Correlation,
            ct
        );

    // Mismo detector y mismo candidato que el alta, solo para devolver el id y el criterio estructurados.
    private static Task<DuplicateMatch?> FindDuplicateAsync(
        CreationContext context,
        CustomerFormDraft draft,
        EmailAddress email,
        CancellationToken ct
    )
    {
        var phone = string.IsNullOrWhiteSpace(draft.PrimaryPhone) ? null : PhoneNumber.Create(draft.PrimaryPhone);
        var displayName = draft.Kind == CustomerKind.Business ? BusinessDisplayName(draft) : PersonDisplayName(draft);
        if (displayName is null || phone is { IsFailure: true })
            return Task.FromResult<DuplicateMatch?>(null);

        return context.Duplicates.FindDuplicateAsync(
            context.Command.TenantId,
            new CustomerDuplicateCandidate(email.Value, phone?.Value.E164Value, displayName, draft.DateOfBirth),
            excludeCustomerId: null,
            ct
        );
    }

    private static string? PersonDisplayName(CustomerFormDraft draft)
    {
        var name = PersonalName.Create(
            draft.FirstName ?? string.Empty,
            draft.LastName ?? string.Empty,
            draft.MiddleName,
            draft.Prefix,
            draft.Suffix
        );
        return name.IsSuccess ? name.Value.DisplayName : null;
    }

    private static string? BusinessDisplayName(CustomerFormDraft draft)
    {
        if (draft.BusinessStructure is not { } structure)
            return null;
        var identity = BusinessIdentity.Create(draft.LegalName ?? string.Empty, structure);
        return identity.IsSuccess ? identity.Value.LegalName : null;
    }

    private static async Task RecordOriginAsync(CreationContext context, Guid customerId, CancellationToken ct)
    {
        var customer =
            await context.Repository.GetByIdAsync(customerId, ct)
            ?? throw new InvalidOperationException("The customer created from the form was not found.");
        var origin = CustomerOrigin.Create(context.Command.SourceService, context.Command.SourceReferenceId);
        Result recorded = origin.IsSuccess ? customer.RecordOrigin(origin.Value) : origin;

        // Sin origen no hay idempotencia: se revierte el alta completa y el bus reintenta.
        if (recorded.IsFailure)
            throw new InvalidOperationException($"Customer origin could not be recorded: {recorded.Error.Code}.");
        await context.UnitOfWork.SaveChangesAsync(ct);
    }

    private static async Task<IReadOnlyList<string>> AddAddressesAsync(
        CreationContext context,
        Guid customerId,
        CustomerFormDraft draft,
        CancellationToken ct
    )
    {
        var saved = true;
        foreach (
            var (kind, address) in new[]
            {
                (AddressKind.Home, draft.HomeAddress),
                (AddressKind.Mailing, draft.MailingAddress),
            }
        )
        {
            if (address is null)
                continue;
            var added = await AddAddressHandler.Handle(
                AddressCommand(context.Command, customerId, kind, address),
                context.Repository,
                context.UnitOfWork,
                ct
            );
            saved &= added.IsSuccess;
        }

        // Una dirección inválida no deshace el alta: el cliente queda y Tasks recibe el aviso.
        return saved ? [] : [Outcomes.AddressNotSavedWarning];
    }

    private static AddAddressCommand AddressCommand(
        CustomerCreationFromFormRequestedIntegrationEvent command,
        Guid customerId,
        AddressKind kind,
        ClientFormAddressValue address
    ) =>
        new(
            command.TenantId,
            customerId,
            command.RequestedByUserId,
            kind,
            address.Line1,
            address.Line2,
            address.City,
            address.Region,
            address.PostalCode,
            address.CountryCode,
            IsPrimary: true,
            CallerCanViewAllCustomers: true
        );

    private static Task PublishCompletedAsync(
        CreationContext context,
        Guid customerId,
        bool replayed,
        IReadOnlyList<string> warnings
    ) =>
        context
            .Bus.PublishAsync(
                new CustomerCreationFromFormCompletedIntegrationEvent
                {
                    TenantId = context.Command.TenantId,
                    CorrelationId = context.Correlation.CorrelationId,
                    RequestId = context.Command.RequestId,
                    SourceReferenceId = context.Command.SourceReferenceId,
                    CustomerId = customerId,
                    Replayed = replayed,
                    Warnings = warnings,
                }
            )
            .AsTask();

    private static Task PublishRejectedAsync(
        CreationContext context,
        string errorCode,
        Guid? existingCustomerId = null,
        string? matchedBy = null
    ) =>
        context
            .Bus.PublishAsync(
                new CustomerCreationFromFormRejectedIntegrationEvent
                {
                    TenantId = context.Command.TenantId,
                    CorrelationId = context.Correlation.CorrelationId,
                    RequestId = context.Command.RequestId,
                    SourceReferenceId = context.Command.SourceReferenceId,
                    ErrorCode = errorCode,
                    ExistingCustomerId = existingCustomerId,
                    MatchedBy = matchedBy,
                }
            )
            .AsTask();

    private static string CorrelationOf(CustomerCreationFromFormRequestedIntegrationEvent command) =>
        string.IsNullOrWhiteSpace(command.CorrelationId) ? command.RequestId.ToString("N") : command.CorrelationId;

    private sealed record CreationContext(
        CustomerCreationFromFormRequestedIntegrationEvent Command,
        ICustomerRepository Repository,
        ICustomerDuplicateDetector Duplicates,
        ITenantEmployeeDirectoryRepository EmployeeDirectory,
        IUnitOfWork UnitOfWork,
        IMessageBus Bus,
        ICorrelationContext Correlation
    );
}
