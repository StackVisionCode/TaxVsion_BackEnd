namespace TaxVision.Auth.Onboarding.Application.Sessions;

public sealed record OnboardingSessionTicket(string SessionToken, DateTime ExpiresAtUtc, string TokenType = "Bearer");
