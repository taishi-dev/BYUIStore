using CampusAdoptions.Models;
using Byui.ApiClients.Netsuite;
using Microsoft.EntityFrameworkCore;

namespace CampusAdoptions.Data;

/// <summary>
/// Seeds the development database from NetSuite adoption data.
/// Runs MigrateAsync then exits early if CourseRequests already exist.
/// </summary>
public class DbInitializer
{
    private readonly AppDbContext       _context;
    private readonly INetsuiteApiClient _netsuite;

    public DbInitializer(AppDbContext context, INetsuiteApiClient netsuite)
    {
        _context  = context;
        _netsuite = netsuite;
    }

    public async Task SeedAsync()
    {
        // ── 1. Apply any pending EF migrations ──────────────────────────
        await _context.Database.MigrateAsync();

        // ── 2. Skip if data already exists ──────────────────────────────
        if (await _context.CourseRequests.AnyAsync())
            return;

        // ── 3. Resolve a system submitter (MaterialManager preferred) ───
        var submitter =
            await _context.Users.FirstOrDefaultAsync(u => u.Role == "MaterialManager")
            ?? await _context.Users.FirstOrDefaultAsync();

        if (submitter is null)
            return; // No users seeded yet — nothing to do

        // ── 4. Fetch adoptions with textbook info from NetSuite ──────────
        //      Term string format: "Spring2026" (season + year, no space)
        const string termCode = "Spring2026";

        var adoptions = await _netsuite.GetAdoptionsWithTextbookForAutoAccessByTermAsync(termCode);
        if (adoptions is null || adoptions.Count == 0)
            return;

        // ── 5. Group by course (Dept + Course + Section + Term) ─────────
        var courseRequests = adoptions
            .GroupBy(a => new
            {
                Dept    = a.CourseRequestItem.Dept    ?? string.Empty,
                Course  = a.CourseRequestItem.Course  ?? string.Empty,
                Section = a.CourseRequestItem.Section ?? string.Empty,
                Term    = a.CourseRequestItem.Term    ?? termCode,
            })
            .Select(group =>
            {
                var courseNumber = $"{group.Key.Dept} {group.Key.Course}".Trim();

                var request = new CourseRequest
                {
                    SubmitterId  = submitter.Id,
                    ApprovedById = submitter.Id,
                    VerifiedById = submitter.Id,
                    CourseNumber = courseNumber,
                    CourseName   = courseNumber,        // NetSuite adoption records don't carry a title
                    Section      = group.Key.Section,
                    Semester     = group.Key.Term,
                    Status       = RequestStatus.Approved,
                    SubmittedAt  = DateTime.UtcNow,
                    ApprovedAt   = DateTime.UtcNow,
                    VerifiedAt   = DateTime.UtcNow,
                    Items        = group
                        .Where(a => a.Textbook is not null || !string.IsNullOrWhiteSpace(a.CourseRequestItem.Isbn))
                        .Select(a => MapToRequestItem(a))
                        .ToList()
                };

                return request;
            })
            .Where(r => r.Items.Any())
            .ToList();

        if (courseRequests.Count == 0)
            return;

        _context.CourseRequests.AddRange(courseRequests);
        await _context.SaveChangesAsync();
    }

    // ── Mapping helper ───────────────────────────────────────────────────
    private static RequestItem MapToRequestItem(AdoptionWithTextbook adoption)
    {
        var course = adoption.CourseRequestItem;
        var book   = adoption.Textbook;

        // "REQ" = Required, anything else treated as optional
        var isRequired = course.RequirementCode is null
            || course.RequirementCode.Equals("REQ", StringComparison.OrdinalIgnoreCase);

        // Try to parse publication year from Copyright string (e.g. "2024" or "© 2024")
        int? pubYear = null;
        if (book?.Copyright is string copy)
        {
            var digits = new string(copy.Where(char.IsDigit).ToArray());
            if (digits.Length >= 4 && int.TryParse(digits[^4..], out var yr) && yr is >= 1900 and <= 2100)
                pubYear = yr;
        }

        return new RequestItem
        {
            ItemType        = ItemType.Book,
            IsRequired      = isRequired,
            Isbn            = book?.Isbn ?? course.Isbn ?? string.Empty,
            Title           = book?.Title  ?? string.Empty,
            Author          = book?.Author ?? string.Empty,
            Publisher       = book?.Publisher ?? string.Empty,
            Edition         = book?.Edition > 0 ? $"{book.Edition}th Edition" : string.Empty,
            PublicationYear = pubYear,
            Quantity        = course.QunatityRequested > 0 ? course.QunatityRequested : 1,
            Notes           = course.HasAutoAccessContent ? "Auto-access digital content included" : null,
        };
    }
}
