using BuildingBlocks.TimeZones;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Analytics;
using TaxVision.Signature.Application.Requests.Queries.Summary;

namespace TaxVision.Signature.Tests.Requests;

/// <summary>Resumen del widget de firmas: recorte por asignación, fuente del período y rango.</summary>
public sealed class SignatureRequestsSummaryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);

    private readonly FakeReader _reader = new();
    private readonly FakeAnalytics _analytics = new();

    [Fact]
    public async Task An_employee_without_view_all_is_scoped_to_their_assignments()
    {
        var result = await HandleAsync(canViewAll: false, flagOn: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(Actor, _reader.Last!.AssignedTo);
        Assert.Null(_analytics.Last);
        Assert.Equal((1, 2, 3), (result.Value.Sent, result.Value.Completed, result.Value.Canceled));
        Assert.Equal(
            (4, 5, 6, 7),
            (result.Value.Draft, result.Value.InProgress, result.Value.ExpiringWithin48h, result.Value.Expired)
        );
        Assert.Equal(12.5, result.Value.AvgMinutesToSign);
    }

    [Fact]
    public async Task View_all_reads_the_office_and_takes_the_period_from_the_snapshot()
    {
        var result = await HandleAsync(canViewAll: true, flagOn: true);

        Assert.True(result.IsSuccess);
        Assert.Null(_reader.Last!.AssignedTo);
        Assert.NotNull(_analytics.Last);
        Assert.Equal(
            (11, 9, 2, 1),
            (result.Value.Sent, result.Value.Completed, result.Value.Expired, result.Value.Canceled)
        );
        Assert.Equal((4, 5, 6), (result.Value.Draft, result.Value.InProgress, result.Value.ExpiringWithin48h));
    }

    [Fact]
    public async Task Flag_off_reads_the_whole_office()
    {
        var result = await HandleAsync(canViewAll: false, flagOn: false);

        Assert.True(result.IsSuccess);
        Assert.Null(_reader.Last!.AssignedTo);
    }

    [Fact]
    public async Task Defaults_to_the_office_month_to_date()
    {
        var result = await HandleAsync(canViewAll: true, flagOn: true, zone: "Asia/Tokyo");

        // 15:00 UTC del 21 ya es el 22 en Tokio.
        Assert.Equal(new DateOnly(2026, 11, 1), result.Value.From);
        Assert.Equal(new DateOnly(2026, 11, 22), result.Value.To);
        Assert.Equal(new DateTime(2026, 10, 31, 15, 0, 0, DateTimeKind.Utc), _reader.Last!.FromUtc);
        Assert.Equal(new DateTime(2026, 11, 22, 15, 0, 0, DateTimeKind.Utc), _reader.Last.ToUtc);
        Assert.Equal(Now, _reader.Last.NowUtc);
    }

    [Fact]
    public async Task Office_without_users_work_view_is_forbidden()
    {
        var result = await HandleAsync(true, true, scope: DashboardScope.Office, canViewOffice: false);

        Assert.True(result.IsFailure);
        Assert.Equal(SignatureRequestsSummaryErrors.OfficeForbidden, result.Error);
        Assert.Null(_reader.Last);
    }

    [Fact]
    public async Task Office_without_view_all_keeps_the_assignment_cut()
    {
        var result = await HandleAsync(false, true, scope: DashboardScope.Office, canViewOffice: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(Actor, _reader.Last!.AssignedTo);
        Assert.Null(_reader.Last.CreatedBy);
        Assert.Null(_analytics.Last);
    }

    [Fact]
    public async Task Office_with_view_all_reads_the_office_snapshot()
    {
        var result = await HandleAsync(true, true, scope: DashboardScope.Office, canViewOffice: true);

        Assert.True(result.IsSuccess);
        Assert.Null(_reader.Last!.AssignedTo);
        Assert.NotNull(_analytics.Last);
    }

    [Fact]
    public async Task Me_counts_only_what_the_actor_created_and_skips_the_snapshot()
    {
        var result = await HandleAsync(true, true, scope: DashboardScope.Me);

        Assert.True(result.IsSuccess);
        Assert.Equal(Actor, _reader.Last!.CreatedBy);
        Assert.Null(_reader.Last.AssignedTo);
        Assert.Null(_analytics.Last);
        Assert.Equal((1, 2, 3), (result.Value.Sent, result.Value.Completed, result.Value.Canceled));
    }

    [Theory]
    [InlineData(null, DashboardScope.Default)]
    [InlineData("ME", DashboardScope.Me)]
    [InlineData("office", DashboardScope.Office)]
    public void Scope_parses_only_me_or_office(string? raw, DashboardScope expected)
    {
        Assert.True(DashboardScopes.TryParse(raw, out var scope));
        Assert.Equal(expected, scope);
        Assert.False(DashboardScopes.TryParse("all", out _));
    }

    [Theory]
    [InlineData("2026-11-10", "2026-11-09")]
    [InlineData("2025-01-01", "2026-01-02")]
    public async Task An_inverted_or_too_long_range_is_rejected(string from, string to)
    {
        var result = await HandleAsync(true, true, from: DateOnly.Parse(from), to: DateOnly.Parse(to));

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Summary.RangeInvalid", result.Error.Code);
        Assert.Null(_reader.Last);
    }

    private Task<BuildingBlocks.Results.Result<SignatureRequestsSummaryResponse>> HandleAsync(
        bool canViewAll,
        bool flagOn,
        string zone = IanaTimeZone.UtcId,
        DateOnly? from = null,
        DateOnly? to = null,
        DashboardScope scope = DashboardScope.Default,
        bool canViewOffice = false
    ) =>
        GetSignatureRequestsSummaryHandler.Handle(
            new GetSignatureRequestsSummaryQuery(TenantId, Actor, canViewAll, from, to, Now, scope, canViewOffice),
            _reader,
            _analytics,
            new FixedZone(zone),
            Options.Create(new SignatureVisibilityOptions { Enabled = flagOn }),
            CancellationToken.None
        );

    private sealed class FakeReader : ISignatureRequestsSummaryReader
    {
        public SignatureRequestsSummaryFilter? Last { get; private set; }

        public Task<SignatureRequestsLiveCounts> ReadAsync(SignatureRequestsSummaryFilter filter, CancellationToken ct)
        {
            Last = filter;
            return Task.FromResult(new SignatureRequestsLiveCounts(4, 5, 6, 1, 2, 7, 3, 12.5));
        }
    }

    private sealed class FakeAnalytics : ISignatureAnalyticsReadService
    {
        public SignatureAnalyticsSummaryQuery? Last { get; private set; }

        public Task<SignatureAnalyticsSummary> GetSummaryAsync(
            SignatureAnalyticsSummaryQuery query,
            CancellationToken ct
        )
        {
            Last = query;
            return Task.FromResult(
                new SignatureAnalyticsSummary(
                    query.TenantId,
                    query.FromDay,
                    query.ToDay,
                    12,
                    11,
                    1,
                    2,
                    9,
                    9,
                    9,
                    0,
                    0,
                    0
                )
            );
        }

        public Task<SignatureAnalyticsTimeline> GetTimelineAsync(
            SignatureAnalyticsTimelineQuery q,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<SignatureAnalyticsByCategory> GetByCategoryAsync(
            SignatureAnalyticsByCategoryQuery q,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }

    private sealed class FixedZone(string zone) : IOfficeTimeZone
    {
        public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult(ResolvedTimeZone.Resolve(zone));
    }
}
