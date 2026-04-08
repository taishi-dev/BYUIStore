namespace CampusAdoptions.Services;

/// <summary>
/// Abstract base class for book lookup services. Provides shared ISBN
/// normalization and ISBN-13 selection logic used by both title-based
/// search (IsbnLookupService) and direct ISBN lookup (IsbnDirectLookupService).
/// </summary>
public abstract class BookLookupServiceBase
{
    protected readonly ILogger _logger;

    protected BookLookupServiceBase(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Human-readable name of this lookup source (for logging).</summary>
    public abstract string ServiceName { get; }

    /// <summary>Strips hyphens, spaces, and whitespace from an ISBN string.</summary>
    protected static string NormalizeIsbn(string isbn) =>
        isbn.Trim().Replace("-", "").Replace(" ", "");

    /// <summary>
    /// From a list of ISBN candidates, prefer ISBN-13 over ISBN-10.
    /// Returns empty string if no valid candidate found.
    /// </summary>
    protected static string SelectBestIsbn(IEnumerable<string> candidates)
    {
        var list = candidates.Where(s => !string.IsNullOrEmpty(s)).ToList();
        return list.FirstOrDefault(s => s.Length == 13)
            ?? list.FirstOrDefault(s => s.Length == 10)
            ?? list.FirstOrDefault()
            ?? "";
    }

    /// <summary>Log a failed lookup with the service name for diagnostics.</summary>
    protected void LogLookupFailure(string identifier, Exception ex) =>
        _logger.LogError(ex, "{Service} lookup failed for '{Identifier}'", ServiceName, identifier);
}
