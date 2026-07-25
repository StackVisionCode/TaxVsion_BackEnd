using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace BuildingBlocks.ResourceAuthorization;

/// <summary>
/// RBAC Fase 4 (RBAC_Hardening_Plan.md) — operaciones reconocidas por
/// <see cref="IsOwnerOrHasManageHandler{TResource}"/>. Mismo <see cref="OperationAuthorizationRequirement"/>
/// nativo de ASP.NET Core que usan los samples oficiales de resource-based authorization — el
/// handler no distingue semánticamente entre ellas (todas resuelven "¿el actor es dueño, tiene el
/// permiso manage, o es PlatformAdmin?"), pero declararlas separadas deja rastro legible en cada
/// call site (<c>Operations.Revoke</c> se lee mejor que <c>Operations.Update</c> en un endpoint de
/// revocación) y dispuesto para diferenciar en el futuro si algún recurso necesita reglas distintas
/// por operación.
/// </summary>
public static class Operations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
    public static readonly OperationAuthorizationRequirement Update = new() { Name = nameof(Update) };
    public static readonly OperationAuthorizationRequirement Delete = new() { Name = nameof(Delete) };
    public static readonly OperationAuthorizationRequirement Manage = new() { Name = nameof(Manage) };
    public static readonly OperationAuthorizationRequirement Send = new() { Name = nameof(Send) };
    public static readonly OperationAuthorizationRequirement Cancel = new() { Name = nameof(Cancel) };
    public static readonly OperationAuthorizationRequirement Revoke = new() { Name = nameof(Revoke) };
}
