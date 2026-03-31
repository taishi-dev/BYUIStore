using CampusAdoptions.Data;
using CampusAdoptions.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusAdoptions.Services;

/// <summary>
/// Compares course material adoptions between the current semester and past
/// semesters (last semester, 1 year ago, 2 years ago) to detect meaningful
/// changes that should trigger email notifications to professors,
/// material managers, or course managers.
///
/// Inspired by the autoresearch experiment loop — iteratively compares
/// each course's adoption against its historical baseline and flags diffs.
/// </summary>
public class AdoptionDiffService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AdoptionDiffService> _logger;

    public AdoptionDiffService(AppDbContext db, ILogger<AdoptionDiffService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Compares adoptions between two specific semesters.
    /// Returns all detected differences grouped by course.
    /// </summary>
    public async Task<AdoptionDiffResult> CompareAsync(string currentSemester, string pastSemester)
    {
        var currentAdoptions = await GetAdoptionsBySemesterAsync(currentSemester);
        var pastAdoptions = await GetAdoptionsBySemesterAsync(pastSemester);

        var diffs = DetectDiffs(currentAdoptions, pastAdoptions, currentSemester, pastSemester);

        return new AdoptionDiffResult
        {
            CurrentSemester = currentSemester,
            PastSemester = pastSemester,
            Diffs = diffs
        };
    }

    /// <summary>
    /// Compares the current semester against multiple past semesters
    /// (last semester, 1 year ago, 2 years ago) and returns the combined
    /// diffs, deduplicated by keeping the most recent comparison.
    /// </summary>
    public async Task<List<AdoptionDiffResult>> CompareAgainstHistoryAsync(
        string currentSemester, IEnumerable<string> pastSemesters)
    {
        var results = new List<AdoptionDiffResult>();
        foreach (var past in pastSemesters)
        {
            var result = await CompareAsync(currentSemester, past);
            if (result.Diffs.Count > 0)
                results.Add(result);
        }
        return results;
    }

    /// <summary>
    /// Determines which email recipients should be notified based on the
    /// types of diffs detected. Returns a list of notification actions.
    /// </summary>
    public List<AdoptionNotification> DetermineNotifications(AdoptionDiffResult diffResult)
    {
        var notifications = new List<AdoptionNotification>();

        foreach (var diff in diffResult.Diffs)
        {
            var notification = diff.Type switch
            {
                // Professor gets notified about their materials changing
                DiffType.MaterialAdded => new AdoptionNotification
                {
                    Recipient = NotificationRecipient.Professor,
                    Diff = diff,
                    Subject = $"[BYUI Bookstore] New Material Added — {diff.CurrentTitle}",
                    Reason = $"A new material was added to {diff.CourseNumber} that was not " +
                             $"in {diff.PastSemester}. Please confirm this adoption."
                },

                DiffType.MaterialRemoved => new AdoptionNotification
                {
                    Recipient = NotificationRecipient.MaterialManager,
                    Diff = diff,
                    Subject = $"[BYUI Bookstore] Material Removed — {diff.PastTitle}",
                    Reason = $"Material '{diff.PastTitle}' (ISBN: {diff.PastIsbn}) was in " +
                             $"{diff.PastSemester} but is missing from {diff.CurrentSemester} " +
                             $"for {diff.CourseNumber}. Verify inventory adjustments."
                },

                DiffType.RequiredStatusChanged => new AdoptionNotification
                {
                    Recipient = NotificationRecipient.Professor,
                    Diff = diff,
                    Subject = $"[BYUI Bookstore] Required Status Changed — {diff.CurrentTitle}",
                    Reason = $"Material '{diff.CurrentTitle}' changed from " +
                             $"{(diff.PastIsRequired == true ? "Required" : "Optional")} to " +
                             $"{(diff.CurrentIsRequired == true ? "Required" : "Optional")} " +
                             $"compared to {diff.PastSemester}. Please confirm this is intentional."
                },

                DiffType.MaterialChanged => new AdoptionNotification
                {
                    Recipient = NotificationRecipient.MaterialManager,
                    Diff = diff,
                    Subject = $"[BYUI Bookstore] Material Swap Detected — {diff.CourseNumber}",
                    Reason = $"Course {diff.CourseNumber} swapped '{diff.PastTitle}' " +
                             $"(ISBN: {diff.PastIsbn}) for '{diff.CurrentTitle}' " +
                             $"(ISBN: {diff.CurrentIsbn}). Update inventory accordingly."
                },

                DiffType.EditionChanged => new AdoptionNotification
                {
                    Recipient = NotificationRecipient.CourseManager,
                    Diff = diff,
                    Subject = $"[BYUI Bookstore] Edition Change — {diff.CurrentTitle}",
                    Reason = $"Edition changed from '{diff.PastEdition}' to " +
                             $"'{diff.CurrentEdition}' for {diff.CourseNumber}. " +
                             $"Coordinate with other sections using the same title."
                },

                DiffType.PriceIncreased => new AdoptionNotification
                {
                    Recipient = NotificationRecipient.Professor,
                    Diff = diff,
                    Subject = $"[BYUI Bookstore] Price Increase Alert — {diff.CurrentTitle}",
                    Reason = diff.Description
                },

                _ => null
            };

            if (notification != null)
                notifications.Add(notification);
        }

        return notifications;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // INTERNAL HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    internal async Task<Dictionary<string, List<RequestItem>>> GetAdoptionsBySemesterAsync(string semester)
    {
        var requests = await _db.CourseRequests
            .Include(r => r.Items)
            .Where(r => r.Semester == semester &&
                        r.Status != RequestStatus.Rejected)
            .ToListAsync();

        // Group items by CourseNumber (normalize key)
        return requests
            .GroupBy(r => r.CourseNumber.Trim().ToUpperInvariant())
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(r => r.Items)
                      .Where(i => i.ItemType == ItemType.Book && !string.IsNullOrWhiteSpace(i.Isbn))
                      .ToList()
            );
    }

    internal List<AdoptionDiff> DetectDiffs(
        Dictionary<string, List<RequestItem>> current,
        Dictionary<string, List<RequestItem>> past,
        string currentSemester,
        string pastSemester)
    {
        var diffs = new List<AdoptionDiff>();
        var allCourses = current.Keys.Union(past.Keys).Distinct();

        foreach (var course in allCourses)
        {
            var currentItems = current.GetValueOrDefault(course) ?? new List<RequestItem>();
            var pastItems = past.GetValueOrDefault(course) ?? new List<RequestItem>();

            var currentIsbns = currentItems.Select(i => NormalizeIsbn(i.Isbn!)).ToHashSet();
            var pastIsbns = pastItems.Select(i => NormalizeIsbn(i.Isbn!)).ToHashSet();

            // Materials added (in current but not in past)
            foreach (var item in currentItems.Where(i => !pastIsbns.Contains(NormalizeIsbn(i.Isbn!))))
            {
                // Check if this is a material swap (same title, different ISBN)
                var matchByTitle = pastItems.FirstOrDefault(p =>
                    TitlesMatch(p.Title, item.Title));

                if (matchByTitle != null)
                {
                    // Check if it's an edition change
                    if (!string.IsNullOrWhiteSpace(item.Edition) &&
                        !string.IsNullOrWhiteSpace(matchByTitle.Edition) &&
                        item.Edition != matchByTitle.Edition)
                    {
                        diffs.Add(new AdoptionDiff
                        {
                            Type = DiffType.EditionChanged,
                            CourseNumber = course,
                            CurrentSemester = currentSemester,
                            PastSemester = pastSemester,
                            CurrentIsbn = item.Isbn,
                            CurrentTitle = item.Title,
                            CurrentEdition = item.Edition,
                            PastIsbn = matchByTitle.Isbn,
                            PastTitle = matchByTitle.Title,
                            PastEdition = matchByTitle.Edition,
                            Description = $"Edition changed: '{matchByTitle.Edition}' → '{item.Edition}' for '{item.Title}'"
                        });
                    }
                    else
                    {
                        diffs.Add(new AdoptionDiff
                        {
                            Type = DiffType.MaterialChanged,
                            CourseNumber = course,
                            CurrentSemester = currentSemester,
                            PastSemester = pastSemester,
                            CurrentIsbn = item.Isbn,
                            CurrentTitle = item.Title,
                            PastIsbn = matchByTitle.Isbn,
                            PastTitle = matchByTitle.Title,
                            Description = $"Material swapped: '{matchByTitle.Title}' (ISBN: {matchByTitle.Isbn}) → '{item.Title}' (ISBN: {item.Isbn})"
                        });
                    }
                }
                else
                {
                    diffs.Add(new AdoptionDiff
                    {
                        Type = DiffType.MaterialAdded,
                        CourseNumber = course,
                        CurrentSemester = currentSemester,
                        PastSemester = pastSemester,
                        CurrentIsbn = item.Isbn,
                        CurrentTitle = item.Title,
                        CurrentEdition = item.Edition,
                        CurrentIsRequired = item.IsRequired,
                        Description = $"New material added: '{item.Title}' (ISBN: {item.Isbn})"
                    });
                }
            }

            // Materials removed (in past but not in current)
            foreach (var item in pastItems.Where(i => !currentIsbns.Contains(NormalizeIsbn(i.Isbn!))))
            {
                // Skip if already accounted for as a swap/edition change
                var alreadyMatched = currentItems.Any(c => TitlesMatch(c.Title, item.Title));
                if (alreadyMatched) continue;

                diffs.Add(new AdoptionDiff
                {
                    Type = DiffType.MaterialRemoved,
                    CourseNumber = course,
                    CurrentSemester = currentSemester,
                    PastSemester = pastSemester,
                    PastIsbn = item.Isbn,
                    PastTitle = item.Title,
                    PastEdition = item.Edition,
                    PastIsRequired = item.IsRequired,
                    Description = $"Material removed: '{item.Title}' (ISBN: {item.Isbn}) was in {pastSemester}"
                });
            }

            // Required/Optional status changes (same ISBN in both semesters)
            foreach (var currentItem in currentItems)
            {
                var isbn = NormalizeIsbn(currentItem.Isbn!);
                var pastItem = pastItems.FirstOrDefault(p => NormalizeIsbn(p.Isbn!) == isbn);
                if (pastItem == null) continue;

                if (currentItem.IsRequired != pastItem.IsRequired)
                {
                    diffs.Add(new AdoptionDiff
                    {
                        Type = DiffType.RequiredStatusChanged,
                        CourseNumber = course,
                        CurrentSemester = currentSemester,
                        PastSemester = pastSemester,
                        CurrentIsbn = currentItem.Isbn,
                        CurrentTitle = currentItem.Title,
                        CurrentIsRequired = currentItem.IsRequired,
                        PastIsbn = pastItem.Isbn,
                        PastTitle = pastItem.Title,
                        PastIsRequired = pastItem.IsRequired,
                        Description = $"Status changed: '{currentItem.Title}' was {(pastItem.IsRequired ? "Required" : "Optional")} in {pastSemester}, now {(currentItem.IsRequired ? "Required" : "Optional")}"
                    });
                }
            }
        }

        return diffs;
    }

    private static string NormalizeIsbn(string isbn) =>
        isbn.Replace("-", "").Replace(" ", "").Trim();

    private static bool TitlesMatch(string? titleA, string? titleB)
    {
        if (string.IsNullOrWhiteSpace(titleA) || string.IsNullOrWhiteSpace(titleB))
            return false;

        var a = NormalizeTitle(titleA);
        var b = NormalizeTitle(titleB);
        return a == b || a.Contains(b) || b.Contains(a);
    }

    private static string NormalizeTitle(string title) =>
        title.Trim().ToLowerInvariant()
             .Replace(":", "").Replace("-", " ")
             .Replace("  ", " ");
}

// ═══════════════════════════════════════════════════════════════════════
// Notification model
// ═══════════════════════════════════════════════════════════════════════

public enum NotificationRecipient
{
    Professor,
    MaterialManager,
    CourseManager
}

public class AdoptionNotification
{
    public NotificationRecipient Recipient { get; set; }
    public AdoptionDiff Diff { get; set; } = null!;
    public string Subject { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
