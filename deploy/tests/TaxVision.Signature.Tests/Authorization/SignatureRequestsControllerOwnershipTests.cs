using System.Security.Claims;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.ResourceAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Api.Controllers;
using TaxVision.Signature.Api.Requests;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests;
using TaxVision.Signature.Application.Requests.Commands.Create;
using TaxVision.Signature.Domain.Requests;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;
using Xunit;

namespace TaxVision.Signature.Tests.Authorization;

/// <summary>
/// RBAC Fase 4 (RBAC_Hardening_Plan.md) — prueba end-to-end (sin TestServer) el chequeo de
/// ownership de <see cref="SignatureRequestsController.Send"/> bajo el feature flag
/// <c>Authorization:ResourceOwnership:Enabled</c>. Mismo criterio que
/// <c>ShareLinksControllerOwnershipTests</c> (CloudStorage.Tests) — sin precedente de
/// WebApplicationFactory en el repo.
/// </summary>
public sealed class SignatureRequestsControllerOwnershipTests
{
    private sealed class FakeSignatureRequestRepository : ISignatureRequestRepository
    {
        private readonly Dictionary<Guid, SignatureRequest> _byId = [];

        public void Seed(SignatureRequest request) => _byId[request.Id] = request;

        public Task<SignatureRequest?> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default) =>
            Task.FromResult(
                _byId.TryGetValue(requestId, out var request) && request.TenantId == tenantId ? request : null
            );

