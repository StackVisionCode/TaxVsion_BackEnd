namespace TaxVision.Tasks.Api.Responses;

/// <summary>
/// Pre-flight del retiro en Tasks: une los conteos de Tasks y de Client Forms (dos módulos que no se conocen
/// entre sí). Campos aditivos; <see cref="OpenTasks"/> conserva su significado original.
/// </summary>
public sealed record TasksOffboardingImpactResponse(
    int OpenTasks,
    int OpenClientRequests,
    int RunningTimers,
    int IntakeLinks,
    int OpenFormRequests
);
