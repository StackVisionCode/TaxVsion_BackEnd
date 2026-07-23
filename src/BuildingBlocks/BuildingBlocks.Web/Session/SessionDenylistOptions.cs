namespace BuildingBlocks.Web.Session;

/// <summary>
/// RBAC Fase 6 — flag por servicio para apagar el chequeo de denylist sin redeploy (ej. si Redis
/// tiene un incidente prolongado y se prefiere degradar explícitamente en vez de depender solo del
/// fail-open silencioso de <see cref="SessionDenylistReader"/>). Default habilitado.
/// </summary>
public sealed class SessionDenylistOptions
{
    public const string SectionName = "SessionDenylist";

    public bool Enabled { get; set; } = true;
}
