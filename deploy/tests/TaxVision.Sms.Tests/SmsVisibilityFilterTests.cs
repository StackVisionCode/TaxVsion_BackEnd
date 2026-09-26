using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Messages.Queries;
using TaxVision.Sms.Application.OptOut.Queries;

namespace TaxVision.Sms.Tests;

/// <summary>
/// Visibilidad por asignación (P2): los handlers de lectura solo restringen al actor cuando el flag está
/// encendido Y el actor NO ve todo (customers.view_all). En cualquier otro caso pasan null al read service
/// (= sin restricción), preservando el comportamiento previo hasta sembrar la proyección.
/// </summary>
public sealed class SmsVisibilityFilterTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    private static IOptions<SmsVisibilityOptions> Options(bool enabled) =>
        Microsoft.Extensions.Options.Options.Create(new SmsVisibilityOptions { Enabled = enabled });

    [Theory]
    [InlineData(true, false, true)] // flag ON + no ve todo  → restringe al actor
    [InlineData(true, true, false)] // flag ON + ve todo     → sin restricción
    [InlineData(false, false, false)] // flag OFF            → sin restricción
    [InlineData(false, true, false)] // flag OFF + ve todo   → sin restricción
    public async Task Search_restringe_al_actor_solo_con_flag_on_y_sin_view_all(
        bool enabled,
        bool canViewAll,
        bool shouldRestrict
    )
    {
        var reader = new CapturingReader();
        var query = new SearchSmsMessagesQuery(
            Tenant,
            CustomerId: null,
            SmsMessageStatusFilter.All,
            Term: null,
            FromUtc: null,
            ToUtc: null,
            SourceContext: "crm-sms",
            Page: 1,
            Size: 20,
            ActorUserId: Actor,
            CanViewAll: canViewAll
        );

        await SearchSmsMessagesHandler.Handle(query, reader, Options(enabled), CancellationToken.None);

        Assert.Equal(shouldRestrict ? Actor : (Guid?)null, reader.LastSearchAssignee);
    }

    [Fact]
    public async Task GetById_restringe_al_actor_con_flag_on_y_sin_view_all()
    {
        var reader = new CapturingReader();
        var query = new GetSmsMessageByIdQuery(Tenant, Guid.NewGuid(), Actor, CanViewAll: false);

        await GetSmsMessageByIdHandler.Handle(query, reader, Options(enabled: true), CancellationToken.None);

        Assert.Equal(Actor, reader.LastDetailAssignee);
    }

    [Fact]
    public async Task Stats_y_optouts_restringen_al_actor_con_flag_on_y_sin_view_all()
    {
        var reader = new CapturingReader();

        await GetSmsStatsHandler.Handle(
            new GetSmsStatsQuery(Tenant, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, "crm-sms", Actor, false),
            reader,
            Options(enabled: true),
            CancellationToken.None
        );
        await SearchSmsOptOutsHandler.Handle(
            new SearchSmsOptOutsQuery(Tenant, SmsOptOutStatusFilter.All, Term: null, Page: 1, Size: 20, Actor, false),
            reader,
            Options(enabled: true),
            CancellationToken.None
        );

        Assert.Equal(Actor, reader.LastStatsAssignee);
        Assert.Equal(Actor, reader.LastOptOutAssignee);
    }

    private sealed class CapturingReader : ISmsReadService
    {
        public Guid? LastSearchAssignee { get; private set; }
        public Guid? LastDetailAssignee { get; private set; }
        public Guid? LastStatsAssignee { get; private set; }
        public Guid? LastOptOutAssignee { get; private set; }

        public Task<PagedResult<SmsMessageSummaryResponse>> SearchMessagesAsync(
            Guid tenantId,
            Guid? customerId,
            SmsMessageStatusFilter status,
            string? term,
            DateTime? fromUtc,
            DateTime? toUtc,
            string? sourceContext,
            int page,
            int size,
            Guid? assignedToUserId = null,
            CancellationToken ct = default
        )
        {
            LastSearchAssignee = assignedToUserId;
            return Task.FromResult(new PagedResult<SmsMessageSummaryResponse>([], page, size, 0));
        }

        public Task<SmsMessageDetailResponse?> GetMessageByIdAsync(
            Guid tenantId,
            Guid messageId,
            Guid? assignedToUserId = null,
            CancellationToken ct = default
        )
        {
            LastDetailAssignee = assignedToUserId;
            return Task.FromResult<SmsMessageDetailResponse?>(null);
        }

        public Task<SmsStatsResponse> GetStatsAsync(
            Guid tenantId,
            DateTime fromUtc,
            DateTime toUtc,
            string? sourceContext,
            Guid? assignedToUserId = null,
            CancellationToken ct = default
        )
        {
            LastStatsAssignee = assignedToUserId;
            return Task.FromResult(new SmsStatsResponse(0, 0, 0, 0, 0, 0, 0, 0));
        }

        public Task<PagedResult<SmsOptOutSummaryResponse>> SearchOptOutsAsync(
            Guid tenantId,
            SmsOptOutStatusFilter status,
            string? term,
            int page,
            int size,
            Guid? assignedToUserId = null,
            CancellationToken ct = default
        )
        {
            LastOptOutAssignee = assignedToUserId;
            return Task.FromResult(new PagedResult<SmsOptOutSummaryResponse>([], page, size, 0));
        }
    }
}
