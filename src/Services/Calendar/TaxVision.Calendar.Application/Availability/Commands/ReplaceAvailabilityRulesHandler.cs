using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Domain.Availability;

namespace TaxVision.Calendar.Application.Availability.Commands;

/// <param name="CanManageOthers">Tiene <c>calendar.availability.manage</c>: puede tocar el horario de otro.</param>
public sealed record ReplaceAvailabilityRulesCommand(
    Guid TenantId,
    Guid ActingUserId,
    Guid UserId,
    bool CanManageOthers,
    IReadOnlyList<AvailabilityRuleInput> Rules
);

/// <summary>
/// Reemplaza el horario semanal completo de una persona. Todo o nada: una franja inválida deja el
/// horario anterior intacto. Las franjas viejas se desactivan, no se borran.
/// </summary>
public static class ReplaceAvailabilityRulesHandler
{
    public const int MaxRules = 21;

    public static async Task<Result<IReadOnlyList<AvailabilityRuleResponse>>> Handle(
        ReplaceAvailabilityRulesCommand command,
        IAvailabilityRepository availability,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.UserId != command.ActingUserId && !command.CanManageOthers)
            return Result.Failure<IReadOnlyList<AvailabilityRuleResponse>>(AvailabilityErrors.Forbidden);
        if (command.Rules.Count > MaxRules)
            return Result.Failure<IReadOnlyList<AvailabilityRuleResponse>>(AvailabilityErrors.TooManyRules);

        var now = DateTime.UtcNow;
        var created = new List<AvailabilityRule>(command.Rules.Count);
        foreach (var input in command.Rules)
        {
            var rule = AvailabilityRule.Create(
                command.TenantId,
                command.UserId,
                (DaysOfWeekMask)(input.DaysMask & (int)DaysOfWeekMask.EveryDay),
                input.StartTime,
                input.EndTime,
                input.TimeZoneId,
                now
            );
            if (rule.IsFailure)
                return Result.Failure<IReadOnlyList<AvailabilityRuleResponse>>(rule.Error);
            created.Add(rule.Value);
        }

        foreach (var existing in await availability.ListRulesAsync(command.TenantId, command.UserId, ct))
            existing.Deactivate();
        foreach (var rule in created)
            availability.AddRule(rule);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success<IReadOnlyList<AvailabilityRuleResponse>>(
            created.Select(AvailabilityRuleResponse.From).ToList()
        );
    }
}
