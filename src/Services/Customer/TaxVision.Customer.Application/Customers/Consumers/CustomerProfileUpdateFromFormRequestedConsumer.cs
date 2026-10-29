using System.Security.Cryptography;
using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.FromForms;
using TaxVision.Customer.Domain.Audit;
using Wolverine;
using CreationOutcomes = BuildingBlocks.Messaging.ClientFormsIntegrationEvents.CustomerCreationFromFormOutcomes;
using Outcomes = BuildingBlocks.Messaging.ClientFormsIntegrationEvents.CustomerProfileUpdateFromFormOutcomes;

namespace TaxVision.Customer.Application.Customers.Consumers;

/// <summary>
/// P-13 — Aplica al perfil del cliente las respuestas aprobadas de un formulario. Descifra en memoria, verifica
/// tenant/cliente/request, REVALIDA permisos con su proyección (defensa en profundidad) y aplica con métodos de
/// dominio explícitos. Publica el hecho Applied (con fallos = parcial) o Rejected. El SSN nunca se registra.
/// </summary>
public static class CustomerProfileUpdateFromFormRequestedConsumer
{
    private const string TenantAdminActorType = "TenantAdmin";
    private const string PlatformAdminActorType = "PlatformAdmin";
    private const string CustomerManage = "customers.manage";

