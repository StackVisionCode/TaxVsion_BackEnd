using System.Security.Claims;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Api.Controllers;
using TaxVision.Correspondence.Application.Work;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using TaxVision.Correspondence.Tests.Api;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Work;

// Títulos en vivo del historial: asunto de correos enviados y de hilos, solo los que quien mira puede abrir.
public sealed class CorrespondenceWorkTitlesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();

    private static async Task<CorrespondenceDbContext> SeedAsync(params object[] entities)
    {
        var db = NewContext(Tenant);
        db.AddRange(entities);
        await db.SaveChangesAsync();
        return db;
    }

    private static Task<WorkTitlesResponse> Resolve(
        CorrespondenceDbContext db,
        IReadOnlyCollection<Guid>? visible,
        Guid? restrictTo,
        params WorkTitleRef[] refs
    ) =>
        GetWorkTitlesHandler.Handle(
            new GetWorkTitlesQuery(Tenant, refs, visible, restrictTo),
            new MailTitleReader(db),
            CancellationToken.None
        );

    private static WorkTitleRef Message(Guid id) => new(WorkResourceTypes.EmailMessage, id);

    private static WorkTitleRef ThreadRef(Guid id) => new(WorkResourceTypes.EmailThread, id);

    [Fact]
    public async Task Sent_emails_and_threads_resolve_to_their_subjects()
    {
        var thread = Thread(Tenant, Guid.NewGuid(), "W-2 for 2026");
        var email = Incoming(Tenant, thread, Account);
        var sent = Sent(Tenant, null, Account, "Engagement letter", "ana@client.com");
        await using var db = await SeedAsync(thread, email, sent);

        var result = await Resolve(db, null, null, Message(sent.Id), ThreadRef(thread.Id));

        Assert.Equal(
            [
                new WorkTitle(WorkResourceTypes.EmailMessage, sent.Id, "Engagement letter"),
                new WorkTitle(WorkResourceTypes.EmailThread, thread.Id, "W-2 for 2026"),
            ],
            result.Items
        );
    }

    [Fact]
    public async Task Mail_of_a_customer_not_assigned_to_the_viewer_is_omitted()
    {
        var preparer = Guid.NewGuid();
        var assigned = Guid.NewGuid();
        var mineThread = Thread(Tenant, assigned, "Mine");
        var otherThread = Thread(Tenant, Guid.NewGuid(), "Other");
        var mineSent = Sent(Tenant, assigned, Account, "Mine sent", "a@b.com");
        var otherSent = Sent(Tenant, Guid.NewGuid(), Account, "Other sent", "a@b.com");
        await using var db = await SeedAsync(
            mineThread,
            otherThread,
            Incoming(Tenant, mineThread, Account),
            Incoming(Tenant, otherThread, Account),
            mineSent,
            otherSent,
            CustomerAssignmentProjection.Create(Tenant, assigned, preparer, DateTime.UtcNow)
        );

        var result = await Resolve(
            db,
            null,
            preparer,
            ThreadRef(mineThread.Id),
            ThreadRef(otherThread.Id),
            Message(mineSent.Id),
            Message(otherSent.Id)
        );

        Assert.Equal(new HashSet<Guid> { mineThread.Id, mineSent.Id }, result.Items.Select(t => t.Id).ToHashSet());
    }

    [Fact]
    public async Task Mail_from_a_mailbox_the_viewer_cannot_see_is_omitted()
    {
        var office = Guid.NewGuid();
        var thread = Thread(Tenant, Guid.NewGuid(), "Payroll");
        var sent = Sent(Tenant, null, office, "Payroll out", "a@b.com");
        await using var db = await SeedAsync(thread, Incoming(Tenant, thread, office), sent);

        Assert.Empty((await Resolve(db, [Account], null, ThreadRef(thread.Id), Message(sent.Id))).Items);
        Assert.Equal(2, (await Resolve(db, [office], null, ThreadRef(thread.Id), Message(sent.Id))).Items.Count);
    }

    [Fact]
    public async Task Trashed_unsent_missing_and_foreign_tenant_mail_is_omitted()
    {
        var trashed = Sent(Tenant, null, Account, "Trashed", "a@b.com");
        trashed.SoftDelete(DateTime.UtcNow);
        var open = Open(Tenant, null, Account, "Still a draft", "a@b.com");
        var foreign = Sent(Guid.NewGuid(), null, Account, "Other office", "a@b.com");
        await using var db = await SeedAsync(trashed, open, foreign);

        var result = await Resolve(
            db,
            null,
            null,
            Message(trashed.Id),
            Message(open.Id),
            Message(foreign.Id),
            Message(Guid.NewGuid()),
            ThreadRef(Guid.NewGuid())
        );

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Foreign_resource_types_are_ignored()
    {
        var sent = Sent(Tenant, null, Account, "Hello", "a@b.com");
        await using var db = await SeedAsync(sent);

        var result = await Resolve(db, null, null, new WorkTitleRef(WorkResourceTypes.Task, sent.Id), Message(sent.Id));

        Assert.Equal(WorkResourceTypes.EmailMessage, Assert.Single(result.Items).ResourceType);
    }

    [Fact]
    public async Task Subjects_are_clipped_to_one_line_and_blank_ones_are_omitted()
    {
        var longSubject = "Re:   quarterly\n" + new string('x', 200);
        var sent = Sent(Tenant, null, Account, longSubject, "a@b.com");
        var blank = Sent(Tenant, null, Account, "   ", "a@b.com");
        await using var db = await SeedAsync(sent, blank);

        var title = Assert.Single((await Resolve(db, null, null, Message(sent.Id), Message(blank.Id))).Items).Title;

        Assert.Equal(WorkLimits.TitleMaxLength, title.Length);
        Assert.StartsWith("Re: quarterly x", title);
        Assert.EndsWith("…", title);
    }

    private static (CorrespondenceWorkController Controller, ControllableMessageBus Bus) Controller(
        IReadOnlyCollection<Guid>? visible = null,
        Guid? restrictTo = null
    )
    {
        var bus = new ControllableMessageBus { NextResult = new WorkTitlesResponse([]) };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("tenant_id", Tenant.ToString()), new Claim("sub", Guid.NewGuid().ToString())],
                "Test"
            )
        );
        var controller = new CorrespondenceWorkController(
            bus,
            new FakeMailboxVisibilityResolver { Visible = visible },
            new FakeCustomerAccessResolver { RestrictTo = restrictTo }
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };
        return (controller, bus);
    }

    [Fact]
    public async Task More_than_the_batch_limit_is_rejected()
    {
        var (controller, bus) = Controller();
        var refs = Enumerable.Range(0, WorkLimits.TitleBatch + 1).Select(_ => Message(Guid.NewGuid())).ToList();

        var result = await controller.Titles(new WorkTitlesRequest(refs), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Correspondence.Work.Titles", Assert.IsType<Error>(bad.Value).Code);
        Assert.Null(bus.LastInvoked);
    }

    [Fact]
    public async Task The_query_carries_the_viewer_mail_visibility()
    {
        var restrictTo = Guid.NewGuid();
        var (controller, bus) = Controller([Account], restrictTo);

        var result = await controller.Titles(new WorkTitlesRequest([Message(Guid.NewGuid())]), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var query = Assert.IsType<GetWorkTitlesQuery>(bus.LastInvoked);
        Assert.Equal(Tenant, query.TenantId);
        Assert.Equal([Account], query.VisibleAccountIds);
        Assert.Equal(restrictTo, query.RestrictToUserId);
    }
}
