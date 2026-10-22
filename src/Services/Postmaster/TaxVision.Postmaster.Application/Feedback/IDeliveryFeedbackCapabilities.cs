using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Application.Feedback;

/// <summary>Qué sabe hacer cada origen: recibir avisos (push), consultarlos (pull) o ambos.</summary>
public interface IDeliveryFeedbackCapabilities
{
    bool SupportsPush(FeedbackSource source);

    bool SupportsPull(FeedbackSource source);

    IDeliveryFeedbackTranslator? FindTranslator(FeedbackSource source);

    IDeliveryFeedbackPoller? FindPoller(FeedbackSource source);
}
