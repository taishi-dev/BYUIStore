using CampusAdoptions.Data;
using CampusAdoptions.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusAdoptions.Controllers;

[Authorize]
public class DashboardController : BaseAppController
{
    private readonly AppDbContext _db;
    public DashboardController(AppDbContext db) => _db = db;

    // ── Polymorphic overrides ─────────────────────────────────────────────
    public override string GetAreaDisplayName() => "Dashboard";

    public override IEnumerable<string> GetAllowedRoles() =>
        new[] { "OfficeManager", "MaterialManager" };

    // GET /Dashboard  — legacy entry point, dispatches to role-specific action
    public IActionResult Index() => CurrentRole switch
    {
        "Professor"       => RedirectToAction("MyCourses",      "Courses"),
        "OfficeManager"   => RedirectToAction("OfficeManager"),
        "MaterialManager" => RedirectToAction("MaterialManager"),
        _                 => RedirectToAction("Login",           "Account")
    };

    // ── GET /Dashboard/OfficeManager ─────────────────────────────────────
    [Authorize(Roles = "OfficeManager")]
    public async Task<IActionResult> OfficeManager()
    {
        var userId = CurrentUserId;

        var pending = await _db.CourseRequests
            .Include(r => r.Submitter)
            .Include(r => r.Items)
            .Where(r => r.Status == RequestStatus.PendingVerification)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync();

        var mySubmissions = await _db.CourseRequests
            .Include(r => r.Items)
            .Where(r => r.SubmitterId == userId)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync();

        var all = await _db.CourseRequests
            .Include(r => r.Submitter)
            .Include(r => r.Items)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync();

        ViewBag.PendingRequests = pending;
        ViewBag.MySubmissions   = mySubmissions;
        ViewBag.AllRequests     = all;
        return View("OfficeManager");
    }

    // ── GET /Dashboard/MaterialManager ───────────────────────────────────
    [Authorize(Roles = "MaterialManager")]
    public async Task<IActionResult> MaterialManager()
    {
        var today        = DateTime.UtcNow.Date;
        var startOfWeek  = today.AddDays(-(int)today.DayOfWeek);

        ViewBag.CntAwaitingApproval = await _db.CourseRequests
            .CountAsync(r => r.Status == RequestStatus.Verified);

        ViewBag.CntApprovedToday = await _db.CourseRequests
            .CountAsync(r => r.Status == RequestStatus.Approved
                          && r.ApprovedAt.HasValue
                          && r.ApprovedAt.Value.Date == today);

        ViewBag.CntRejectedThisWeek = await _db.CourseRequests
            .CountAsync(r => r.Status == RequestStatus.Rejected
                          && r.ApprovedAt.HasValue
                          && r.ApprovedAt.Value >= startOfWeek);

        var queue = await _db.CourseRequests
            .Include(r => r.Submitter)
            .Include(r => r.VerifiedBy)
            .Include(r => r.Items)
            .Where(r => r.Status == RequestStatus.Verified)
            .OrderBy(r => r.VerifiedAt)   // oldest first (most urgent)
            .ToListAsync();

        return View("MaterialManager", queue);
    }
}
