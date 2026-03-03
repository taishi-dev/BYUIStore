using BYUIVerbaCollect.Data;
using BYUIVerbaCollect.Models;
using BYUIVerbaCollect.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BYUIVerbaCollect.Controllers;

[Authorize]
public class RequestsController : Controller
{
    private readonly AppDbContext _db;
    public RequestsController(AppDbContext db) => _db = db;

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string CurrentRole =>
        User.FindFirstValue(ClaimTypes.Role) ?? "";

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
        return View(req);
    }

    // ════════════════════════════════════════════════════════════════════════
    // VERIFY  (Office Manager & Bookstore Staff)
    // ════════════════════════════════════════════════════════════════════════

    [HttpGet]
    [Authorize(Roles = "OfficeManager,BookstoreStaff")]
    public async Task<IActionResult> Verify(int id)
    {
        var req = await LoadRequest(id);
        if (req is null) return NotFound();

        return View(new ReviewRequestViewModel { Request = req, Action = "verify" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "OfficeManager,BookstoreStaff")]
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
    [Authorize(Roles = "BookstoreStaff")]
    public async Task<IActionResult> Approve(int id)
    {
        var req = await LoadRequest(id);
        if (req is null) return NotFound();

        return View(new ReviewRequestViewModel { Request = req, Action = "approve" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "BookstoreStaff")]
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

    // ── helper ────────────────────────────────────────────────────────────
    private Task<CourseRequest?> LoadRequest(int id) =>
        _db.CourseRequests
           .Include(r => r.Submitter)
           .Include(r => r.VerifiedBy)
           .Include(r => r.ApprovedBy)
           .Include(r => r.Items)
           .FirstOrDefaultAsync(r => r.Id == id);
}
