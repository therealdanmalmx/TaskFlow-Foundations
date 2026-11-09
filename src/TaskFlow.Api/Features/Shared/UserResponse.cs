using TaskFlow.Api.Domain.Users;

namespace TaskFlow.Api.Features.Shared;

public sealed record UserResponse(Guid Id, string Username, string Email, string DisplayName)
{
    public static UserResponse From(User user) =>
        new(user.Id, user.Username, user.Email, user.DisplayName);
}
