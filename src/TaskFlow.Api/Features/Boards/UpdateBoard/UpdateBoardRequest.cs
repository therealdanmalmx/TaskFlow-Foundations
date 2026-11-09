namespace TaskFlow.Api.Features.Boards.UpdateBoard;

public sealed record UpdateBoardRequest(string? Name, string? Description, string? Color);
