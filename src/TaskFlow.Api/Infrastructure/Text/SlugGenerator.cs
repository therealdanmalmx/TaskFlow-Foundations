using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Infrastructure.Persistence;

namespace TaskFlow.Api.Infrastructure.Text;

public static class SlugGenerator
{
    public static async Task<string> CreateUniqueAsync(
        AppDbContext db,
        string name,
        CancellationToken cancellationToken = default)
    {
        var baseSlug = Create(name);
        var candidate = baseSlug;
        var suffix = 2;

        while (await db.Workspaces.AnyAsync(
                   workspace => workspace.Slug == candidate,
                   cancellationToken))
        {
            candidate = $"{baseSlug}-{suffix++}";
        }

        return candidate;
    }

    private static string Create(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousWasSeparator = false;

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "workspace" : result[..Math.Min(result.Length, 80)];
    }
}