        public Task<SignatureRequest?> GetBySealedFileIdAsync(
            Guid tenantId,
            Guid sealedFileId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<SignatureRequest?> GetByCertificateFileIdAsync(
            Guid tenantId,
            Guid certificateFileId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task AddAsync(SignatureRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListCompletedWithSealedFileAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SignatureRequest>>([]);

        public void Remove(SignatureRequest request) => throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListScheduledReadyToSendAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListDraftsWaitingForFileAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListStrandedDraftsAsync(
            DateTime createdBeforeUtc,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListExpiredCandidatesAsync(
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListReminderCandidatesAsync(
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListPurgeCandidatesAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListStaleUnsentAsync(
            DateTime olderThanUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }

    private sealed class ControllableMessageBus : IMessageBus
    {
        public object? NextResult { get; set; }
        public object? LastInvoked { get; private set; }

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            LastInvoked = message;
            return Task.FromResult((T)NextResult!);
        }

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) => ValueTask.CompletedTask;

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
            throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public string? TenantId
        {
            get => null;
            set { }
        }

        public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
            throw new NotImplementedException();

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();
    }

    /// <summary>Wrapper real y liviano sobre <see cref="IsOwnerOrHasManageHandler{TResource}"/> — sin DI, sin ServiceProvider.</summary>
    private sealed class RealAuthorizationService(IAuthorizationHandler handler) : IAuthorizationService
    {
        public async Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements
        )
        {
            var context = new AuthorizationHandlerContext(requirements, user, resource);
            await handler.HandleAsync(context);
            return context.HasSucceeded ? AuthorizationResult.Success() : AuthorizationResult.Failed();
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IAuthorizationRequirement requirement
        ) => AuthorizeAsync(user, resource, [requirement]);

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            throw new NotImplementedException();
    }

    private sealed class FakeOwnershipOptionsMonitor(bool enabled) : IOptionsMonitor<ResourceOwnershipOptions>
    {
        public ResourceOwnershipOptions CurrentValue { get; } = new() { Enabled = enabled };

        public ResourceOwnershipOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<ResourceOwnershipOptions, string> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose() { }
        }
    }

    private static SignatureRequestsController BuildController(
        SignatureRequest request,
        Guid actingUserId,
        bool flagEnabled,
        bool asPlatformAdmin = false
    )
    {
        var repo = new FakeSignatureRequestRepository();
        repo.Seed(request);
        var bus = new ControllableMessageBus { NextResult = Result.Success() };
        var authorizationService = new RealAuthorizationService(
            new IsOwnerOrHasManageHandler<SignatureRequest>(
                SignaturePermissions.RequestManage,
                new JwtEmbeddedPermissionsSource(),
                new AuthorizationMetrics()
            )
        );

        var claims = new List<Claim>
        {
            new("tenant_id", request.TenantId.ToString()),
            new("sub", actingUserId.ToString()),
        };
        if (asPlatformAdmin)
            claims.Add(new Claim("actor_type", "PlatformAdmin"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var controller = new SignatureRequestsController(
            bus,
            repo,
            authorizationService,
            new JwtEmbeddedPermissionsSource(),
            new FakeOwnershipOptionsMonitor(flagEnabled)
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };
        return controller;
    }

    private static SignatureRequest NewSignatureRequest(Guid tenantId, Guid createdByUserId) =>
        SignatureRequest
            .CreateDraft(
                tenantId,
                createdByUserId,
                "Test request",
                description: null,
                "Other",
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;

    // ----- A4: política legal del despacho (certificado + consentimiento) en POST /signature/requests -----

    private static SignatureRequestsController BuildCreateController(bool asAdmin, out ControllableMessageBus bus)
    {
        bus = new ControllableMessageBus
        {
            NextResult = Result.Failure<SignatureRequestResponse>(new Error("test.skip", "handler not run")),
        };
        var claims = new List<Claim>
        {
            new("tenant_id", Guid.NewGuid().ToString()),
            new("sub", Guid.NewGuid().ToString()),
        };
        if (asAdmin)
            claims.Add(new Claim("actor_type", "PlatformAdmin"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        return new SignatureRequestsController(
            bus,
            new FakeSignatureRequestRepository(),
            new RealAuthorizationService(
                new IsOwnerOrHasManageHandler<SignatureRequest>(
                    SignaturePermissions.RequestManage,
                    new JwtEmbeddedPermissionsSource(),
                    new AuthorizationMetrics()
                )
            ),
            new JwtEmbeddedPermissionsSource(),
            new FakeOwnershipOptionsMonitor(false)
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };
    }

    // Body que intenta debilitar ambas políticas (generar certificado OFF, consentimiento OFF).
    private static CreateSignatureRequestBody WeakeningPolicyBody() =>
        new(
            Title: "Guard test",
            Description: null,
            Category: "Other",
            Documents: [new CreateSignatureRequestDocumentBody(Guid.NewGuid(), "Doc", null)],
            RequiresSequentialSigning: false,
            GenerateCertificate: false,
            RequiresConsent: false
        );

    [Fact]
    public async Task Create_forces_a_non_admin_to_inherit_the_firm_certificate_and_consent_policy()
    {
        var controller = BuildCreateController(asAdmin: false, out var bus);

        await controller.Create(WeakeningPolicyBody(), CancellationToken.None);

        var cmd = Assert.IsType<CreateSignatureRequestCommand>(bus.LastInvoked);
        // null = ignora lo que mandó el no-admin y hereda el default del tenant.
        Assert.Null(cmd.GenerateCertificate);
        Assert.Null(cmd.RequiresConsent);
    }

    [Fact]
    public async Task Create_lets_an_admin_override_the_firm_certificate_and_consent_policy()
    {
        var controller = BuildCreateController(asAdmin: true, out var bus);

        await controller.Create(WeakeningPolicyBody(), CancellationToken.None);

        var cmd = Assert.IsType<CreateSignatureRequestCommand>(bus.LastInvoked);
        Assert.False(cmd.GenerateCertificate);
        Assert.False(cmd.RequiresConsent);
    }

    [Fact]
    public async Task Send_bypasses_ownership_check_when_flag_is_disabled()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var request = NewSignatureRequest(tenantId, ownerId);
        var controller = BuildController(request, actingUserId: Guid.NewGuid(), flagEnabled: false);

        var result = await controller.Send(request.Id, CancellationToken.None);

        Assert.IsNotType<ObjectResult>(result);
    }

    [Fact]
    public async Task Send_allows_the_requests_creator()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var request = NewSignatureRequest(tenantId, ownerId);
        var controller = BuildController(request, actingUserId: ownerId, flagEnabled: true);

        var result = await controller.Send(request.Id, CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
    }

    [Fact]
    public async Task Send_rejects_a_non_owner_without_the_manage_permission()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var request = NewSignatureRequest(tenantId, ownerId);
        var controller = BuildController(request, actingUserId: Guid.NewGuid(), flagEnabled: true);

        var result = await controller.Send(request.Id, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal("SignatureRequest.NotOwner", error.Code);
    }

    [Fact]
    public async Task Send_always_allows_PlatformAdmin_regardless_of_ownership()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var request = NewSignatureRequest(tenantId, ownerId);
        var controller = BuildController(
            request,
            actingUserId: Guid.NewGuid(),
            flagEnabled: true,
            asPlatformAdmin: true
        );

        var result = await controller.Send(request.Id, CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
    }
}
