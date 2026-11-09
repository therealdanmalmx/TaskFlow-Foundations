using TaskFlow.Api.Infrastructure.Caching;

namespace TaskFlow.Api.Features.Shared;

public sealed record ActivityResponse(bool Available, IReadOnlyList<ActivityEntry> Entries);
