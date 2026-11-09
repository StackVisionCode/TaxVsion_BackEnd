using TaxVision.Documents.Application.Generations.ClientFormSummary;
using TaxVision.Documents.Application.Generations.GenerateInvoiceDocument;
using TaxVision.Documents.Application.Generations.OnboardingReceipt;
using TaxVision.Documents.Application.Generations.SaaSReceipt;
using Wolverine;

namespace TaxVision.Documents.Api.Messaging;

/// <summary>Cola local de las generaciones; la usan Program.cs y la prueba de durabilidad.</summary>
public static class GenerationQueueConfiguration
{
    public const string QueueName = "documents-generation";

    public static WolverineOptions ConfigureGenerationQueue(this WolverineOptions options)
    {
        // Durable: UseDurableOutboxOnAllSendingEndpoints no cubre las colas locales (DOC-1).
        options.LocalQueue(QueueName).UseDurableInbox();
        options.PublishMessage<ProcessInvoiceGenerationCommand>().ToLocalQueue(QueueName);
        options.PublishMessage<ProcessOnboardingReceiptGenerationCommand>().ToLocalQueue(QueueName);
        options.PublishMessage<ProcessSaaSReceiptGenerationCommand>().ToLocalQueue(QueueName);
        options.PublishMessage<ProcessClientFormSummaryGenerationCommand>().ToLocalQueue(QueueName);
        return options;
    }
}
