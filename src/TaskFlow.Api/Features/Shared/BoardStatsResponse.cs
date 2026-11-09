namespace TaskFlow.Api.Features.Shared;

public sealed record BoardStatsResponse(
    int Todo,
    int Doing,
    int Done,
    int Total,
    int DoneToday,
    double? OldestActiveAgeDays,
    int Overdue);
