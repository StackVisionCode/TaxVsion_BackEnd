using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Api.Controllers;
using TaxVision.Correspondence.Application.Backfill;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Tests.Compose;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Backfill;

// Backfill de la biblioteca: re-declara los adjuntos de cada enviado; los borradores no.
public sealed class RedeclareFileSourcesTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _account = Guid.NewGuid();
    private readonly FakeDraftRepository _drafts = new();
    private readonly FakeIncomingEmailRepository _incoming = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeMessageBus _bus = new();

    private Draft AddSent(Guid? customerId, int attachments)
    {
        var files = Enumerable.Range(0, attachments).Select(i => File($"file-{i}.pdf")).ToArray();
        var draft = Sent(_tenantId, customerId, _account, "Subject", "ana@client.com", null, files);
        _drafts.AddAsync(draft).GetAwaiter().GetResult();
        return draft;
    }

    private Task<RedeclareFileSourcesReport> RunAsync(bool dryRun) =>
        RedeclareFileSourcesHandler.Handle(
            new RedeclareFileSourcesCommand(_tenantId, dryRun),
            _drafts,
            _incoming,
            new FakeOutboxUnit(_unitOfWork, _bus),
            NullLogger<RedeclareFileSourcesCommand>.Instance,
            CancellationToken.None
        );

    private IReadOnlyList<FileSourceDeclaredIntegrationEvent> Declared =>
        _bus.Published.OfType<FileSourceDeclaredIntegrationEvent>().ToList();

    private void SeedMixed()
    {
        AddSent(Guid.NewGuid(), attachments: 2);
        AddSent(null, attachments: 1);
        AddSent(Guid.NewGuid(), attachments: 0);
        var open = Open(_tenantId, null, _account, "Still a draft", "ana@client.com", null, File("draft.pdf"));
        _drafts.AddAsync(open).GetAwaiter().GetResult();
        var otherTenant = Sent(Guid.NewGuid(), null, _account, "Other", "a@b.com", null, File("other.pdf"));
        _drafts.AddAsync(otherTenant).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task DryRun_counts_without_publishing()
    {
        SeedMixed();

        var report = await RunAsync(dryRun: true);

        Assert.Equal(new RedeclareFileSourcesReport(true, 2, 3), report);
        Assert.Empty(_bus.Published);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Real_run_declares_one_source_per_attachment_of_each_sent_email()
    {
        SeedMixed();
        var sent = _drafts.All.Where(d => d.TenantId == _tenantId && d.Status == DraftStatus.Sent).ToList();
        var expected = sent.SelectMany(d => d.Attachments.Select(a => (a.FileId, d.Id, d.CustomerId))).ToList();

        var report = await RunAsync(dryRun: false);

        Assert.Equal(new RedeclareFileSourcesReport(false, 2, 3), report);
        Assert.Equivalent(
            expected,
            Declared.Select(e => (e.FileId, e.SourceResourceId, e.RelatedCustomerId)),
            strict: true
        );
        Assert.All(
            Declared,
            e =>
            {
                Assert.Equal(_tenantId, e.TenantId);
                Assert.Equal("email_message", e.SourceResourceType);
                Assert.Equal("outgoing_attachment", e.SourceRole);
            }
        );
    }

    [Fact]
    public async Task Real_run_commits_one_outbox_unit_per_page()
    {
        for (var i = 0; i < RedeclareFileSourcesHandler.PageSize + 1; i++)
            AddSent(null, attachments: 1);

        var report = await RunAsync(dryRun: false);

        Assert.Equal(RedeclareFileSourcesHandler.PageSize + 1, report.FilesDeclared);
        Assert.Equal(2, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Real_run_also_declares_downloaded_incoming_attachments()
    {
        var customer = Guid.NewGuid();
        var downloaded = Guid.NewGuid();
        var withCustomer = Files.MailFileTestData.Incoming(
            Files.MailFileTestData.Thread(_tenantId, customer),
            _account,
            downloaded
        );
        var withoutCustomerFile = Guid.NewGuid();
        var withoutCustomer = Files.MailFileTestData.Incoming(
            Files.MailFileTestData.Thread(_tenantId, null),
            _account,
            withoutCustomerFile
        );
        var notOpened = Files.MailFileTestData.Incoming(
            Files.MailFileTestData.Thread(_tenantId, customer),
            _account,
            null
        );
        var otherTenant = Files.MailFileTestData.Incoming(
            Files.MailFileTestData.Thread(Guid.NewGuid(), customer),
            _account,
            Guid.NewGuid()
        );
        foreach (var email in new[] { withCustomer, withoutCustomer, notOpened, otherTenant })
            await _incoming.AddAsync(email);

        var dry = await RunAsync(dryRun: true);
        var report = await RunAsync(dryRun: false);

        Assert.Equal(new RedeclareFileSourcesReport(true, 2, 2), dry);
        Assert.Equal(new RedeclareFileSourcesReport(false, 2, 2), report);
        Assert.Equivalent(
            new[]
            {
                (downloaded, withCustomer.Id, (Guid?)customer),
                (withoutCustomerFile, withoutCustomer.Id, (Guid?)null),
            },
            Declared.Select(e => (e.FileId, e.SourceResourceId, e.RelatedCustomerId)),
            strict: true
        );
        Assert.All(
            Declared,
            e =>
            {
                Assert.Equal("incoming_email", e.SourceResourceType);
                Assert.Equal("incoming_attachment", e.SourceRole);
            }
        );
    }

    private (CorrespondenceAdminController Controller, List<object> Invoked) Admin(ActorType actor, Guid tenant)
    {
        var invoked = new List<object>();
        var bus = new FakeMessageBus
        {
            OnInvoke = message =>
            {
                invoked.Add(message);
                var command = (RedeclareFileSourcesCommand)message;
                return Task.FromResult<object?>(new RedeclareFileSourcesReport(command.DryRun, 0, 0));
            },
        };
        var user = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim("sub", Guid.NewGuid().ToString()),
                    new Claim(ClaimNames.TenantId, tenant.ToString()),
                    new Claim(ClaimNames.ActorType, actor.ToString()),
                ],
                "test"
            )
        );
        var controller = new CorrespondenceAdminController(bus)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
        };
        return (controller, invoked);
    }

    [Fact]
    public async Task Admin_endpoint_runs_a_dry_run_by_default()
    {
        var (controller, invoked) = Admin(ActorType.PlatformAdmin, PlatformTenant.Id);

        var result = await controller.RedeclareFileSources(_tenantId, ct: CancellationToken.None);

        Assert.True(Assert.IsType<RedeclareFileSourcesReport>(Assert.IsType<OkObjectResult>(result).Value).DryRun);
        Assert.Equal(new RedeclareFileSourcesCommand(_tenantId, DryRun: true), Assert.Single(invoked));
    }

    [Fact]
    public async Task Admin_endpoint_rejects_a_service_token_of_another_tenant_and_a_missing_tenant()
    {
        var (foreign, foreignInvoked) = Admin(ActorType.Service, Guid.NewGuid());
        var (platform, platformInvoked) = Admin(ActorType.PlatformAdmin, PlatformTenant.Id);

        Assert.IsType<ForbidResult>(await foreign.RedeclareFileSources(_tenantId, false, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(
            await platform.RedeclareFileSources(Guid.Empty, ct: CancellationToken.None)
        );
        Assert.Empty(foreignInvoked);
        Assert.Empty(platformInvoked);
    }
}
