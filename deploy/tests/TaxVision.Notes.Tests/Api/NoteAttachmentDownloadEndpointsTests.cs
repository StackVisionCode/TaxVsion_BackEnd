using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notes.Api.Controllers;
using TaxVision.Notes.Application.Notes.Queries;
using Wolverine;

namespace TaxVision.Notes.Tests.Api;

/// <summary>
/// Descarga mediada de adjuntos: personal con <c>notes.read</c> por <c>/notes/{id}/…</c> y portal con
/// <c>notes.portal.read</c> por <c>/notes/portal/{id}/…</c>; el cliente sale siempre del token.
/// </summary>
public sealed class NoteAttachmentDownloadEndpointsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();

    [Fact]
    public void Staff_download_uses_notes_read_and_the_note_get_rate_limit()
    {
        var action = typeof(NotesController).GetMethod(nameof(NotesController.AttachmentDownloadUrl))!;

        Assert.Equal(
            "{id:guid}/attachments/{fileId:guid}/download-url",
            action.GetCustomAttribute<HttpPostAttribute>()!.Template
        );
        Assert.Equal(
            [NotesPermissions.Read],
            action
                .GetCustomAttributes<HasPermissionAttribute>()
                .Select(attribute => attribute.Policy![HasPermissionAttribute.PolicyPrefix.Length..])
        );
        Assert.Equal("notes.f.get", action.GetCustomAttribute<RateLimitAttribute>()!.PolicyName);
    }

    [Fact]
    public void Portal_download_lives_in_the_portal_controller_with_its_rate_limit()
    {
        var type = typeof(PortalNotesController);
        var action = type.GetMethod(nameof(PortalNotesController.AttachmentDownloadUrl))!;

        Assert.Equal([ActorType.CustomerPortal], type.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes);
        Assert.Equal(
            [NotesPermissions.PortalRead],
            type.GetCustomAttributes<HasPermissionAttribute>()
                .Select(attribute => attribute.Policy![HasPermissionAttribute.PolicyPrefix.Length..])
        );
        Assert.Equal(
            "{id:guid}/attachments/{fileId:guid}/download-url",
            action.GetCustomAttribute<HttpPostAttribute>()!.Template
        );
        Assert.Equal("notes.f.portal_read", action.GetCustomAttribute<RateLimitAttribute>()!.PolicyName);
    }

    [Fact]
    public async Task The_portal_query_takes_the_customer_from_the_token()
    {
        var (noteId, fileId) = (Guid.NewGuid(), Guid.NewGuid());
        var bus = AnsweringBus.Create();
        var controller = new PortalNotesController(bus) { ControllerContext = Context(withCustomer: true) };

        var result = await controller.AttachmentDownloadUrl(noteId, fileId, "preview", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var query = Assert.IsType<GetPortalNoteAttachmentDownloadUrlQuery>(((AnsweringBus)(object)bus).LastMessage);
        Assert.Equal(
            (Tenant, Customer, noteId, fileId, true),
            (query.TenantId, query.CustomerId, query.NoteId, query.FileId, query.ForPreview)
        );
    }

    [Fact]
    public async Task A_portal_token_without_customer_is_forbidden()
    {
        var bus = AnsweringBus.Create();
        var controller = new PortalNotesController(bus) { ControllerContext = Context(withCustomer: false) };

        var result = await controller.AttachmentDownloadUrl(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result);
        Assert.Null(((AnsweringBus)(object)bus).LastMessage);
    }

    private static ControllerContext Context(bool withCustomer)
    {
        var claims = new List<Claim>
        {
            new(ClaimNames.TenantId, Tenant.ToString()),
            new("sub", Actor.ToString()),
            new(ClaimNames.ActorType, nameof(ActorType.CustomerPortal)),
        };
        if (withCustomer)
            claims.Add(new(ClaimNames.CustomerId, Customer.ToString()));
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
        };
    }

    /// <summary>Bus que registra el último InvokeAsync y responde una URL.</summary>
    public class AnsweringBus : DispatchProxy
    {
        public object? LastMessage { get; private set; }

        public static IMessageBus Create() => DispatchProxy.Create<IMessageBus, AnsweringBus>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is not { Name: nameof(IMessageBus.InvokeAsync), IsGenericMethod: true })
                throw new NotSupportedException(targetMethod?.Name);

            LastMessage = args![0];
            return Task.FromResult(
                Result.Success(
                    new NoteAttachmentDownloadUrlResponse(
                        Guid.NewGuid(),
                        new Uri("https://files.test/f"),
                        DateTime.UtcNow
                    )
                )
            );
        }
    }
}
