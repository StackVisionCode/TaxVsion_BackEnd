namespace TaxVision.Tasks.ClientForms.Application.Actions.Abstractions;

// Outcome es uno de FormFileReassignmentOutcomes.
public sealed record FormFileReassignmentResult(Guid FileId, string Outcome);
