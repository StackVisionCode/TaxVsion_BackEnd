using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using DomainExternalReference = TaxVision.Signature.Domain.Requests.ValueObjects.ExternalReference;

namespace TaxVision.Signature.Application.Messaging;

/// <summary>P-18: traduce la correlación de negocio del dominio al contrato de mensajería (aditivo).</summary>
public static class SignatureExternalReferenceMapper
{
    public static SignatureExternalReference? ToMessaging(this DomainExternalReference? reference) =>
        reference is null
            ? null
            : new SignatureExternalReference
            {
                SourceService = reference.SourceService,
                SourceType = reference.SourceType,
                SourceId = reference.SourceId,
            };
}
