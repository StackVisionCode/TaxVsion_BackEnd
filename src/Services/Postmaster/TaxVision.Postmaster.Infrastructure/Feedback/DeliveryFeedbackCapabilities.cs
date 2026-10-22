using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Infrastructure.Feedback;

/// <summary>
/// Sale de lo que esté registrado en DI: sumar un proveedor es registrar su traductor (push), su poller
/// (pull) o ambos. Sin cambios en el handler.
/// </summary>
public sealed class DeliveryFeedbackCapabilities(
    IEnumerable<IDeliveryFeedbackTranslator> translators,
    IEnumerable<IDeliveryFeedbackPoller> pollers
) : IDeliveryFeedbackCapabilities
{
    private readonly Dictionary<FeedbackSource, IDeliveryFeedbackTranslator> _translators = translators.ToDictionary(
        t => t.Source
    );
    private readonly Dictionary<FeedbackSource, IDeliveryFeedbackPoller> _pollers = pollers.ToDictionary(p => p.Source);

    public bool SupportsPush(FeedbackSource source) => _translators.ContainsKey(source);

    public bool SupportsPull(FeedbackSource source) => _pollers.ContainsKey(source);

    public IDeliveryFeedbackTranslator? FindTranslator(FeedbackSource source) => _translators.GetValueOrDefault(source);

    public IDeliveryFeedbackPoller? FindPoller(FeedbackSource source) => _pollers.GetValueOrDefault(source);
}
