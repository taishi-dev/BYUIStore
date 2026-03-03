using BYUIVerbaCollect.Data;
using BYUIVerbaCollect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BYUIVerbaCollect.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly AppDbContext _db;
    public DashboardController(AppDbContext db) => _db = db;

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string CurrentRole =>
        User.FindFirstValue(ClaimTypes.Role) ?? "";

    // GET /Dashboard
    public async Task<IActionResult> Index()
    {
        var role = CurrentRole;
        var userId = CurrentUserId;

        ViewBag.Role = role;

        if (role == "Professor")
        {
            var myRequests = await _db.CourseRequests
                .Include(r => r.Items)
                .Where(r => r.SubmitterId == userId)
                .OrderByDescending(r => r.SubmittedAt)
                .ToListAsync();

            return View("Professor", myRequests);
        }

        if (role == "OfficeManager")
        {
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

            ViewBag.PendingRequests  = pending;
            ViewBag.MySubmissions    = mySubmissions;
            ViewBag.AllRequests      = all;
            return View("OfficeManager");
        }

        if (role == "BookstoreStaff")
        {
            var verified = await _db.CourseRequests
                .Include(r => r.Submitter)
                .Include(r => r.VerifiedBy)
                .Include(r => r.Items)
                .Where(r => r.Status == RequestStatus.Verified)
                .OrderByDescending(r => r.SubmittedAt)
                .ToListAsync();

            var all = await _db.CourseRequests
                .Include(r => r.Submitter)
                .Include(r => r.VerifiedBy)
                .Include(r => r.ApprovedBy)
                .Include(r => r.Items)
                .OrderByDescending(r => r.SubmittedAt)
                .ToListAsync();

            ViewBag.VerifiedRequests = verified;
            ViewBag.AllRequests      = all;
            return View("BookstoreStaff");
        }

        return RedirectToAction("Login", "Account");
    }
}
