namespace TaskFlow.Api.Infrastructure.Routing;

public static class StaticAssetPath
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avif",
        ".css",
        ".eot",
        ".gif",
        ".ico",
        ".jpeg",
        ".jpg",
        ".js",
        ".json",
        ".map",
        ".mjs",
        ".otf",
        ".png",
        ".svg",
        ".ttf",
        ".txt",
        ".wasm",
        ".webmanifest",
        ".webp",
        ".woff",
        ".woff2",
        ".xml",
    };

    public static bool Matches(PathString path) =>
        path.StartsWithSegments("/assets") ||
        Extensions.Contains(Path.GetExtension(path.Value ?? string.Empty));
}
