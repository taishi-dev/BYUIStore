using BYUIVerbaCollect.Data;
using BYUIVerbaCollect.Models;
using BYUIVerbaCollect.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BYUIVerbaCollect.Controllers;

[Authorize]
public class AvailabilityController : Controller
{
    private readonly BookAvailabilityService _svc;
    private readonly IEmailService _email;
    private readonly AppDbContext _db;

    public AvailabilityController(BookAvailabilityService svc, IEmailService email, AppDbContext db)
    {
        _svc = svc; _email = email; _db = db;
    }

    public async Task<IActionResult> Index(string? semester)
    {
        var semesters = await _svc.GetAvailableSemestersAsync();
        ViewBag.Semesters        = semesters;
        ViewBag.SelectedSemester = semester ?? semesters.FirstOrDefault();

        var items = await _svc.GetReportAsync(ViewBag.SelectedSemester as string);

        // ── Auto-email Material Manager notifications ─────────────────────
        // Only send when the MaterialManager role explicitly refreshes the report
        if (User.FindFirstValue(ClaimTypes.Role) == "MaterialManager")
        {
            foreach (var book in items)
            {
                // Find the professor who submitted the request for this book
                var submitter = await _db.CourseRequests
                    .Include(r => r.Submitter)
                    .Where(r => r.CourseNumber == book.CourseNumber &&
                                r.Semester == book.Semester &&
                                r.Status == RequestStatus.Approved)
                    .Select(r => r.Submitter)
                    .FirstOrDefaultAsync();

                if (submitter is null || string.IsNullOrEmpty(submitter.Email)) continue;

                // Email 1: price > $60
                if (book.PriceFlaggedOver60)
                {
                    var price = book.PrintRetailPrice ?? book.PrintListPrice ?? book.EbookPrice ?? 0;
                    var (subj, body) = EmailTemplates.HighPriceAlert(
                        submitter.FullName, book.CourseNumber,
                        book.Title, book.Isbn, price);
                    await _email.SendAsync(submitter.Email, submitter.FullName, subj, body);
                }

                // Email 2: required ↔ optional change vs previous semester
                var prevItem = await _db.RequestItems
                    .Include(ri => ri.CourseRequest)
                    .Where(ri => ri.Isbn == book.Isbn &&
                                 ri.CourseRequest.CourseNumber == book.CourseNumber &&
                                 ri.CourseRequest.Semester != book.Semester &&
                                 (ri.CourseRequest.Status == RequestStatus.Approved ||
                                  ri.CourseRequest.Status == RequestStatus.Verified))
                    .OrderByDescending(ri => ri.CourseRequest.SubmittedAt)
                    .Select(ri => new { ri.IsRequired, ri.CourseRequest.Semester })
                    .FirstOrDefaultAsync();

                if (prevItem is not null && prevItem.IsRequired != book.IsRequired)
                {
                    var (subj, body) = EmailTemplates.RequiredStatusChangeAlert(
                        submitter.FullName, book.CourseNumber, book.Title, book.Isbn,
                        prevItem.Semester, prevItem.IsRequired, book.IsRequired);
                    await _email.SendAsync(submitter.Email, submitter.FullName, subj, body);
                }
            }
        }

        return View(items);
    }

    // GET /Availability/Item?isbn=...
    // The "VIEW ON ITEMS PAGE" detail page for a single book
    public async Task<IActionResult> Item(string isbn)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            return RedirectToAction(nameof(Index));

        var clean = isbn.Replace("-", "").Trim();

        // Run availability check
        var avail = await _svc.CheckSingleIsbnAsync(clean);

        // Load all course requests that use this ISBN
        var adoptions = await _db.CourseRequests
            .Include(r => r.Items)
            .Include(r => r.Submitter)
            .Where(r => r.Items.Any(i => i.Isbn == clean))
            .OrderByDescending(r => r.SubmittedAt)
            .Take(20)
            .ToListAsync();

        // Build term average from all books in the most recent semester that has data
        var semesters = await _svc.GetAvailableSemestersAsync();
        var latestSemester = semesters.FirstOrDefault() ?? "";
        var allBooks = latestSemester != ""
            ? await _svc.GetReportAsync(latestSemester)
            : new List<BookAvailabilityItem>();

        int termAvg = allBooks.Any(b => b.AffordabilityScore.HasValue)
            ? (int)Math.Round(allBooks.Where(b => b.AffordabilityScore.HasValue)
                                       .Average(b => b.AffordabilityScore!.Value))
            : 50;

        ViewBag.Avail          = avail;
        ViewBag.Adoptions      = adoptions;
        ViewBag.TermAvg        = termAvg;
        ViewBag.LatestSemester = latestSemester;
        ViewBag.AllBooks       = allBooks;

        return View(avail);
    }
}
