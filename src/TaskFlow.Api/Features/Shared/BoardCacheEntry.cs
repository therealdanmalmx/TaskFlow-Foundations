namespace TaskFlow.Api.Features.Shared;

internal sealed record BoardCacheEntry(
    long BoardRevision,
    BoardDataResponse Board);
