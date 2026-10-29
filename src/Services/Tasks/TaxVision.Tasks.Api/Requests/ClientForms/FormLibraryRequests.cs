namespace TaxVision.Tasks.Api.Requests.ClientForms;

public sealed record RecordLibraryReviewRequest(int ReviewedVersionNumber, string ReviewerRole, string? Note);

public sealed record SetLibraryListingRequest(string LibraryKey, bool IsListed);