    public static async Task Handle(
        CustomerProfileUpdateFromFormRequestedIntegrationEvent command,
        IProtectedPayloadCodec codec,
        ICustomerRepository repository,
        ITenantEmployeeDirectoryRepository employeeDirectory,
        IUserPermissionsProjectionRepository permissions,
        ISensitiveDataProtector protector,
        ICustomerAuditWriter auditWriter,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CustomerProfileUpdateFromFormRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(CorrelationOf(command)))
        {
            var payload = Unprotect(command, codec, logger);
            if (payload is null)
                return;

            var customerId = command.Participant.CustomerId;
            var customer = customerId is { } id ? await repository.GetByIdAsync(id, ct) : null;
            if (customer is null || customer.TenantId != command.TenantId)
            {
                await PublishRejectedAsync(bus, command, correlation, Fail(payload, Outcomes.CustomerNotFound));
                return;
            }

            var (canManage, isAdmin) = await AuthorizeAsync(command, payload, employeeDirectory, permissions, ct);
            if (!canManage)
            {
                await PublishRejectedAsync(bus, command, correlation, Fail(payload, Outcomes.NotAuthorized));
                return;
            }

            var result = await CustomerProfileUpdateApplier.ApplyAsync(
                customer,
                payload.Values,
                protector,
                repository,
                isAdmin,
                payload.RequestedByUserId,
                ct
            );

            await WriteAuditAsync(command, payload, result, auditWriter, correlation, ct);
            await unitOfWork.SaveChangesAsync(ct);

            if (result.AppliedTargets.Count == 0)
                await PublishRejectedAsync(bus, command, correlation, result.Failures);
            else
                await PublishAppliedAsync(bus, command, correlation, result);
        }
    }

    private static async Task<(bool CanManage, bool IsAdmin)> AuthorizeAsync(
        CustomerProfileUpdateFromFormRequestedIntegrationEvent command,
        CustomerProfileUpdateFromFormPayload payload,
        ITenantEmployeeDirectoryRepository employeeDirectory,
        IUserPermissionsProjectionRepository permissions,
        CancellationToken ct
    )
    {
        // PlatformAdmin no es empleado del tenant; su tipo viaja autenticado dentro del payload cifrado.
        if (string.Equals(payload.RequestedByActorType, PlatformAdminActorType, StringComparison.Ordinal))
            return (true, true);

        var creator = await employeeDirectory.GetByUserIdAsync(payload.RequestedByUserId, ct);
        if (creator is null || creator.TenantId != command.TenantId)
            return (false, false);
        var isAdmin = creator.ActorType == TenantAdminActorType;
        if (isAdmin)
            return (true, true);

        var projection = await permissions.GetAsync(command.TenantId, payload.RequestedByUserId, ct);
        var granted = projection is { IsActive: true } && projection.PermissionCodes().Contains(CustomerManage);
        return (granted, false);
    }

    private static CustomerProfileUpdateFromFormPayload? Unprotect(
        CustomerProfileUpdateFromFormRequestedIntegrationEvent command,
        IProtectedPayloadCodec codec,
        ILogger logger
    )
    {
        try
        {
            if (
                !string.Equals(
                    command.SourceService,
                    CreationOutcomes.ClientFormsSourceService,
                    StringComparison.Ordinal
                )
            )
                throw new CryptographicException("Unknown source service.");
            var binding = new ProtectedPayloadBinding(
                command.TenantId,
                command.SourceReferenceId,
                command.Participant,
                IdempotencyKey: command.IdempotencyKey
            );
            return codec.Unprotect<CustomerProfileUpdateFromFormPayload>(
                command.Payload,
                ClientFormProtectedPayloadSchemas.CustomerProfileUpdate,
                binding
            );
        }
        catch (CryptographicException)
        {
            // Reintentar no arregla un payload que no corresponde al sobre: se descarta (sin el valor en el log).
            logger.LogWarning(
                "Security: discarded profile update command {CommandId} for tenant {TenantId}; protected payload validation failed.",
                command.CommandId,
                command.TenantId
            );
            return null;
        }
    }

    private static async Task WriteAuditAsync(
        CustomerProfileUpdateFromFormRequestedIntegrationEvent command,
        CustomerProfileUpdateFromFormPayload payload,
        CustomerProfileUpdateApplier.ProfileUpdateResult result,
        ICustomerAuditWriter auditWriter,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var customerId = command.Participant.CustomerId!.Value;
        var outcome =
            result.AppliedTargets.Count == 0 ? "rejected"
            : result.Failures.Count == 0 ? "applied"
            : "partial";
        var details = JsonSerializer.Serialize(new { applied = result.AppliedTargets });
        await auditWriter.AddAsync(
            CustomerAuditLog.Create(
                command.TenantId,
                customerId,
                payload.RequestedByUserId,
                CustomerAuditAction.ProfileUpdatedFromForm,
                outcome,
                ipAddress: null,
                userAgent: null,
                correlation.CorrelationId,
                details,
                command.OccurredOn
            ),
            ct
        );

        var fiscalApplied = result.AppliedTargets.Any(target =>
            target.StartsWith("customer.fiscal", StringComparison.Ordinal)
        );
        if (fiscalApplied)
            await auditWriter.AddAsync(
                CustomerAuditLog.Create(
                    command.TenantId,
                    customerId,
                    payload.RequestedByUserId,
                    CustomerAuditAction.FiscalProfileUpdatedFromForm,
                    "applied",
                    ipAddress: null,
                    userAgent: null,
                    correlation.CorrelationId,
                    details: null,
                    command.OccurredOn
                ),
                ct
            );
    }

    private static IReadOnlyList<ClientFormProfileUpdateFailure> Fail(
        CustomerProfileUpdateFromFormPayload payload,
        string code
    )
    {
        var failures = new List<ClientFormProfileUpdateFailure>();
        foreach (var value in payload.Values)
            failures.Add(new ClientFormProfileUpdateFailure(value.TargetKey, code));
        return failures;
    }

    private static Task PublishAppliedAsync(
        IMessageBus bus,
        CustomerProfileUpdateFromFormRequestedIntegrationEvent command,
        ICorrelationContext correlation,
        CustomerProfileUpdateApplier.ProfileUpdateResult result
    ) =>
        bus.PublishAsync(
                new CustomerProfileUpdateFromFormAppliedIntegrationEvent
                {
                    TenantId = command.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    CommandId = command.CommandId,
                    SourceReferenceId = command.SourceReferenceId,
                    AppliedTargets = result.AppliedTargets,
                    Failures = result.Failures,
                }
            )
            .AsTask();

    private static Task PublishRejectedAsync(
        IMessageBus bus,
        CustomerProfileUpdateFromFormRequestedIntegrationEvent command,
        ICorrelationContext correlation,
        IReadOnlyList<ClientFormProfileUpdateFailure> failures
    ) =>
        bus.PublishAsync(
                new CustomerProfileUpdateFromFormRejectedIntegrationEvent
                {
                    TenantId = command.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    CommandId = command.CommandId,
                    SourceReferenceId = command.SourceReferenceId,
                    Failures = failures,
                }
            )
            .AsTask();

    private static string CorrelationOf(CustomerProfileUpdateFromFormRequestedIntegrationEvent command) =>
        string.IsNullOrWhiteSpace(command.CorrelationId) ? command.CommandId.ToString("N") : command.CorrelationId;
}
