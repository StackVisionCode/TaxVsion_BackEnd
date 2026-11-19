using BuildingBlocks.Contracts.Work;
using BuildingBlocks.TimeZones;
using TaxVision.Signature.Application.Requests.Queries.Work;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Tests.Application;

/// <summary>Work Profile: qué cuenta como abierto, en espera, vencido y próximo en firmas, con la zona de la oficina.</summary>
public sealed class SignatureWorkTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    // 10:00 en Nueva York (EST, UTC-5) del 2026-11-18.
    private static readonly DateTime Now = new(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");

    [Fact]
    public void Open_is_waiting_for_the_preparer_signature_and_waiting_is_sent_by_the_user()
    {
        var rows = new[]
        {
            Row(author: Other, preparer: User),
            Row(author: User, preparer: Other),
            Row(author: User, preparer: User), // le toca firmar: cuenta como abierta, no en espera
            Row(author: Other, preparer: User, preparerSigned: true), // ya firmó
            Row(author: Other, preparer: Other), // ajena
        };

        var count = Summarize(rows).Counts.Single();

        Assert.Equal(WorkKinds.Signatures, count.Kind);
        Assert.Equal(2, count.Open);
        Assert.Equal(1, count.Waiting);
    }

    [Fact]
    public void Overdue_counts_past_expiration_and_recently_expired_without_completion()
    {
        var rows = new[]
        {
            Row(author: User, preparer: null, expiresAt: Now.AddMinutes(-5)),
            Row(author: User, preparer: null, status: SignatureRequestStatus.Expired, expiresAt: Now.AddDays(-2)),
            Row(author: User, preparer: null, expiresAt: Now.AddDays(10)),
        };

        var count = Summarize(rows).Counts.Single();

        Assert.Equal(2, count.Overdue);
        Assert.Equal(2, count.Waiting); // la expirada ya no está en espera
    }

    [Fact]
    public void Due_soon_ends_at_the_office_midnight_seven_days_ahead()
    {
        // 2026-11-24 23:30 en Nueva York: dentro de la ventana; 2026-11-25 00:30: fuera.
        var inside = Row(
            author: User,
            preparer: null,
            expiresAt: new DateTime(2026, 11, 25, 4, 30, 0, DateTimeKind.Utc)
        );
        var outside = Row(
            author: User,
            preparer: null,
            expiresAt: new DateTime(2026, 11, 25, 5, 30, 0, DateTimeKind.Utc)
        );

        var office = Summarize([inside, outside], NewYork).Counts.Single();
        var utc = Summarize([inside, outside], ResolvedTimeZone.Utc).Counts.Single();

        Assert.Equal(1, office.DueSoon);
        Assert.Equal(0, utc.DueSoon);
    }

    [Fact]
    public void Top_has_the_five_most_urgent_with_the_crm_link_and_no_signer_data()
    {
        var expired = Row(
            author: User,
            preparer: null,
            status: SignatureRequestStatus.Expired,
            expiresAt: Now.AddDays(-1)
        );
        var soon = Row(author: User, preparer: null, expiresAt: Now.AddDays(1));
        var later = Enumerable
            .Range(2, 5)
            .Select(day => Row(author: User, preparer: null, expiresAt: Now.AddDays(day)));
        var noExpiry = Row(author: Other, preparer: User, noExpiry: true);

        var top = Summarize([noExpiry, .. later, soon, expired]).Top;

        Assert.Equal(WorkLimits.TopItems, top.Count);
        Assert.Equal(expired.Id, top[0].Id);
        Assert.Equal(soon.Id, top[1].Id);
        Assert.DoesNotContain(top, item => item.Id == noExpiry.Id);
        Assert.Equal($"/signature/requests/{expired.Id}", top[0].Link);
        Assert.Equal("expired", top[0].Status);
        Assert.Equal("awaiting-signers", top[1].Status);
        Assert.Equal(soon.CustomerId, top[1].CustomerId);
        Assert.Equal(soon.ExpiresAtUtc, top[1].DueAtUtc);
        Assert.All(top, item => Assert.Equal(WorkKinds.Signatures, item.Kind));
    }

    [Fact]
    public void Summary_names_the_provider_and_the_module()
    {
        var summary = Summarize([]);

        Assert.Equal("signature", summary.Provider);
        Assert.Equal("signatures", summary.Module);
        Assert.Equal(new WorkCount(WorkKinds.Signatures, 0, 0, 0, 0), summary.Counts.Single());
        Assert.Empty(summary.Top);
    }

    [Fact]
    public void Batch_has_one_row_per_requested_user_with_open_work_and_overdue()
    {
        var third = Guid.NewGuid();
        var rows = new[]
        {
            Row(author: Other, preparer: User),
            Row(author: User, preparer: Other, expiresAt: Now.AddHours(-1)),
            Row(author: Other, preparer: null, status: SignatureRequestStatus.Expired),
        };

        var batch = SignatureWork.Batch([User, Other, third], rows, Window(NewYork));

        Assert.Equal(new WorkBatchRow(User, 2, 1), batch.Rows.Single(r => r.UserId == User));
        // Other: espera en la primera, firma pendiente en la segunda (vencida) y la expirada.
        Assert.Equal(new WorkBatchRow(Other, 2, 2), batch.Rows.Single(r => r.UserId == Other));
        Assert.Equal(new WorkBatchRow(third, 0, 0), batch.Rows.Single(r => r.UserId == third));
    }

    [Fact]
    public void Items_cover_the_top_universe_open_waiting_and_recently_expired()
    {
        var rows = new[]
        {
            Row(author: Other, preparer: User),
            Row(author: User, preparer: User),
            Row(author: User, preparer: Other), // en espera
            Row(author: User, preparer: null, status: SignatureRequestStatus.Expired),
            Row(author: Other, preparer: User, preparerSigned: true), // ya firmó: fuera
            Row(author: Other, preparer: Other), // ajena: fuera
        };

        var page = SignatureWork.Items(User, rows, Window(NewYork), page: 1, size: 20);

        Assert.Equal(4, page.TotalCount);
        Assert.Equal(4, page.Items.Count);
        Assert.Equal(
            ["awaiting-preparer", "awaiting-preparer", "awaiting-signers", "expired"],
            page.Items.Select(item => item.Status).Order(StringComparer.Ordinal)
        );
    }

    [Fact]
    public void Every_top_item_appears_on_the_first_page_in_the_same_order()
    {
        var rows = new[]
        {
            Row(author: User, preparer: null, status: SignatureRequestStatus.Expired, expiresAt: Now.AddDays(-2)),
            Row(author: User, preparer: Other, expiresAt: Now.AddDays(1)),
            Row(author: Other, preparer: User, expiresAt: Now.AddDays(3)),
            Row(author: Other, preparer: User, noExpiry: true),
            Row(author: User, preparer: Other, expiresAt: Now.AddDays(15)),
            Row(author: Other, preparer: User, expiresAt: Now.AddHours(-3)),
            Row(author: User, preparer: Other, noExpiry: true),
            Row(author: Other, preparer: User, expiresAt: Now.AddDays(25)),
        };

        var top = Summarize(rows).Top;
        var page = SignatureWork.Items(User, rows, Window(NewYork), page: 1, size: 20);

        Assert.Equal(WorkLimits.TopItems, top.Count);
        Assert.Equal(rows.Length, page.TotalCount);
        Assert.Equal(top, page.Items.Take(top.Count));
    }

    [Fact]
    public void Items_follow_the_top_order_and_paginate()
    {
        var overdue = Row(author: Other, preparer: User, expiresAt: Now.AddHours(-1));
        var soon = Row(author: Other, preparer: User, expiresAt: Now.AddDays(2));
        var later = Row(author: Other, preparer: User, expiresAt: Now.AddDays(20));
        var noExpiry = Row(author: Other, preparer: User, noExpiry: true);
        var rows = new[] { noExpiry, later, soon, overdue };

        var first = SignatureWork.Items(User, rows, Window(NewYork), page: 1, size: 3);
        var second = SignatureWork.Items(User, rows, Window(NewYork), page: 2, size: 3);

        Assert.Equal([overdue.Id, soon.Id, later.Id], first.Items.Select(item => item.Id));
        Assert.Equal([noExpiry.Id], second.Items.Select(item => item.Id));
        Assert.Equal((1, 3, 4), (first.Page, first.Size, first.TotalCount));
        Assert.True(first.HasMore);
        Assert.Equal($"/signature/requests/{overdue.Id}", first.Items[0].Link);
    }

    [Fact]
    public void Titles_are_clipped_and_empty_titles_are_omitted()
    {
        var (named, blank, longOne) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var titles = SignatureWork.Titles([
            new SignatureTitleRow(named, "  Form   8879 "),
            new SignatureTitleRow(blank, "   "),
            new SignatureTitleRow(longOne, new string('a', 200)),
        ]);

        Assert.Equal(2, titles.Items.Count);
        Assert.Equal(new WorkTitle(WorkResourceTypes.SignatureRequest, named, "Form 8879"), titles.Items[0]);
        Assert.Equal(WorkLimits.TitleMaxLength, titles.Items[1].Title.Length);
        Assert.DoesNotContain(titles.Items, title => title.Id == blank);
    }

    // ----- helpers -----

    private static WorkSummaryResponse Summarize(IReadOnlyList<SignatureWorkRow> rows, ResolvedTimeZone? zone = null) =>
        SignatureWork.Summarize(User, rows, Window(zone ?? NewYork));

    private static SignatureWorkWindow Window(ResolvedTimeZone zone) => SignatureWorkWindow.For(Now, zone);

    private static SignatureWorkRow Row(
        Guid author,
        Guid? preparer,
        bool preparerSigned = false,
        SignatureRequestStatus status = SignatureRequestStatus.InProgress,
        DateTime? expiresAt = null,
        bool noExpiry = false
    ) =>
        new(
            Guid.NewGuid(),
            "Form 8879",
            status,
            author,
            preparer,
            preparerSigned,
            noExpiry ? null : expiresAt ?? Now.AddDays(30),
            Guid.NewGuid()
        );
}
