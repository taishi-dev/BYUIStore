using CampusAdoptions.Data;
using CampusAdoptions.Models;
using CampusAdoptions.Services;
using CampusAdoptions.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CampusAdoptions.Controllers;

[Authorize]
public class RequestsController : BaseAppController
{
    private readonly AppDbContext _db;
    private readonly BookAvailabilityService _availService;

    public RequestsController(AppDbContext db, BookAvailabilityService availService)
    {
        _db = db;
        _availService = availService;
    }

    // ── Polymorphic overrides ─────────────────────────────────────────────
    public override string GetAreaDisplayName() => "Requests";

    public override IEnumerable<string> GetAllowedRoles() =>
        new[] { "Professor", "OfficeManager", "MaterialManager" };

    // ════════════════════════════════════════════════════════════════════════
    // SUBMIT  (Professors & Office Managers)
    // ════════════════════════════════════════════════════════════════════════

    [HttpGet]
    [Authorize(Roles = "Professor,OfficeManager")]
    public IActionResult Submit()
    {
        return View(new SubmitRequestViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "Professor,OfficeManager")]
    public async Task<IActionResult> Submit(SubmitRequestViewModel model)
    {
        // Remove item-level validation errors for empty items (JS adds/removes rows)
        var keysToRemove = ModelState.Keys
            .Where(k => k.StartsWith("Items[") && ModelState[k]!.Errors.Count > 0)
            .ToList();

        // Re-validate
        if (!ModelState.IsValid)
            return View(model);

        if (model.Items.Count == 0)
        {
            ModelState.AddModelError("", "Please add at least one book or supply item.");
            return View(model);
        }

        var req = new CourseRequest
        {
            SubmitterId   = CurrentUserId,
            CourseName    = model.CourseName,
            CourseNumber  = model.CourseNumber,
            Semester      = model.Semester,
            Section       = model.Section,
            SubmittedAt   = DateTime.UtcNow,
            Status        = RequestStatus.PendingVerification
        };

        foreach (var item in model.Items)
        {
            var ri = new RequestItem
            {
                ItemType    = item.ItemType == "Supply" ? ItemType.Supply : ItemType.Book,
                Quantity    = item.Quantity,
                IsRequired  = item.IsRequired,
                Notes       = item.Notes
            };

            if (ri.ItemType == ItemType.Book)
            {
                ri.Title           = item.Title;
                ri.Author          = item.Author;
                ri.Isbn            = item.Isbn;
                ri.Publisher       = item.Publisher;
                ri.Edition         = item.Edition;
                ri.PublicationYear = item.PublicationYear;
            }
            else
            {
                ri.SupplyDescription = item.SupplyDescription;
            }

            req.Items.Add(ri);
        }

        _db.CourseRequests.Add(req);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Request submitted successfully!";
        return RedirectToAction("Index", "Dashboard");
    }

    // ════════════════════════════════════════════════════════════════════════
    // DETAILS
    // ════════════════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var req = await _db.CourseRequests
            .Include(r => r.Submitter)
            .Include(r => r.VerifiedBy)
            .Include(r => r.ApprovedBy)
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (req is null) return NotFound();

        // Load previous semester adoptions for the same course (for COPY ANOTHER ADOPTION tab)
        var prevAdoptions = await _db.CourseRequests
            .Include(r => r.Items)
            .Include(r => r.Submitter)
            .Where(r => r.CourseNumber == req.CourseNumber
                     && r.Id != req.Id
                     && (r.Status == RequestStatus.Approved
                         || r.Status == RequestStatus.Verified
                         || r.Status == RequestStatus.PendingVerification))
            .OrderByDescending(r => r.SubmittedAt)
            .Take(5)
            .ToListAsync();

        ViewBag.PreviousAdoptions = prevAdoptions;

        // ── Pre-load availability cache so checkmarks render instantly ─────────
        // For each book ISBN, check our in-memory cache (no network call).
        // On cache hit the result is embedded in the HTML as a JS object so the
        // browser can apply checkmarks immediately — before any async fetch fires.
        var cachedAvail = new Dictionary<string, object>();
        foreach (var item in req.Items.Where(i => i.ItemType == ItemType.Book
                                                   && !string.IsNullOrWhiteSpace(i.Isbn)))
        {
            var isbn = item.Isbn!.Replace("-", "").Trim();
            var cached = _availService.TryGetFromCache(isbn);
            if (cached is not null)
            {
                cachedAvail[isbn] = new
                {
                    digitalVitalSource = cached.DigitalAvailableOnVitalSource,
                    digitalGoogle      = cached.EbookAvailableOnGoogle,
                    googlePrice        = cached.EbookPrice,
                    printPrice         = cached.PrintRetailPrice ?? cached.PrintListPrice,
                    vitalSourcePrice   = cached.VitalSourcePrice,
                    amazonUrl          = cached.AmazonUrl,
                    vitalsourceUrl     = cached.VitalSourceUrl,
                    googleBuyLink      = cached.GoogleBuyLink,
                    coverThumbnail     = cached.CoverThumbnailUrl
                };
            }
        }

