using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers.Calendar;

/// <summary>
/// Correo, push e in-app de los asistentes de una cita, por el punto único. El gate de entrega (inactivo,
/// preferencia, catálogo) decide por cada asistente con <c>UserId</c>; un cliente invitado sin cuenta recibe igual.
/// </summary>
internal static class CalendarRecipients
{
    /// <summary>
    /// Un aviso por persona (CAL-10): si la cita tiene sala, sus invitados reciben el de la sala
    /// (Communication, con su enlace) y el de la cita queda para el organizador, que es el host.
    /// </summary>
    public static IReadOnlyList<AppointmentRecipient> WithoutRoomInvitees(
        IReadOnlyList<AppointmentRecipient> recipients,
        bool hasRoom,
        Guid? organizerUserId
    ) => hasRoom ? recipients.Where(r => r.UserId is { } id && id == organizerUserId).ToList() : recipients;

    /// <summary>
    /// Push genérico a cada destinatario con cuenta, con el enlace a la cita; sin dispositivo no hace nada.
    /// Un aviso nuevo de la misma cita sustituye al anterior.
    /// </summary>
    public static async Task PushAsync(
        NotificationDispatcher dispatcher,
        IRecipientLocale locales,
        IReadOnlyList<AppointmentRecipient> recipients,
        Guid tenantId,
        string kind,
        Guid appointmentId,
        Guid relatedEventId,
        string? correlationId,
        CancellationToken ct
    )
    {
        // Una lectura de idioma por usuario durante el evento.
        var languages = new RecipientLocaleCache(locales, tenantId);
        foreach (var recipient in recipients)
        {
            if (recipient.UserId is not { } userId)
                continue;
            var target = new NotificationTarget(userId, Locale: await languages.ForUserAsync(userId, ct));
            await dispatcher.DispatchAsync(
                Intent(kind, tenantId, target, relatedEventId, correlationId) with
                {
                    Push = new PushContent(
                        appointmentId.ToString(),
                        Tag: $"appointment:{appointmentId:N}",
                        // El clic busca el aviso de la cita en la bandeja aunque este push vaya aparte.
                        SourceEventId: relatedEventId
                    ),
                },
                ct
            );
        }
    }

    /// <summary>Un compañero (con <c>UserId</c>) lee como Staff; un cliente o invitado sin cuenta, como Client.</summary>
    public static ScribeRenderAudience AudienceOf(AppointmentRecipient recipient) =>
        recipient.UserId is null ? ScribeRenderAudience.Client : ScribeRenderAudience.Staff;

    /// <summary>
    /// Renderiza una vez por audiencia e idioma presentes (no por persona) y encola el correo de cada
    /// destinatario. Todos los renders van antes de encolar: si Scribe falla, el reintento no deja correos a
    /// medias. Las variables pueden depender del idioma (textos del sistema dentro del correo).
    /// </summary>
    public static async Task QueueByAudienceAsync(
        IReadOnlyList<AppointmentRecipient> recipients,
        Guid tenantId,
        string templateKey,
        string kind,
        Func<RecipientLocale, IReadOnlyDictionary<string, object?>> variables,
        IScribeRenderClient scribeClient,
        NotificationDispatcher dispatcher,
        IRecipientLocale locales,
        Guid relatedEventId,
        string? correlationId,
        CancellationToken ct,
        string? rsvpBaseUrl = null,
        Guid? organizerUserId = null,
        string? timeZoneId = null
    )
    {
        var languages = new RecipientLocaleCache(locales, tenantId);
        var renders = new Dictionary<(ScribeRenderAudience, string, ScribeReader), ScribeRenderedEmail>();
        var emails = new List<(AppointmentRecipient Recipient, RecipientLocale Locale, ScribeRenderedEmail Render)>();
        foreach (var recipient in recipients)
        {
            // Idioma de quien lee; las horas tipadas, en la zona de la cita (no en la del lector), con su rótulo.
            var locale = (await LocaleOfAsync(recipient, languages, locales, tenantId, ct)) with
            {
                TimeZoneId = timeZoneId,
            };
            var audience = AudienceOf(recipient);
            ScribeRenderedEmail rendered;
            // Con enlace de respuesta el render es de esa persona: sus botones son solo suyos.
            if (RsvpTokenOf(recipient, rsvpBaseUrl, organizerUserId) is { } token)
            {
                var own = new Dictionary<string, object?>(variables(locale));
                CalendarRsvpLinks.AddTo(own, rsvpBaseUrl!, token);
                rendered = (
                    await scribeClient.RenderForRecipientAsync(templateKey, tenantId, own, audience, locale, ct)
                ).EnsureRendered(templateKey);
            }
            else if (!renders.TryGetValue((audience, locale.TemplateLanguage, locale.Reader), out rendered!))
            {
                rendered = (
                    await scribeClient.RenderForRecipientAsync(
                        templateKey,
                        tenantId,
                        variables(locale),
                        audience,
                        locale,
                        ct
                    )
                ).EnsureRendered(templateKey);
                renders[(audience, locale.TemplateLanguage, locale.Reader)] = rendered;
            }
            emails.Add((recipient, locale, rendered));
        }

        foreach (var (recipient, locale, rendered) in emails)
        {
            await dispatcher.DispatchAsync(
                Intent(
                    kind,
                    tenantId,
                    new NotificationTarget(
                        recipient.UserId,
                        recipient.Email,
                        CustomerId: recipient.CustomerId,
                        Locale: locale
                    ),
                    relatedEventId,
                    correlationId
                ) with
                {
                    // Ya renderizado arriba con el idioma y la zona de la cita.
                    Email = new EmailContent(
                        templateKey,
                        NoVariables,
                        AudienceOf(recipient),
                        Render: (_, _) => Task.FromResult(rendered)
                    ),
                },
                ct
            );
        }
    }

