namespace TaskFlow.Api.Features.Shared;

public sealed record SessionResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    string Username,
    UserResponse User);
