using System.Security.Cryptography;
using TaxVision.Auth.Onboarding.Application.Abstractions;

namespace TaxVision.Auth.Onboarding.Infrastructure.Security;

public sealed class NumericOtpCodeGenerator : IOtpCodeGenerator
{
    public string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}
