using System.Net.Mail;
using System.Text.RegularExpressions;

namespace TaskFlow.Api.Features.Shared;

public static partial class InputValidation
{
    public const string DefaultBoardColor = "#2563eb";

    public static string? ValidateUsername(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Username is required.";
        }

        var username = value.Trim();
        if (username.Length is < 3 or > 50)
        {
            return "Username must be between 3 and 50 characters.";
        }

        return UsernamePattern().IsMatch(username)
            ? null
            : "Username can contain only letters, numbers, dots, hyphens, and underscores.";
    }

    public static string? ValidateEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 254)
        {
            return "A valid email address is required.";
        }

        return MailAddress.TryCreate(value.Trim(), out _)
            ? null
            : "A valid email address is required.";
    }

    public static string? ValidateDisplayName(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > 100
            ? "Display name is required and must be 100 characters or fewer."
            : null;

    public static string? ValidatePassword(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Length is < 12 or > 200
            ? "Password must be between 12 and 200 characters."
            : null;

    public static string? ValidateWorkspaceName(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > 100
            ? "Workspace name is required and must be 100 characters or fewer."
            : null;

    public static string? ValidateBoard(
        string? name,
        string? description,
        string? color)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
        {
            return "Board name is required and must be 100 characters or fewer.";
        }

        if ((description?.Trim().Length ?? 0) > 2_000)
        {
            return "Board description must be 2,000 characters or fewer.";
        }

        return string.IsNullOrWhiteSpace(color) || !ColorPattern().IsMatch(color.Trim())
            ? "Color must be a six-digit hexadecimal value such as #2563eb."
            : null;
    }

    public static string? ValidateTask(string? title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
        {
            return "Title is required and must be 200 characters or fewer.";
        }

        return (description?.Trim().Length ?? 0) > 4_000
            ? "Description must be 4,000 characters or fewer."
            : null;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}
