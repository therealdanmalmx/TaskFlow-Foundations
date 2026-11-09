namespace TaskFlow.Api.Tests.Support;

public sealed record TestUser(
    string Token,
    Guid UserId,
    string Username,
    string DisplayName,
    string Password,
    Guid? WorkspaceId,
    Guid? BoardId);
