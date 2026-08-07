using System.Security.Claims;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.ResourceAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.Notes.Api.Controllers;
using TaxVision.Notes.Api.Requests;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;
using TaxVision.Notes.Tests.Application;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;
using Xunit;

namespace TaxVision.Notes.Tests.Api;

/// <summary>
/// H-03 — Notes es el piloto de la Capa 3b (ownership) con el flag
/// <c>Authorization:ResourceOwnership:Enabled</c> ya en <c>true</c>. Es defensa en profundidad, no
/// una regla nueva: los 7 endpoints de edición de contenido ya exigían
/// <c>NoteVisibilityPolicy.CanEditContent</c> (estrictamente el autor) en Application, así que el
/// controller y el handler coinciden. Estos tests fijan esa coincidencia.
/// </summary>
public sealed class NotesControllerOwnershipTests
{
    private static Note NewNote(Guid tenantId, Guid authorUserId) =>
        Note.Create(
            tenantId,
            authorUserId,
            NoteContent.Create("<p>x</p>").Value,
            NoteReference.Create(NoteTargetType.None, null).Value,
            NoteVisibility.Private,
            null
        ).Value;

    [Fact]
    public async Task Con_el_flag_encendido_el_autor_pasa_el_chequeo_del_controller()
    {
        var tenantId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var note = NewNote(tenantId, authorId);
        var controller = BuildController(note, actingUserId: authorId, flagEnabled: true);

        await Assert.ThrowsAsync<ReachedApplicationHandlerException>(() =>
            controller.SetColor(note.Id, new SetNoteColorRequest(NoteColorKind.Important), CancellationToken.None)
        );
    }

    [Fact]
    public async Task Con_el_flag_encendido_un_no_autor_recibe_403_antes_de_llegar_al_handler()
    {
        var tenantId = Guid.NewGuid();
        var note = NewNote(tenantId, Guid.NewGuid());
        var controller = BuildController(note, actingUserId: Guid.NewGuid(), flagEnabled: true);

        // No lanza: el controller corta ANTES de invocar el handler de Application.
        var result = await controller.SetColor(
            note.Id,
            new SetNoteColorRequest(NoteColorKind.Important),
            CancellationToken.None
        );

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public async Task PlatformAdmin_pasa_aunque_no_sea_el_autor()
    {
        // Coherente con la Capa 1: PlatformAdmin bypassea el pipeline de autorización (README §41.1).
        var tenantId = Guid.NewGuid();
        var note = NewNote(tenantId, Guid.NewGuid());
        var controller = BuildController(note, actingUserId: Guid.NewGuid(), flagEnabled: true, asPlatformAdmin: true);

        await Assert.ThrowsAsync<ReachedApplicationHandlerException>(() =>
            controller.SetColor(note.Id, new SetNoteColorRequest(NoteColorKind.Important), CancellationToken.None)
        );
    }

    [Fact]
    public async Task Con_el_flag_apagado_el_controller_no_bloquea_nada()
    {
        // El 403 real lo sigue devolviendo el handler de Application (CanEditContent) — este chequeo
        // es solo la red de seguridad extra.
        var tenantId = Guid.NewGuid();
        var note = NewNote(tenantId, Guid.NewGuid());
        var controller = BuildController(note, actingUserId: Guid.NewGuid(), flagEnabled: false);

        await Assert.ThrowsAsync<ReachedApplicationHandlerException>(() =>
            controller.SetColor(note.Id, new SetNoteColorRequest(NoteColorKind.Important), CancellationToken.None)
        );
    }

    private static NotesController BuildController(
        Note note,
        Guid actingUserId,
        bool flagEnabled,
        bool asPlatformAdmin = false
    )
    {
        var repo = new FakeNoteRepository();
        repo.Seed(note);

        var claims = new List<Claim>
        {
            new("tenant_id", note.TenantId.ToString()),
            new("sub", actingUserId.ToString()),
        };
        if (asPlatformAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "PlatformAdmin"));

        return new NotesController(
            new StubMessageBus(),
            new JwtEmbeddedPermissionsSource(),
            repo,
            new RealAuthorizationService(
                new IsOwnerOrHasManageHandler<Note>(
                    null,
                    new JwtEmbeddedPermissionsSource(),
                    new AuthorizationMetrics()
                )
            ),
            new StubOwnershipOptionsMonitor(flagEnabled)
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
            },
        };
    }

    /// <summary>Envuelve el handler real sin DI ni ServiceProvider (mismo patrón que Correspondence).</summary>
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

    private sealed class StubOwnershipOptionsMonitor(bool enabled) : IOptionsMonitor<ResourceOwnershipOptions>
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

    /// <summary>Marca que el gate del controller dejó pasar y la request llegó a Application.</summary>
    private sealed class ReachedApplicationHandlerException : Exception;

    private sealed class StubMessageBus : IMessageBus
    {
        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new ReachedApplicationHandlerException();

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
}
