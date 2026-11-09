using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>Contenido cifrado del token de un enlace de cuenta (reset, invitación, cambio de correo).</summary>
public sealed record AuthAccountTokenPayload(string Token);

/// <summary>Esquemas y ataduras del token protegido de los eventos de cuenta de Auth (NOT-04).</summary>
public static class AuthTokenPayloadSchemas
{
    public const string PasswordReset = "auth.password_reset_token.v1";
    public const string Invitation = "auth.invitation_token.v1";
    public const string EmailChange = "auth.email_change_token.v1";

    // Atadura por tenant y usuario (o invitación): el payload no sirve copiado a otro evento.
    public static ProtectedPayloadBinding PasswordResetBinding(Guid tenantId, Guid userId) =>
        new(tenantId, null, null, IdempotencyKey: $"auth.password_reset:{userId:N}");

    public static ProtectedPayloadBinding InvitationBinding(Guid tenantId, Guid invitationId) =>
        new(tenantId, null, null, IdempotencyKey: $"auth.invitation:{invitationId:N}");

    public static ProtectedPayloadBinding EmailChangeBinding(Guid tenantId, Guid userId) =>
        new(tenantId, null, null, IdempotencyKey: $"auth.email_change:{userId:N}");
}
