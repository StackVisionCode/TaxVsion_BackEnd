using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Consumers.Offboarding;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Offboarding;

/// <summary>Cambio de preparador de una solicitud enviada: aviso al nuevo preparador y al autor.</summary>
public sealed class SignaturePreparerChangedConsumerTests
{
    private static readonly Guid TenantId = OffboardingNoticeHarness.TenantId;
    private static readonly Guid Previous = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Author = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid NewPreparer = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Admin = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid RequestId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Por_retiro_avisa_al_nuevo_preparador_y_al_autor_con_textos_distintos()
    {
        var harness = Harness(host: "manfer.taxoffix.io");

        await HandleAsync(harness, Event(author: Author, changedBy: null, SignaturePreparerChangeReasons.Offboarding));

        var preparerRow = Assert.Single(harness.InAppRows, row => row.RecipientUserId == NewPreparer);
        Assert.Equal(NotificationKinds.SignaturePreparerChanged, preparerRow.Kind);
        Assert.Equal("You're now the preparer for “Smith 1040”", preparerRow.FallbackTitle);
        Assert.StartsWith("The previous preparer is no longer with your office.", preparerRow.FallbackBody);
        Assert.Equal(RequestId.ToString(), preparerRow.ResourceId);

        var authorRow = Assert.Single(harness.InAppRows, row => row.RecipientUserId == Author);
        Assert.Equal("New preparer for “Smith 1040”", authorRow.FallbackTitle);
        Assert.Contains("Dana Preparer is now the preparer", authorRow.FallbackBody);

        Assert.Equal(2, harness.Scribe.Calls.Count);
        Assert.Contains(harness.Scribe.Calls, call => (bool)call.Variables["is_new_preparer"]!);
        Assert.Contains(harness.Scribe.Calls, call => !(bool)call.Variables["is_new_preparer"]!);
        Assert.All(harness.Scribe.Calls, call => Assert.True((bool)call.Variables["is_offboarding"]!));
        Assert.All(
            harness.Scribe.Calls,
            call =>
                Assert.Equal(
                    $"https://manfer.taxoffix.io/signature/requests/{RequestId:D}",
                    call.Variables["request_url"]
                )
        );
        Assert.Equal(
            new[] { NewPreparer, Author }.Select(OffboardingNoticeHarness.EmailOf).OrderBy(x => x),
            harness.Postmaster.Select(e => e.To).OrderBy(x => x)
        );
    }

    [Fact]
    public async Task Si_el_autor_es_el_nuevo_preparador_recibe_un_solo_aviso()
    {
        var harness = Harness();

        await HandleAsync(
            harness,
            Event(author: NewPreparer, changedBy: null, SignaturePreparerChangeReasons.Offboarding)
        );

        Assert.Equal(new[] { NewPreparer }, harness.InAppRecipients);
        Assert.Single(harness.Scribe.Calls);
    }

    [Fact]
    public async Task Cambio_manual_no_avisa_a_quien_lo_hizo_y_no_menciona_el_retiro()
    {
        var harness = Harness();

        await HandleAsync(harness, Event(author: Admin, changedBy: Admin, SignaturePreparerChangeReasons.Manual));

        var row = Assert.Single(harness.InAppRows);
        Assert.Equal(NewPreparer, row.RecipientUserId);
        Assert.DoesNotContain("no longer with your office", row.FallbackBody);
        Assert.False((bool)Assert.Single(harness.Scribe.Calls).Variables["is_offboarding"]!);
    }

    [Fact]
    public async Task Un_reintento_no_repite_correo_ni_push()
    {
        var harness = Harness();
        var evt = Event(author: Author, changedBy: null, SignaturePreparerChangeReasons.Offboarding);

        await HandleAsync(harness, evt);
        await HandleAsync(harness, evt);

        Assert.Equal(2, harness.Postmaster.Count);
        Assert.Equal(2, harness.Pushes.Count);
    }

    private static OffboardingNoticeHarness Harness(string? host = null) =>
        new(active: [Author, NewPreparer, Admin], host: host);

    private static SignaturePreparerChangedIntegrationEvent Event(Guid author, Guid? changedBy, string reason) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "corr-preparer",
            SignatureRequestId = RequestId,
            Title = "Smith 1040",
            CreatedByUserId = author,
            PreviousPreparerUserId = Previous,
            NewPreparerUserId = NewPreparer,
            NewPreparerDisplayName = "Dana Preparer",
            ChangedByUserId = changedBy,
            Reason = reason,
            ChangedAtUtc = new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc),
        };

    private static Task HandleAsync(OffboardingNoticeHarness harness, SignaturePreparerChangedIntegrationEvent evt) =>
        SignaturePreparerChangedConsumer.Handle(
            evt,
            harness.Sender,
            harness.Correlation,
            NullLogger<SignaturePreparerChangedIntegrationEvent>.Instance,
            CancellationToken.None
        );
}
