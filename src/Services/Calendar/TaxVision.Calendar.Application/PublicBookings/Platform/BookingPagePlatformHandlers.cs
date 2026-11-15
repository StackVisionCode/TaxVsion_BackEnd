using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Application.Staff.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.Types;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Application.PublicBookings.Platform;

// La configuración de las páginas públicas. Cada handler: cargar en el tenant del token → cambiar → guardar.

/// <summary>Carga y guarda para los handlers de la página.</summary>
internal static class PlatformPages
{
    public static async Task<Result<BookingPageAdminView>> ChangeAsync(
        IBookingPageRepository pages,
        Guid tenantId,
        Guid pageId,
        Func<BookingPage, Result> change,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var page = await pages.GetByIdAsync(tenantId, pageId, ct);
        if (page is null)
            return Result.Failure<BookingPageAdminView>(BookingPageErrors.NotFound);
        var changed = change(page);
        if (changed.IsFailure)
            return Result.Failure<BookingPageAdminView>(changed.Error);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(PlatformViews.Page(page));
    }

    /// <summary>El tipo de cita del tenant, o null. Si sirve para una demo lo decide el dominio (BookingPage).</summary>
    public static async Task<AppointmentType?> TypeAsync(
        IAppointmentTypeRepository types,
        Guid tenantId,
        Guid typeId,
        CancellationToken ct
    )
    {
        var type = await types.GetByIdAsync(tenantId, typeId, ct);
        return type.IsSuccess && type.Value.Id == typeId ? type.Value : null;
    }

    public static Result<(BookingPageSlug Slug, LocalizedText Title, LocalizedText Description)> Texts(
        string? slug,
        string? titleEn,
        string? titleEs,
        string? descriptionEn,
        string? descriptionEs
    )
    {
        var parsedSlug = BookingPageSlug.Create(slug);
        if (parsedSlug.IsFailure)
            return Result.Failure<(BookingPageSlug, LocalizedText, LocalizedText)>(parsedSlug.Error);
        var title = LocalizedText.Create(titleEn, titleEs, BookingPage.MaxTitleLength);
        if (title.IsFailure)
            return Result.Failure<(BookingPageSlug, LocalizedText, LocalizedText)>(title.Error);
        var description = LocalizedText.Create(descriptionEn, descriptionEs, BookingPage.MaxDescriptionLength);
        return description.IsFailure
            ? Result.Failure<(BookingPageSlug, LocalizedText, LocalizedText)>(description.Error)
            : Result.Success((parsedSlug.Value, title.Value, description.Value));
    }
}

/// <summary>Crea la página, apagada. Solo en un tenant permitido (la plataforma): una oficina no publica.</summary>
public static class CreateBookingPageHandler
{
    public static async Task<Result<BookingPageAdminView>> Handle(
        CreateBookingPageCommand command,
        IBookingPageRepository pages,
        IAppointmentTypeRepository types,
        IOptions<PublicBookingOptions> options,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (!options.Value.EffectiveTenantIds.Contains(command.TenantId))
            return Result.Failure<BookingPageAdminView>(BookingPageErrors.TenantNotAllowed);
        var texts = PlatformPages.Texts(
            command.Slug,
            command.TitleEn,
            command.TitleEs,
            command.DescriptionEn,
            command.DescriptionEs
        );
        if (texts.IsFailure)
            return Result.Failure<BookingPageAdminView>(texts.Error);
        if (await pages.GetBySlugAsync(command.TenantId, texts.Value.Slug.Value, ct) is not null)
            return Result.Failure<BookingPageAdminView>(BookingPageErrors.SlugTaken);
        var type = await PlatformPages.TypeAsync(types, command.TenantId, command.AppointmentTypeId, ct);
        if (type is null)
            return Result.Failure<BookingPageAdminView>(BookingPageErrors.TypeNotUsable);

        var page = BookingPage.Create(
            command.TenantId,
            texts.Value.Slug,
            texts.Value.Title,
            texts.Value.Description,
            type,
            command.UserId,
            DateTime.UtcNow
        );
        if (page.IsFailure)
            return Result.Failure<BookingPageAdminView>(page.Error);

        pages.Add(page.Value);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(PlatformViews.Page(page.Value));
    }
}

