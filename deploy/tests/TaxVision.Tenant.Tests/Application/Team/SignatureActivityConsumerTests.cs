using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using TaxVision.Tenant.Application.Team.Consumers;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application.Team;

public sealed class SignatureActivityConsumerTests : ActivityConsumerContract
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    protected override IEnumerable<ActivityCase> Cases()
    {
        var created = Stamp(
            new SignatureRequestCreatedIntegrationEvent
            {
                SignatureRequestId = RequestId,
                CreatedByUserId = Actor,
                Title = "8879",
                Category = "Tax",
                OriginalFileIds = [Guid.NewGuid()],
                DocumentCount = 1,
                TokenExpirationHours = null,
                RequiresSequentialSigning = false,
                SignerCount = 1,
                ExpiresAtUtc = null,
                CustomerId = CustomerId,
            }
        );
        yield return new(
            created,
            created with
            {
                CreatedByUserId = Guid.Empty,
            },
            (e, r, u, c) =>
                SignatureActivityConsumer.Handle((SignatureRequestCreatedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.SignatureCreated,
            RequestId,
            CustomerId,
            ActivityVisibility.Team
        );

        // Quien envía no tiene por qué ser quien creó: solo cuenta SentByUserId.
        var sent = Stamp(
            new SignatureRequestSentIntegrationEvent
            {
                SignatureRequestId = RequestId,
                CreatedByUserId = Guid.NewGuid(),
                SentAtUtc = DateTime.UtcNow,
                SignerIds = [Guid.NewGuid()],
                SentByUserId = Actor,
                CustomerId = CustomerId,
            }
        );
        yield return new(
            sent,
            sent with
            {
                SentByUserId = null,
            },
            (e, r, u, c) => SignatureActivityConsumer.Handle((SignatureRequestSentIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.SignatureSent,
            RequestId,
            CustomerId,
            ActivityVisibility.Team
        );

        var signed = Stamp(
            new PreparerSignedIntegrationEvent
            {
                SignatureRequestId = RequestId,
                CreatedByUserId = Guid.NewGuid(),
                PreparerUserId = Actor,
                PtinOrEfin = "P01234567",
                PreparerDisplayName = "Ana",
                SignedAtUtc = DateTime.UtcNow,
                CustomerId = CustomerId,
            }
        );
        yield return new(
            signed,
            signed with
            {
                PreparerUserId = Guid.Empty,
            },
            (e, r, u, c) => SignatureActivityConsumer.Handle((PreparerSignedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.SignaturePreparerSigned,
            RequestId,
            CustomerId,
            ActivityVisibility.Team
        );

        var canceled = Stamp(
            new SignatureRequestCanceledIntegrationEvent
            {
                SignatureRequestId = RequestId,
                CreatedByUserId = Guid.NewGuid(),
                CanceledByUserId = Actor,
                CanceledAtUtc = DateTime.UtcNow,
                PendingSignerIds = [],
            }
        );
        yield return new(
            canceled,
            canceled with
            {
                CanceledByUserId = Guid.Empty,
            },
            (e, r, u, c) =>
                SignatureActivityConsumer.Handle((SignatureRequestCanceledIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.SignatureCanceled,
            RequestId,
            null,
            ActivityVisibility.Team
        );
    }
}
