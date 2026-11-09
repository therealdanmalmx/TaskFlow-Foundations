namespace TaskFlow.Api.Infrastructure.Routing;

public static class ApiBoundaryPath
{
    public static bool Matches(PathString path, string method)
    {
        if (path.StartsWithSegments("/api") || path.StartsWithSegments("/health"))
        {
            return true;
        }

        if (!path.StartsWithSegments("/auth", out var remaining))
        {
            return false;
        }

        if (remaining.HasValue && remaining != "/")
        {
            return true;
        }

        // /auth is a client-side route only when a browser navigates to it.
        // Mutating requests to the same path still belong to the API boundary.
        return !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method);
    }
}
