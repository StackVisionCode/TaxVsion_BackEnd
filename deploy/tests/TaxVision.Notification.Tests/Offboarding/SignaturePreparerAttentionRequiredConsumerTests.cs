using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers.Offboarding;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Offboarding;

/// <summary>Solicitud de firma que necesita atención por un retiro: destinatario concreto o admins de firma.</summary>
public sealed class SignaturePreparerAttentionRequiredConsumerTests
{
    private static readonly Guid TenantId = OffboardingNoticeHarness.TenantId;
    private static readonly Guid Leaver = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Author = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid SignatureAdmin = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid RequestId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Con_destinatario_activo_le_avisa_solo_a_el()
    {
        var harness = Harness(active: [Author, SignatureAdmin]);

        await HandleAsync(harness, Event(SignaturePreparerAttentionReasons.PreparerCleared, recipient: Author));

        Assert.Equal(new[] { Author }, harness.InAppRecipients);
        Assert.Empty(harness.Resolver.Audiences.OfType<ByPermission>());
        Assert.Equal(new[] { OffboardingNoticeHarness.EmailOf(Author) }, harness.Postmaster.Select(e => e.To));
    }

    [Fact]
    public async Task Sin_destinatario_avisa_a_la_audiencia_del_evento()
    {
        var harness = Harness(active: [Author, SignatureAdmin]);

        await HandleAsync(harness, Event(SignaturePreparerAttentionReasons.PreparerChangeBlocked, recipient: null));

        Assert.Equal(new[] { SignatureAdmin }, harness.InAppRecipients);
        Assert.Equal(
            "signature.request.manage",
            Assert.Single(harness.Resolver.Audiences.OfType<ByPermission>()).PermissionCode
        );
    }

    [Fact]
    public async Task Si_el_destinatario_ya_no_esta_activo_cae_a_la_audiencia()
    {
        var harness = Harness(active: [SignatureAdmin]);

        await HandleAsync(harness, Event(SignaturePreparerAttentionReasons.PreparerCleared, recipient: Author));

        Assert.Equal(new[] { SignatureAdmin }, harness.InAppRecipients);
    }

    [Theory]
    [InlineData(
        "PreparerCleared",
        "Choose a new preparer for “Smith 1040”",
        "Choose a new preparer before you send it."
    )]
    [InlineData(
        "PreparerChangeBlocked",
        "“Smith 1040” needs a new preparer",
        "Change the preparer so the return can be signed."
    )]
    [InlineData(
        "UnsentRequestKept",
        "Review the unsent request “Smith 1040”",
        "decide whether to send it or cancel it."
    )]
    [InlineData("SomethingNew", "“Smith 1040” needs your attention", "no longer with your office")]
    public async Task El_texto_depende_del_motivo(string reason, string title, string bodyFragment)
    {
        var harness = Harness(active: [SignatureAdmin], host: "manfer.taxoffix.io");

        await HandleAsync(harness, Event(reason, recipient: null));

        var row = Assert.Single(harness.InAppRows);
        Assert.Equal(NotificationKinds.SignaturePreparerAttentionRequired, row.Kind);
        Assert.Equal("signature_request", row.ResourceType);
        Assert.Equal(RequestId.ToString(), row.ResourceId);
        Assert.Equal(title, row.FallbackTitle);
        Assert.Contains(bodyFragment, row.FallbackBody);
        // El detalle técnico del evento no se muestra.
        Assert.DoesNotContain("internal detail", row.FallbackBody);
        Assert.Equal($"signature.preparer_attention_required:{RequestId:N}:{reason}", row.GroupKey);

        var render = Assert.Single(harness.Scribe.Calls);
        Assert.Equal(SignaturePreparerAttentionRequiredConsumer.EmailEventKey, render.EventKey);
        Assert.Equal(reason, render.Variables["reason"]);
        Assert.Equal("Smith 1040", render.Variables["request_title"]);
        Assert.Equal($"https://manfer.taxoffix.io/signature/requests/{RequestId:D}", render.Variables["request_url"]);
    }

    [Fact]
    public async Task Un_reintento_no_repite_correo_ni_push()
    {
        var harness = Harness(active: [Author]);
        var evt = Event(SignaturePreparerAttentionReasons.PreparerCleared, recipient: Author);

        await HandleAsync(harness, evt);
        await HandleAsync(harness, evt);

        Assert.Single(harness.Postmaster);
        Assert.Single(harness.Pushes);
    }

    [Fact]
    public async Task Republicado_con_otro_EventId_no_repite_correo_ni_push()
    {
        var harness = Harness(active: [SignatureAdmin]);
        var evt = Event(SignaturePreparerAttentionReasons.UnsentRequestKept, recipient: null);

        await HandleAsync(harness, evt);
        await HandleAsync(harness, evt with { EventId = Guid.NewGuid() });

        Assert.Single(harness.Postmaster);
        Assert.Single(harness.Pushes);
    }

    [Fact]
    public async Task Otro_motivo_sobre_la_misma_solicitud_si_avisa()
    {
        var harness = Harness(active: [Author]);

        await HandleAsync(harness, Event(SignaturePreparerAttentionReasons.PreparerCleared, recipient: Author));
        await HandleAsync(harness, Event(SignaturePreparerAttentionReasons.PreparerChangeBlocked, recipient: Author));

        Assert.Equal(2, harness.Postmaster.Count);
        Assert.Equal(2, harness.Pushes.Count);
    }

    private static OffboardingNoticeHarness Harness(IReadOnlyList<Guid> active, string? host = null) =>
        new(
            active: active,
            byPermission: new Dictionary<string, IReadOnlyList<Guid>>
            {
                ["signature.request.manage"] = [SignatureAdmin, Leaver],
            },
            host: host
        );

    private static SignaturePreparerAttentionRequiredIntegrationEvent Event(string reason, Guid? recipient) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "corr-signature",
            SignatureRequestId = RequestId,
            Title = "Smith 1040",
            Status = "InProgress",
            Reason = reason,
            Detail = "internal detail",
            OffboardedUserId = Leaver,
            CreatedByUserId = Author,
            RecipientUserId = recipient,
            AudiencePermission = "signature.request.manage",
        };

    private static Task HandleAsync(
        OffboardingNoticeHarness harness,
        SignaturePreparerAttentionRequiredIntegrationEvent evt
    ) =>
        SignaturePreparerAttentionRequiredConsumer.Handle(
            evt,
            harness.Sender,
            harness.Correlation,
            NullLogger<SignaturePreparerAttentionRequiredIntegrationEvent>.Instance,
            CancellationToken.None
        );
}
