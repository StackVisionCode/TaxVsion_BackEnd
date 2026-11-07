using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.Employees;
using Wolverine;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Customers.Commands.Create;

public static class CreateCustomerHandler
{
    public static async Task<Result<CustomerResponse>> Handle(
        CreateCustomerCommand cmd,
        ICustomerRepository repository,
        ICustomerDuplicateDetector duplicates,
        ITenantEmployeeDirectoryRepository employeeDirectory,
        ICustomerCreationLock creationLock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var draftResult = CustomerDraftBuilder.Build(cmd);
        if (draftResult.IsFailure)
            return Result.Failure<CustomerResponse>(draftResult.Error);

        var draft = draftResult.Value;

        await AcquireCreationLockAsync(creationLock, cmd.TenantId, draft.Email, ct);

        // El mismo detector que usa el import: dos puertas al mismo dato con reglas distintas terminan
        // en que la laxa se usa para meter lo que la otra rechaza.
        var match = await duplicates.FindDuplicateAsync(
            cmd.TenantId,
            new CustomerDuplicateCandidate(
                draft.Email.Value,
                draft.Phone?.E164Value,
                draft.DisplayNameFor(cmd.Kind),
                cmd.DateOfBirth
            ),
            excludeCustomerId: null,
            ct
        );

        if (match is not null)
        {
            if (!cmd.Overwrite)
                return Result.Failure<CustomerResponse>(
                    CustomerDuplicateErrors.DuplicateFound(
                        match.ExistingCustomerId,
                        match.ExistingDisplayName,
                        match.MatchedBy
                    )
                );

            return await OverwriteAsync(match.ExistingCustomerId, cmd, draft, repository, unitOfWork, ct);
        }

        var customerResult = draft.Register(cmd);
        if (customerResult.IsFailure)
            return Result.Failure<CustomerResponse>(customerResult.Error);

        var customer = customerResult.Value;

        // El alta auto-asigna al creador como preparador primary, salvo que sea admin (el admin ve todo
        // por permiso, no necesita fila). Si el directorio aún no lo tiene, se asigna igual: mejor dejarlo
        // con acceso a su propio cliente que perderlo. Va antes de persistir → mismo SaveChanges.
        var creator = await employeeDirectory.GetByUserIdAsync(cmd.CreatedByUserId, ct);
        var creatorIsAdmin = creator?.ActorType is "TenantAdmin" or "PlatformAdmin";
        var autoAssigned =
            !creatorIsAdmin
            && CanBeAutoAssigned(creator, cmd.TenantId)
            && customer.AssignPreparer(cmd.CreatedByUserId, cmd.CreatedByUserId).IsSuccess;

        // CustomerCreated y el snapshot de asignaciones salen de los eventos de dominio.
        await PersistCustomerAsync(customer, repository, unitOfWork, ct);
        if (autoAssigned)
            await PublishPreparerAssignedEventAsync(customer, cmd.CreatedByUserId, correlation, bus);

        return Result.Success(MapToResponse(customer));
    }

    // El email siempre existe: lo exigen el alta y la importación. Sin candado a tiempo, el usuario reintenta.
    private static async Task AcquireCreationLockAsync(
        ICustomerCreationLock creationLock,
        Guid tenantId,
        EmailAddress email,
        CancellationToken ct
    )
    {
        try
        {
            await creationLock.AcquireAsync(tenantId, email.NormalizedValue, ct);
        }
        catch (TimeoutException ex)
        {
            throw new ConflictException(
                "Customer.CreationBusy",
                "A client with this email is being created right now. Try again in a moment.",
                ex
            );
        }
    }

    /// <summary>
    /// Le aplica los datos nuevos al cliente que ya estaba, en vez de crear otro.
    ///
    /// <para>
    /// Anuncia <c>CustomerUpdated</c> (evento de dominio) y <b>no</b> <c>CustomerCreated</c>: los siete servicios que
    /// proyectan el directorio de clientes aplican por fecha, y anunciar como alta lo que fue una
    /// edición les diría que existe un cliente que no nació hoy.
    /// </para>
    /// </summary>
    private static async Task<Result<CustomerResponse>> OverwriteAsync(
        Guid existingCustomerId,
        CreateCustomerCommand cmd,
        CustomerDraft draft,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByIdAsync(cmd.TenantId, existingCustomerId, ct);
        if (existing is null)
            return Result.Failure<CustomerResponse>(new Error("Customer.NotFound", "Customer not found."));

        var applied = draft.ApplyTo(existing, cmd);
        if (applied.IsFailure)
            return Result.Failure<CustomerResponse>(applied.Error);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(MapToResponse(existing));
    }

    // B-13: un creador conocido y no elegible (retirado, inactivo, portal u otro tenant) no queda como preparador.
    private static bool CanBeAutoAssigned(TenantEmployeeDirectoryEntry? creator, Guid tenantId) =>
        creator is null || (creator.TenantId == tenantId && creator.IsEligiblePreparer);

    private static async Task PersistCustomerAsync(
        CustomerEntity customer,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await repository.AddAsync(customer, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    // Anuncia el preparador auto-asignado en el alta (mismo evento que el endpoint de asignar), para que
    // chat y las proyecciones de visibilidad downstream conozcan al responsable del cliente nuevo.
    private static Task PublishPreparerAssignedEventAsync(
        CustomerEntity customer,
        Guid preparerUserId,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new CustomerPreparerAssignedIntegrationEvent
                {
                    TenantId = customer.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    CustomerId = customer.Id,
                    PreparerUserId = preparerUserId,
                    AssignedByUserId = preparerUserId,
                }
            )
            .AsTask();

    // ============== Mapeo aggregate -> response ==============

    private static CustomerResponse MapToResponse(CustomerEntity c) =>
        new(
            c.Id,
            c.TenantId,
            c.Kind,
            c.Status,
            c.DisplayName,
            c.PrimaryEmail.Value,
            c.PrimaryPhone?.E164Value,
            c.Language,
            c.OccupationId,
            OccupationName: null,
            c.BusinessIdentity?.PrincipalBusinessActivityId,
            PrincipalBusinessActivityName: null,
            c.CreatedAtUtc,
            AvatarFileId: c.ProfilePictureFileId
        );
}
