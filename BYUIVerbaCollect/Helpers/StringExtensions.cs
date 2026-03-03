using System.Text.RegularExpressions;

namespace BYUIVerbaCollect.Helpers;

/// <summary>
/// String helpers used in Razor views.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Converts "PendingVerification" → "Pending Verification"
    /// </summary>
    public static string SplitCamelCase(this string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return Regex.Replace(input, "([A-Z])", " $1").Trim();
    }
}
