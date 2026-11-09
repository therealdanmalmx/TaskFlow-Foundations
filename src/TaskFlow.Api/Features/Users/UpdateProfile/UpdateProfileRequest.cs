namespace TaskFlow.Api.Features.Users.UpdateProfile;

public sealed record UpdateProfileRequest(string? DisplayName, string? Email);