    /// <summary>Compañero → su idioma; cliente invitado → el del cliente; externo sin datos → inglés.</summary>
    private static async Task<RecipientLocale> LocaleOfAsync(
        AppointmentRecipient recipient,
        RecipientLocaleCache languages,
        IRecipientLocale locales,
        Guid tenantId,
        CancellationToken ct
    ) =>
        recipient switch
        {
            { UserId: { } userId } => await languages.ForUserAsync(userId, ct),
            { CustomerId: { } customerId } => await locales.ForCustomerAsync(tenantId, customerId, ct),
            _ => locales.FromEventLanguage(null),
        };

    /// <summary>El organizador ya aceptó: sus avisos no llevan botones.</summary>
    private static string? RsvpTokenOf(AppointmentRecipient recipient, string? rsvpBaseUrl, Guid? organizerUserId) =>
        rsvpBaseUrl is null
        || string.IsNullOrWhiteSpace(recipient.RsvpToken)
        || (organizerUserId is not null && recipient.UserId == organizerUserId)
            ? null
            : recipient.RsvpToken;

    /// <summary>
    /// Publica in-app por el puente del catálogo a cada asistente con cuenta. Un asistente sin
    /// <c>UserId</c> (cliente invitado) no tiene bandeja interna: queda cubierto por el email.
    /// </summary>
    public static async Task PublishInAppAsync(
        NotificationDispatcher dispatcher,
        IReadOnlyList<AppointmentRecipient> recipients,
        Guid tenantId,
        Guid appointmentId,
        Guid sourceEventId,
        string kind,
        string sourceEventType,
        DateTime occurredAtUtc,
        string title,
        string? correlationId,
        Guid? customerId,
        CancellationToken ct
    )
    {
        var context = new Dictionary<string, string>();
        if (customerId is { } cid)
            context["customerId"] = cid.ToString();

        foreach (var recipient in recipients)
        {
            if (recipient.UserId is not { } userId)
                continue;

            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    kind,
                    tenantId,
                    new NotificationTarget(userId),
                    sourceEventId,
                    sourceEventType,
                    occurredAtUtc,
                    correlationId,
                    InApp: new InAppContent(
                        ResourceId: appointmentId.ToString(),
                        ResourceContext: context,
                        FallbackTitle: title
                    )
                ),
                ct
            );
        }
    }

    private static readonly IReadOnlyDictionary<string, object?> NoVariables = new Dictionary<string, object?>();

    // Push y correo no usan el tipo ni la fecha del evento (solo el in-app los publica).
    private static NotificationIntent Intent(
        string kind,
        Guid tenantId,
        NotificationTarget target,
        Guid relatedEventId,
        string? correlationId
    ) => new(kind, tenantId, target, relatedEventId, kind, DateTime.UtcNow, correlationId);
}
