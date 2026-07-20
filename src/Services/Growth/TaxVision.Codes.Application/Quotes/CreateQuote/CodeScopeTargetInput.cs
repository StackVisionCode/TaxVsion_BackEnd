using TaxVision.Codes.Domain.Definitions;

namespace TaxVision.Codes.Application.Quotes.CreateQuote;

public sealed record CodeScopeTargetInput(CodeScopeType Type, string ScopeId);