/// <summary>Cambia el nombre público, los textos y el tipo de cita.</summary>
public static class UpdateBookingPageHandler
{
    public static async Task<Result<BookingPageAdminView>> Handle(
        UpdateBookingPageCommand command,
        IBookingPageRepository pages,
        IAppointmentTypeRepository types,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var texts = PlatformPages.Texts(
            command.Slug,
            command.TitleEn,
            command.TitleEs,
            command.DescriptionEn,
            command.DescriptionEs
        );
        if (texts.IsFailure)
            return Result.Failure<BookingPageAdminView>(texts.Error);
        if (
            await pages.GetBySlugAsync(command.TenantId, texts.Value.Slug.Value, ct) is { } other
            && other.Id != command.PageId
        )
            return Result.Failure<BookingPageAdminView>(BookingPageErrors.SlugTaken);
        var appointmentType = await PlatformPages.TypeAsync(types, command.TenantId, command.AppointmentTypeId, ct);
        if (appointmentType is null)
            return Result.Failure<BookingPageAdminView>(BookingPageErrors.TypeNotUsable);

        return await PlatformPages.ChangeAsync(
            pages,
            command.TenantId,
            command.PageId,
            page =>
            {
                var type = page.ChangeAppointmentType(appointmentType, command.UserId, DateTime.UtcNow);
                return type.IsFailure
                    ? type
                    : page.ChangeTexts(
                        texts.Value.Slug,
                        texts.Value.Title,
                        texts.Value.Description,
                        command.UserId,
                        DateTime.UtcNow
                    );
            },
            unitOfWork,
            ct
        );
    }
}

public static class ChangeBookingPageRulesHandler
{
    public static Task<Result<BookingPageAdminView>> Handle(
        ChangeBookingPageRulesCommand command,
        IBookingPageRepository pages,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) =>
        PlatformPages.ChangeAsync(
            pages,
            command.TenantId,
            command.PageId,
            page =>
                page.ChangeRules(
                    command.MinNoticeMinutes,
                    command.MaxDaysAhead,
                    command.BufferMinutes,
                    command.SlotStepMinutes,
                    command.RequireEmailVerification,
                    command.ReminderHoursBefore,
                    command.IcsAlarmMinutes ?? [],
                    command.UserId,
                    DateTime.UtcNow
                ),
            unitOfWork,
            ct
        );
}

public static class ConfigureBookingPageFieldsHandler
{
    public static Task<Result<BookingPageAdminView>> Handle(
        ConfigureBookingPageFieldsCommand command,
        IBookingPageRepository pages,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) =>
        PlatformPages.ChangeAsync(
            pages,
            command.TenantId,
            command.PageId,
            page =>
                page.ConfigureFields(
                    command.PhoneField,
                    command.OfficeSizeField,
                    command.InterestField,
                    command.UserId,
                    DateTime.UtcNow
                ),
            unitOfWork,
            ct
        );
}

/// <summary>Los textos de consentimiento y su versión: cada reserva guarda la versión que aceptó.</summary>
public static class ChangeBookingPageConsentHandler
{
    public static Task<Result<BookingPageAdminView>> Handle(
        ChangeBookingPageConsentCommand command,
        IBookingPageRepository pages,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var privacy = LocalizedText.Create(
            command.PrivacyNoticeEn,
            command.PrivacyNoticeEs,
            BookingPage.MaxConsentTextLength
        );
        var marketing = LocalizedText.Create(
            command.MarketingConsentEn,
            command.MarketingConsentEs,
            BookingPage.MaxConsentTextLength
        );
        if (privacy.IsFailure || marketing.IsFailure)
            return Task.FromResult(
                Result.Failure<BookingPageAdminView>(privacy.IsFailure ? privacy.Error : marketing.Error)
            );

        return PlatformPages.ChangeAsync(
            pages,
            command.TenantId,
            command.PageId,
            page =>
                page.ChangeConsent(
                    privacy.Value,
                    marketing.Value,
                    command.ConsentVersion,
                    command.UserId,
                    DateTime.UtcNow
                ),
            unitOfWork,
            ct
        );
    }
}

