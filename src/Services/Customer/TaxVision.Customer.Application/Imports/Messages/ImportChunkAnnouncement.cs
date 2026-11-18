using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Application.Imports.Messages;

/// <summary>Arma el aviso de un bloque guardado con la ficha de directorio de cada cliente (sin datos fiscales).</summary>
internal static class ImportChunkAnnouncement
{
    internal static CustomersImportChunkCommittedIntegrationEvent For(
        CustomerImportAttempt attempt,
        int chunkNumber,
        IReadOnlyList<ImportRowOutcome> touched,
        string correlationId
    ) =>
        new()
        {
            TenantId = attempt.TenantId,
            CorrelationId = correlationId,
            ImportJobId = attempt.Id,
            ChunkNumber = chunkNumber,
            CreatedByUserId = attempt.CreatedByUserId,
            Customers = touched
                .Select(outcome => Card(outcome.Customer!, outcome.Kind == ImportRowOutcomeKind.Success))
                .ToList(),
        };

    private static ImportedCustomerCard Card(Domain.Customers.Customer customer, bool isNew) =>
        new()
        {
            CustomerId = customer.Id,
            IsNew = isNew,
            Kind = customer.Kind.ToString(),
            DisplayName = customer.DisplayName,
            PrimaryEmail = customer.PrimaryEmail?.Value,
            PrimaryPhone = customer.PrimaryPhone?.E164Value,
            Language = customer.Language.ToString(),
            Status = customer.Status.ToString(),
            EmailEnabled = customer.ContactPolicy.EmailEnabled,
            SmsEnabled = customer.ContactPolicy.SmsEnabled,
            OccupationId = customer.OccupationId,
            AvatarFileId = customer.ProfilePictureFileId,
        };
}
