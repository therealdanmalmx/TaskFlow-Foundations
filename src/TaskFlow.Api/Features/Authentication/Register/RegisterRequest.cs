namespace TaskFlow.Api.Features.Authentication.Register;

public sealed record RegisterRequest(
    string? Username,
    string? Email,
    string? DisplayName,
    string? Password);