        ViewBag.AvailabilityCache = JsonSerializer.Serialize(cachedAvail);
        return View(req);
    }

    // ════════════════════════════════════════════════════════════════════════
    // VERIFY  (Office Manager & Bookstore Staff)
    // ════════════════════════════════════════════════════════════════════════

    [HttpGet]
    [Authorize(Roles = "OfficeManager,MaterialManager")]
    public async Task<IActionResult> Verify(int id)
    {
        var req = await LoadRequest(id);
        if (req is null) return NotFound();

        return View(new ReviewRequestViewModel { Request = req, Action = "verify" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "OfficeManager,MaterialManager")]
    public async Task<IActionResult> Verify(int id, string action, string? rejectionNote)
    {
        var req = await LoadRequest(id);
        if (req is null) return NotFound();

        if (action == "verify")
        {
            req.Status        = RequestStatus.Verified;
            req.VerifiedById  = CurrentUserId;
            req.VerifiedAt    = DateTime.UtcNow;
            TempData["Success"] = "Request verified successfully.";
        }
        else
        {
            req.Status        = RequestStatus.Rejected;
            req.RejectionNote = rejectionNote;
            TempData["Warning"] = "Request rejected.";
        }

        await _db.SaveChangesAsync();
        return RedirectToAction("Index", "Dashboard");
    }

    // ════════════════════════════════════════════════════════════════════════
    // APPROVE  (Bookstore Staff only)
    // ════════════════════════════════════════════════════════════════════════

    [HttpGet]
    [Authorize(Roles = "MaterialManager")]
    public async Task<IActionResult> Approve(int id)
    {
        var req = await LoadRequest(id);
        if (req is null) return NotFound();

        return View(new ReviewRequestViewModel { Request = req, Action = "approve" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "MaterialManager")]
    public async Task<IActionResult> Approve(int id, string action, string? rejectionNote)
    {
        var req = await LoadRequest(id);
        if (req is null) return NotFound();

        if (action == "approve")
        {
            req.Status        = RequestStatus.Approved;
            req.ApprovedById  = CurrentUserId;
            req.ApprovedAt    = DateTime.UtcNow;
            TempData["Success"] = "Request approved!";

            // Persist approved ISBNs into the CourseBookAssignment catalog
            foreach (var item in req.Items.Where(i => i.ItemType == ItemType.Book && !string.IsNullOrEmpty(i.Isbn)))
            {
                var existing = await _db.CourseBookAssignments
                    .FirstOrDefaultAsync(b => b.Isbn == item.Isbn);

                if (existing is null)
                {
                    // Try to match a Course record for this request
                    var course = await _db.Courses
                        .FirstOrDefaultAsync(c => c.CourseNumber == req.CourseNumber && c.Semester == req.Semester);

                    if (course is not null)
                    {
                        _db.CourseBookAssignments.Add(new CourseBookAssignment
                        {
                            CourseId              = course.Id,
                            Isbn                  = item.Isbn!,
                            Title                 = item.Title,
                            Author                = item.Author,
                            Publisher             = item.Publisher,
                            Edition               = item.Edition,
                            PublicationYear       = item.PublicationYear,
                            IsRequired            = item.IsRequired,
                            AssignedFromRequestId = req.Id
                        });
                    }
                }
            }
        }
        else
        {
            req.Status        = RequestStatus.Rejected;
            req.RejectionNote = rejectionNote;
            TempData["Warning"] = "Request rejected.";
        }

        await _db.SaveChangesAsync();
        return RedirectToAction("Index", "Dashboard");
    }

    // ════════════════════════════════════════════════════════════════════════
    // ADD SECTION  (from COURSE ACTIONS → Add Section modal)
    // ════════════════════════════════════════════════════════════════════════

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSection(int id,
        string section, string? semester, string? courseName,
        string? courseNumber, string? instructorName,
        string? notes, bool copyMaterials = false,
        List<ItemViewModel>? NewItems = null)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            TempData["Error"] = "Section number is required.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var source = await _db.CourseRequests
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (source is null) return NotFound();

        var newReq = new CourseRequest
        {
            SubmitterId  = CurrentUserId,
            CourseName   = courseName?.Trim()   ?? source.CourseName,
            CourseNumber = courseNumber?.Trim()  ?? source.CourseNumber,
            Semester     = semester?.Trim()      ?? source.Semester,
            Section      = section.Trim(),
            SubmittedAt  = DateTime.UtcNow,
            Status       = RequestStatus.PendingVerification
        };

        // Optionally copy all materials from the source section
        if (copyMaterials)
        {
            foreach (var item in source.Items)
            {
                newReq.Items.Add(new RequestItem
                {
                    ItemType         = item.ItemType,
                    Isbn             = item.Isbn,
                    Title            = item.Title,
                    Author           = item.Author,
                    Publisher        = item.Publisher,
                    Edition          = item.Edition,
                    PublicationYear  = item.PublicationYear,
                    IsRequired       = item.IsRequired,
                    Quantity         = item.Quantity,
                    Notes            = item.Notes,
                    SupplyDescription = item.SupplyDescription
                });
            }
        }

        // Add brand-new materials entered directly in the Add Section modal
        if (NewItems != null)
        {
            foreach (var item in NewItems)
            {
                var isBook   = item.ItemType != "Supply";
                var hasBook  = isBook && !string.IsNullOrWhiteSpace(item.Title);
                var hasSupply = !isBook && !string.IsNullOrWhiteSpace(item.SupplyDescription);

                if (!hasBook && !hasSupply) continue;   // skip empty rows

                newReq.Items.Add(new RequestItem
                {
                    ItemType          = isBook ? ItemType.Book : ItemType.Supply,
                    Isbn              = item.Isbn?.Trim(),
                    Title             = item.Title?.Trim(),
                    Author            = item.Author?.Trim(),
                    Publisher         = item.Publisher?.Trim(),
                    Edition           = item.Edition?.Trim(),
                    PublicationYear   = item.PublicationYear,
                    IsRequired        = item.IsRequired,
                    Quantity          = item.Quantity > 0 ? item.Quantity : 1,
                    Notes             = item.Notes?.Trim(),
                    SupplyDescription = item.SupplyDescription?.Trim()
                });
            }
        }

        // Attach any instructor name as a note on the new request
        if (!string.IsNullOrWhiteSpace(instructorName))
        {
            newReq.Items.Add(new RequestItem
            {
                ItemType = ItemType.Supply,
                SupplyDescription = $"Instructor: {instructorName.Trim()}",
                Notes    = notes?.Trim(),
                Quantity = 0,
                IsRequired = false
            });
        }

        _db.CourseRequests.Add(newReq);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Section '{section}' added successfully.";
        return RedirectToAction(nameof(Details), new { id = newReq.Id });
    }

    // ════════════════════════════════════════════════════════════════════════
    // ADD ITEM  (inline from Details page)
    // ════════════════════════════════════════════════════════════════════════

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(int id,
        string? isbn, string? title, string? author,
        string? publisher, string? edition, int? publicationYear,
        bool isRequired = true)
    {
        var req = await _db.CourseRequests
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (req is null) return NotFound();

        if (string.IsNullOrWhiteSpace(isbn) && string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "Please provide at least an ISBN or title.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var item = new RequestItem
        {
            ItemType        = ItemType.Book,
            Isbn            = isbn?.Trim(),
            Title           = title?.Trim(),
            Author          = author?.Trim(),
            Publisher       = publisher?.Trim(),
            Edition         = edition?.Trim(),
            PublicationYear = publicationYear,
            IsRequired      = isRequired,
            Quantity        = 1
        };

        req.Items.Add(item);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"'{item.Title ?? item.Isbn}' added to the materials list.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ════════════════════════════════════════════════════════════════════════
    // REMOVE ITEM  (from Details page)
    // ════════════════════════════════════════════════════════════════════════

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(int id, int itemId)
    {
        // Verify the item belongs to this request
        var item = await _db.RequestItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.CourseRequestId == id);

        if (item is null) return NotFound();

        _db.RequestItems.Remove(item);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"'{item.Title ?? item.SupplyDescription ?? "Item"}' removed from the materials list.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ════════════════════════════════════════════════════════════════════════
    // DELETE REQUEST  (from Dashboard — any authenticated user who owns it,
    //                  or OfficeManager / BookstoreStaff for any request)
    // ════════════════════════════════════════════════════════════════════════

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRequest(int id)
    {
        var req = await _db.CourseRequests
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (req is null) return NotFound();

        // Ownership check: Professors may only delete their own requests.
        // OfficeManagers and BookstoreStaff may delete any.
        var role = CurrentRole;
        if (role == "Professor" && req.SubmitterId != CurrentUserId)
            return Forbid();

        var label = $"{req.CourseNumber} — {req.Semester}";

        // Remove child items first (cascade-on-delete is not always configured)
        _db.RequestItems.RemoveRange(req.Items);
        _db.CourseRequests.Remove(req);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Course request '{label}' has been deleted.";
        return RedirectToAction("Index", "Dashboard");
    }

    // ── helper ────────────────────────────────────────────────────────────
    private Task<CourseRequest?> LoadRequest(int id) =>
        _db.CourseRequests
           .Include(r => r.Submitter)
           .Include(r => r.VerifiedBy)
           .Include(r => r.ApprovedBy)
           .Include(r => r.Items)
           .FirstOrDefaultAsync(r => r.Id == id);
}
