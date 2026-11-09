namespace TaskFlow.Api.Features.Shared;

internal sealed record BoardStatsCacheEntry(
    long BoardRevision,
    BoardStatsResponse Stats);
