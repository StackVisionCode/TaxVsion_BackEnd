using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Tests.Domain;

public sealed class MailboxFallbackPolicyTests
{
    [Theory]
    // La culpa es del buzón o del proveedor: el mismo correo puede salir por el sistema.
    [InlineData(MailboxSendFailure.MailboxUnavailable, true)]
    [InlineData(MailboxSendFailure.ProviderUnavailable, true)]
    // El correo en sí está mal: por otra vía fallaría igual (o le llegaría mal al destinatario).
    [InlineData(MailboxSendFailure.InvalidMessage, false)]
    // Cupo por minuto: se difiere y se reintenta por el mismo buzón.
    [InlineData(MailboxSendFailure.Throttled, false)]
    // Sin saber qué pasó no se arriesga un duplicado.
    [InlineData(MailboxSendFailure.Unknown, false)]
    public void ShouldFallBack_depends_on_whose_fault_it_was(MailboxSendFailure failure, bool expected)
    {
        Assert.Equal(
            expected,
            MailboxFallbackPolicy.ShouldFallBack(ProviderScope.TenantPreferred, EmailStream.Transactional, failure)
        );
    }

    [Fact]
    public void ShouldFallBack_never_for_the_strict_mailbox_scope()
    {
        Assert.False(
            MailboxFallbackPolicy.ShouldFallBack(
                ProviderScope.TenantMailbox,
                EmailStream.Transactional,
                MailboxSendFailure.MailboxUnavailable
            )
        );
    }

    [Fact]
    public void ShouldFallBack_never_for_bulk_mail()
    {
        // Una campaña por el sistema quemaría la reputación de envío de todas las oficinas.
        Assert.False(
            MailboxFallbackPolicy.ShouldFallBack(
                ProviderScope.TenantPreferred,
                EmailStream.Bulk,
                MailboxSendFailure.MailboxUnavailable
            )
        );
    }
}
