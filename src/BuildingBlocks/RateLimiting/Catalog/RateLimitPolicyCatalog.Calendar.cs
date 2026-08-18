namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition CalendarRead = Define(
        "calendar.f.read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition CalendarCreate = Define(
        "calendar.g.create",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por mover, cambiar titulo y editar la serie: escrituras simples sobre una cita que
    // ya existe.
    public static readonly RateLimitPolicyDefinition CalendarUpdate = Define(
        "calendar.g.update",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition CalendarDelete = Define(
        "calendar.g.delete",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Un click por invitacion, y las invitaciones llegan de a muchas: cuota de escritura normal.
    public static readonly RateLimitPolicyDefinition CalendarRsvp = Define(
        "calendar.g.rsvp",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // La consulta caliente del servicio: un mes de una oficina con 40 series son 40 expansiones de
    // RRULE por request, y el frontend la llama en cada cambio de vista.
    public static readonly RateLimitPolicyDefinition CalendarRange = Define(
        "calendar.h.range",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 100
    );

    // Sin JWT: el token firmado de la URL es la credencial, asi que la particion es el token y no el
    // usuario. Google reintenta el feed desde IPs rotativas, de modo que limitar por IP no frena
    // nada y castiga al que comparte salida.
    public static readonly RateLimitPolicyDefinition CalendarIcsFeed = Define(
        "calendar.h.ics",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Token,
        [],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow
    );

    // La mas cara de todas: expande las series y ademas cruza reglas de disponibilidad y bloqueos.
    public static readonly RateLimitPolicyDefinition CalendarAvailability = Define(
        "calendar.i.availability",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 10,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 60
    );
}
