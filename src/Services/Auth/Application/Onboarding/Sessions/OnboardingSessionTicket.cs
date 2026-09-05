namespace TaxVision.Auth.Application.Onboarding.Sessions;

public sealed record OnboardingSessionTicket(string SessionToken, DateTime ExpiresAtUtc, string TokenType = "Bearer");
