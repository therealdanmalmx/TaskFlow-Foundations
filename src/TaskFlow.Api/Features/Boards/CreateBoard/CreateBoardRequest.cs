namespace TaskFlow.Api.Features.Boards.CreateBoard;

public sealed record CreateBoardRequest(string? Name, string? Description, string? Color);
