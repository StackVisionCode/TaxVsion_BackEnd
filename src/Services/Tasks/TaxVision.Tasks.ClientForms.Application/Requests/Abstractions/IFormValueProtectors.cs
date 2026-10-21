using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;

public interface IAnswersProtector
{
    ProtectedAnswers Protect(string json);
    string Unprotect(ProtectedAnswers value);
}

public interface ISensitiveValueProtector
{
    ProtectedSensitiveValue Protect(string normalized);
    string Unprotect(ProtectedSensitiveValue value);
}

public sealed record ProtectedSensitiveValue(string Cipher, short KeyVersion);