/// <summary>
/// Comprueba en Auth que cada anfitrión sea un usuario activo de la plataforma. Va aparte y sin base de datos:
/// así la llamada HTTP no ocurre con una transacción abierta. El controlador la corre antes de guardar.
/// </summary>
public static class VerifyPlatformHostsHandler
{
    public static async Task<Result> Handle(
        VerifyPlatformHostsQuery query,
        IStaffContactClient contacts,
        CancellationToken ct
    )
    {
        foreach (var userId in query.UserIds)
        {
            var contact = await contacts.FindAsync(query.TenantId, userId, ct);
            if (contact is null)
                return Result.Failure(BookingPageErrors.HostNotFound);
            if (!contact.IsActive)
                return Result.Failure(BookingPageErrors.HostInactive);
        }
        return Result.Success();
    }
}

/// <summary>Reemplaza los anfitriones. Ya vienen verificados en Auth (VerifyPlatformHostsHandler).</summary>
public static class SetBookingPageHostsHandler
{
    public static Task<Result<BookingPageAdminView>> Handle(
        SetBookingPageHostsCommand command,
        IBookingPageRepository pages,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) =>
        PlatformPages.ChangeAsync(
            pages,
            command.TenantId,
            command.PageId,
            page => page.SetHosts(command.Hosts ?? [], command.UserId, DateTime.UtcNow),
            unitOfWork,
            ct
        );
}

public static class SetHeardAboutOptionsHandler
{
    public static Task<Result<BookingPageAdminView>> Handle(
        SetBookingPageHeardAboutOptionsCommand command,
        IBookingPageRepository pages,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) =>
        PlatformPages.ChangeAsync(
            pages,
            command.TenantId,
            command.PageId,
            page => page.SetHeardAboutOptions(command.Options ?? [], command.UserId, DateTime.UtcNow),
            unitOfWork,
            ct
        );
}

/// <summary>Enciende la página; antes revisa que el tipo de cita siga sirviendo.</summary>
public static class ActivateBookingPageHandler
{
    public static async Task<Result<BookingPageAdminView>> Handle(
        ActivateBookingPageCommand command,
        IBookingPageRepository pages,
        IAppointmentTypeRepository types,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var page = await pages.GetByIdAsync(command.TenantId, command.PageId, ct);
        if (
            page is not null
            && !BookingPage.IsUsableType(
                await PlatformPages.TypeAsync(types, command.TenantId, page.AppointmentTypeId, ct)
            )
        )
            return Result.Failure<BookingPageAdminView>(BookingPageErrors.TypeNotUsable);

        return await PlatformPages.ChangeAsync(
            pages,
            command.TenantId,
            command.PageId,
            target => target.Activate(command.UserId, DateTime.UtcNow),
            unitOfWork,
            ct
        );
    }
}

/// <summary>Apaga la página: el landing ofrece escribirnos. Las demos ya confirmadas siguen en pie.</summary>
public static class DeactivateBookingPageHandler
{
    public static Task<Result<BookingPageAdminView>> Handle(
        DeactivateBookingPageCommand command,
        IBookingPageRepository pages,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) =>
        PlatformPages.ChangeAsync(
            pages,
            command.TenantId,
            command.PageId,
            page =>
            {
                page.Deactivate(command.UserId, DateTime.UtcNow);
                return Result.Success();
            },
            unitOfWork,
            ct
        );
}

public static class ListBookingPagesHandler
{
    public static async Task<Result<IReadOnlyList<BookingPageSummary>>> Handle(
        ListBookingPagesQuery query,
        IBookingPageRepository pages,
        CancellationToken ct
    )
    {
        var summaries = new List<BookingPageSummary>();
        foreach (var page in await pages.ListAsync(query.TenantId, ct))
            summaries.Add(
                new BookingPageSummary(
                    page.Id,
                    page.Slug.Value,
                    page.Title.En,
                    page.IsActive,
                    page.ActiveHosts.Count,
                    page.UpdatedAtUtc
                )
            );
        return Result.Success<IReadOnlyList<BookingPageSummary>>(summaries);
    }
}

public static class GetBookingPageHandler
{
    public static async Task<Result<BookingPageAdminView>> Handle(
        GetBookingPageQuery query,
        IBookingPageRepository pages,
        CancellationToken ct
    )
    {
        var page = await pages.GetByIdAsync(query.TenantId, query.PageId, ct);
        return page is null
            ? Result.Failure<BookingPageAdminView>(BookingPageErrors.NotFound)
            : Result.Success(PlatformViews.Page(page));
    }
}
