using BuildingBlocks.Contracts.Files;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;
using static TaxVision.CloudStorage.Tests.Application.Library.FakeLibraryReadRepository;

namespace TaxVision.CloudStorage.Tests.Application.Library;

public sealed class LibraryPageAssemblerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private static readonly LibrarySource[] Everything =
    [
        LibrarySource.Documents,
        LibrarySource.Signature,
        LibrarySource.Billing,
        LibrarySource.Financial,
        LibrarySource.Tasks,
        LibrarySource.Forms,
        LibrarySource.Mail,
    ];

    private readonly FakeLibraryReadRepository _repository = new();
    private readonly FakeFileOwnerVisibilityClient _owners = new();
    private readonly SteppingClock _clock = new(Now);
    private int _minute;

    [Fact]
    public async Task Sql_sources_alone_never_call_an_owner()
    {
        Doc();
        Doc();

        var page = await Read(sources: [LibrarySource.Documents]);

        Assert.Equal(2, page.Items.Count);
        Assert.Empty(_owners.Calls);
        Assert.Empty(page.UnavailableSources);
    }

    [Fact]
    public async Task An_unconfirmed_row_is_left_out()
    {
        var doc = Doc();
        Task();

        var page = await Read();

        Assert.Equal([doc.Id], page.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task A_confirmed_row_carries_the_owner_open_in_and_title()
    {
        var customer = Guid.NewGuid();
        var thread = Guid.NewGuid();
        var row = Mail(customer);
        var context = new Dictionary<string, string> { ["messageId"] = "m1", ["accountId"] = "a1" };
        _owners.Confirm(new VisibleFile(row.Id, "email_thread", thread, "Your engagement letter", customer, context));

        var item = Assert.Single((await Read()).Items);

        Assert.Equal("mail", item.Source);
        Assert.Equal("email_attachment", item.Kind);
        Assert.Equal("Your engagement letter", item.ResourceTitle);
        Assert.Equal(customer, item.CustomerId);
        Assert.Equal("email_thread", item.OpenIn.ResourceType);
        Assert.Equal(thread, item.OpenIn.ResourceId);
        Assert.Equal(customer, item.OpenIn.CustomerId);
        Assert.Equal(context, item.OpenIn.Context);
        Assert.Null(item.FolderPath);
    }

    [Fact]
    public async Task Mixed_rows_keep_the_sql_order()
    {
        var first = Doc();
        var second = Task();
        var third = Doc();
        var fourth = Form();
        _owners.Confirm(second.Id).Confirm(fourth.Id);

        var page = await Read();

        Assert.Equal([first.Id, second.Id, third.Id, fourth.Id], page.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task A_second_tranche_fills_the_page()
    {
        Task();
        var doc1 = Doc();
        Task();
        var doc2 = Doc();
        var doc3 = Doc();

        var page = await Read(take: 3, window: 3);

        Assert.Equal([doc1.Id, doc2.Id, doc3.Id], page.Items.Select(item => item.Id));
        Assert.Equal(2, _repository.Calls);
        Assert.Equal([3, 3], _repository.RequestedTakes);
    }

    [Fact]
    public async Task The_round_cap_returns_a_short_page_with_a_cursor()
    {
        var tasks = Enumerable.Range(0, 20).Select(_ => Task()).ToList();

        var page = await Read(take: 5, window: 2, maxRounds: 4);

        Assert.Empty(page.Items);
        Assert.Equal(4, _repository.Calls);
        Assert.NotNull(page.NextCursor);
        // El cursor queda en la última fila examinada, aunque se descartara.
        Assert.Equal(tasks[7].Id, LibraryCursor.Decode(page.NextCursor, LibrarySort.Newest).Value.Id);
    }

    [Fact]
    public async Task Pages_neither_repeat_nor_skip_rows()
    {
        var expected = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            var row = i % 3 == 0 ? Task() : Doc();
            if (i % 3 != 0 || i % 2 == 0)
                expected.Add(row.Id);
            if (i % 3 == 0 && i % 2 == 0)
                _owners.Confirm(row.Id);
        }

        var seen = new List<Guid>();
        string? cursor = null;
        var guard = 0;
        do
        {
            var page = await Read(take: 2, window: 3, cursor: cursor);
            seen.AddRange(page.Items.Select(item => item.Id));
            cursor = page.NextCursor;
        } while (cursor is not null && ++guard < 50);

        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task An_unavailable_owner_is_left_out_for_the_rest_of_the_request()
    {
        var mail = Mail(Guid.NewGuid());
        var doc1 = Doc();
        Mail(Guid.NewGuid());
        var doc2 = Doc();
        var doc3 = Doc();
        _owners.Down(FileOwnerService.Correspondence);

        var page = await Read(take: 3, window: 2);

        Assert.Equal([doc1.Id, doc2.Id, doc3.Id], page.Items.Select(item => item.Id));
        Assert.DoesNotContain(mail.Id, page.Items.Select(item => item.Id));
        Assert.Equal(["mail"], page.UnavailableSources);
        Assert.Single(_owners.Calls, call => call.Owner == FileOwnerService.Correspondence);
        Assert.True(_repository.RequestedScopes[0].Includes(LibrarySource.Mail));
        Assert.All(_repository.RequestedScopes.Skip(1), scope => Assert.False(scope.Includes(LibrarySource.Mail)));
    }

    [Fact]
    public async Task With_only_an_unavailable_owner_requested_the_page_ends()
    {
        Task();
        Task();
        Task();
        _owners.Down(FileOwnerService.Tasks);

        var page = await Read(sources: [LibrarySource.Tasks], take: 5, window: 2);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
        Assert.Equal(["tasks"], page.UnavailableSources);
        Assert.Equal(1, _repository.Calls);
    }

    [Fact]
    public async Task A_spent_owner_budget_cuts_the_loop_and_returns_a_cursor()
    {
        for (var i = 0; i < 10; i++)
            Task();
        _owners.OnCall = () => _clock.Advance(TimeSpan.FromSeconds(4));

        var page = await Read(take: 5, window: 2, maxRounds: 4, budgetSeconds: 6);

        Assert.Empty(page.Items);
        Assert.Equal(2, _repository.Calls);
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public async Task One_call_per_owner_and_tranche_with_tasks_and_forms_together()
    {
        var task = Task();
        var form = Form();
        var mail = Mail(Guid.NewGuid());

        await Read();

        Assert.Equal(2, _owners.Calls.Count);
        var tasks = Assert.Single(_owners.Calls, call => call.Owner == FileOwnerService.Tasks);
        Assert.Equal([task.Id, form.Id], tasks.FileIds);
        Assert.Equal(FileVisibilityPurposes.List, tasks.Purpose);
        var correspondence = Assert.Single(_owners.Calls, call => call.Owner == FileOwnerService.Correspondence);
        Assert.Equal([mail.Id], correspondence.FileIds);
    }

    [Fact]
    public async Task A_full_page_of_sql_rows_still_reads_one_row_ahead()
    {
        Doc();
        Doc();
        Doc();

        var page = await Read(sources: [LibrarySource.Documents], take: 2);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal([3], _repository.RequestedTakes);
        Assert.NotNull(page.NextCursor);
    }

    private LibraryRow Doc() => Seed(Row(OwnerType.Tenant, FolderType.Documents, createdAtUtc: Next()));

    private LibraryRow Task() =>
        Seed(Row(OwnerType.User, FolderType.Tasks, Guid.NewGuid(), "task", "attachment", createdAtUtc: Next()));

    private LibraryRow Form() =>
        Seed(
            Row(
                OwnerType.Customer,
                FolderType.ClientForms,
                Guid.NewGuid(),
                "form_request",
                "attachment",
                createdAtUtc: Next()
            )
        );

    private LibraryRow Mail(Guid customer) =>
        Seed(
            Row(
                OwnerType.Customer,
                FolderType.EmailOutgoing,
                customer,
                "email_message",
                "outgoing_attachment",
                createdAtUtc: Next()
            )
        );

    // Cada fila nueva es más antigua: el orden newest es el de siembra.
    private DateTime Next() => Now.AddMinutes(-++_minute);

    private LibraryRow Seed(LibraryRow row)
    {
        _repository.Add(Tenant, row);
        return row;
    }

    private Task<LibraryPage> Read(
        LibrarySource[]? sources = null,
        int take = 50,
        int window = 100,
        int maxRounds = 4,
        int budgetSeconds = 6,
        string? cursor = null
    )
    {
        var options = new LibraryOwnerOptions
        {
            Window = window,
            MaxRounds = maxRounds,
            OwnerBudgetSeconds = budgetSeconds,
        };
        var filter = LibraryFilter.Create(new LibrarySearchRequest { Take = take, Cursor = cursor }).Value;
        var access = LibraryAccessPolicyTests.Access(readable: Everything, viewAll: true);
        return LibraryPageAssembler.ReadAsync(
            new LibraryPageDependencies(_repository, _owners, options, _clock),
            Tenant,
            access,
            sources ?? Everything,
            filter,
            CancellationToken.None
        );
    }
}
