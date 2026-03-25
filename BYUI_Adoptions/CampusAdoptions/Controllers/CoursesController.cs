using CampusAdoptions.Data;
using CampusAdoptions.Models;
using CampusAdoptions.Services;
using CampusAdoptions.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CampusAdoptions.Controllers;

[Authorize]
public class CoursesController : Controller
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly IConfiguration _cfg;
    private readonly ILogger<CoursesController> _logger;

    public CoursesController(AppDbContext db, IEmailService email,
        IConfiguration cfg, ILogger<CoursesController> logger)
    {
        _db     = db;
        _email  = email;
        _cfg    = cfg;
        _logger = logger;
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string CurrentRole =>
        User.FindFirstValue(ClaimTypes.Role) ?? "";

    // GET /Courses  — staff (OfficeManager / MaterialManager) see all sections
    public async Task<IActionResult> Index(string? search, string? dept, string? filter)
    {
        // Guard: Professors are scoped to their own courses only
        if (CurrentRole == "Professor")
        {
            TempData["Warning"] = "You can only view your own courses.";
            return RedirectToAction(nameof(MyCourses));
        }

        // ── Fetch catalog courses AND all requests ────────────────────────
        var catalogCourses = await _db.Courses
            .OrderBy(c => c.CourseNumber)
            .ToListAsync();

        var requests = await _db.CourseRequests
            .Include(r => r.Submitter)
            .Include(r => r.Items)
            .ToListAsync();

        var requestsByCourse = requests
            .ToLookup(r => r.CourseNumber, StringComparer.OrdinalIgnoreCase);

        var catalogNumbers = catalogCourses
            .Select(c => c.CourseNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Build groups: catalog courses first (shows ALL courses even without requests)
        var fromCatalog = catalogCourses
            .GroupBy(c => c.CourseNumber, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CourseGroup
            {
                CourseNumber = g.Key,
                CourseName   = g.First().CourseName,
                Sections     = requestsByCourse[g.Key].OrderBy(r => r.Section).ToList(),
            });

        // Then add any requests whose course number isn't in the catalog
        var orphaned = requests
            .Where(r => !catalogNumbers.Contains(r.CourseNumber))
            .GroupBy(r => r.CourseNumber, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CourseGroup
            {
                CourseNumber = g.Key,
                CourseName   = g.First().CourseName,
                Sections     = g.OrderBy(r => r.Section).ToList(),
            });

        var allGroups = fromCatalog.Concat(orphaned)
            .OrderBy(g => g.CourseNumber)
            .ToList();

        // ── Status tab counts (based on requests) ─────────────────────────
        ViewBag.CountIncomplete = requests.Count(r => r.Status == RequestStatus.PendingVerification);
        ViewBag.CountSubmitted  = requests.Count(r => r.Status == RequestStatus.Verified);
        ViewBag.CountReviewed   = requests.Count(r => r.Status == RequestStatus.Verified);
        ViewBag.CountApproved   = requests.Count(r => r.Status == RequestStatus.Approved);
        ViewBag.CountRejected   = requests.Count(r => r.Status == RequestStatus.Rejected);
        ViewBag.CountTotal      = allGroups.Count;   // total unique courses in catalog

        // ── Available departments for filter ──────────────────────────────
        var departments = allGroups
            .Select(g => g.Department)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        ViewBag.Departments = departments;

        // ── Apply status filter on groups ─────────────────────────────────
        IEnumerable<CourseGroup> filtered = filter switch
        {
            "pending"  => allGroups.Where(g => g.Sections.Any(s => s.Status == RequestStatus.PendingVerification)),
            "verified" => allGroups.Where(g => g.Sections.Any(s => s.Status == RequestStatus.Verified)),
            "approved" => allGroups.Where(g => g.Sections.Any(s => s.Status == RequestStatus.Approved)),
            "rejected" => allGroups.Where(g => g.Sections.Any(s => s.Status == RequestStatus.Rejected)),
            _          => allGroups   // "all" shows every catalog course
        };

        // ── Apply department filter ───────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(dept) && dept != "ALL")
            filtered = filtered.Where(g => g.CourseNumber.StartsWith(dept, StringComparison.OrdinalIgnoreCase));

        // ── Apply search ──────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            filtered = filtered.Where(g =>
                g.CourseNumber.ToLower().Contains(s) ||
                g.CourseName.ToLower().Contains(s));
        }

        ViewBag.CurrentSearch = search;
        ViewBag.CurrentDept   = dept;
        ViewBag.CurrentFilter = filter ?? "all";

        return View(filtered.ToList());
    }

    // ── GET /Courses/MyCourses  — Professors see only their own submissions ─
    public async Task<IActionResult> MyCourses(string? search, string? dept, string? filter)
    {
        var userId = CurrentUserId;

        var all = await _db.CourseRequests
            .Include(r => r.Submitter)
            .Include(r => r.Items)
            .Where(r => r.SubmitterId == userId)
            .OrderBy(r => r.CourseNumber)
            .ThenBy(r => r.Section)
            .ToListAsync();

        ViewBag.CountIncomplete = all.Count(r => r.Status == RequestStatus.PendingVerification);
        ViewBag.CountSubmitted  = all.Count(r => r.Status == RequestStatus.Verified);
        ViewBag.CountReviewed   = all.Count(r => r.Status == RequestStatus.Verified);
        ViewBag.CountApproved   = all.Count(r => r.Status == RequestStatus.Approved);
        ViewBag.CountRejected   = all.Count(r => r.Status == RequestStatus.Rejected);
        ViewBag.CountTotal      = all.Count;

        var departments = all
            .Select(r => r.CourseNumber.Contains(' ')
                ? r.CourseNumber.Split(' ')[0].Trim()
                : r.CourseNumber)
            .Distinct().OrderBy(d => d).ToList();
        ViewBag.Departments = departments;

        IEnumerable<CourseRequest> filtered = filter switch
        {
            "pending"  => all.Where(r => r.Status == RequestStatus.PendingVerification),
            "verified" => all.Where(r => r.Status == RequestStatus.Verified),
            "approved" => all.Where(r => r.Status == RequestStatus.Approved),
            "rejected" => all.Where(r => r.Status == RequestStatus.Rejected),
            _          => all
        };

        if (!string.IsNullOrWhiteSpace(dept) && dept != "ALL")
            filtered = filtered.Where(r => r.CourseNumber.StartsWith(dept, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            filtered = filtered.Where(r =>
                r.CourseNumber.ToLower().Contains(s) ||
                r.CourseName.ToLower().Contains(s)   ||
                (r.Section ?? "").ToLower().Contains(s));
        }

        ViewBag.CurrentSearch = search;
        ViewBag.CurrentDept   = dept;
        ViewBag.CurrentFilter = filter ?? "all";
        ViewBag.IsMyCourses   = true;   // drives sidebar + URL generation in view

        var groups = filtered
            .GroupBy(r => r.CourseNumber)
            .Select(g => new CourseGroup
            {
                CourseNumber = g.Key,
                CourseName   = g.First().CourseName,
                Sections     = g.ToList()
            })
            .OrderBy(g => g.CourseNumber)
            .ToList();

        return View("Index", groups);
    }

    // ── GET /Courses/Materials?course=RM+342 ─────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Materials(string? course, string? tab)
    {
        if (string.IsNullOrWhiteSpace(course))
            return RedirectToAction("Index");

        const string currentSemester = "Spring 2026";

        var currentRequest = await _db.CourseRequests
            .Include(r => r.Items)
            .Include(r => r.Submitter)
            .FirstOrDefaultAsync(r =>
                r.CourseNumber == course && r.Semester == currentSemester);

        var priorAdoptions = await _db.CourseRequests
            .Include(r => r.Items)
            .Include(r => r.Submitter)
            .Where(r =>
                r.CourseNumber == course &&
                r.Semester != currentSemester &&
                r.Status == RequestStatus.Approved)
            .OrderByDescending(r => r.Semester)
            .ToListAsync();

        var vm = new CourseMaterialsViewModel
        {
            CourseNumber    = course,
            CourseName      = currentRequest?.CourseName
                              ?? priorAdoptions.FirstOrDefault()?.CourseName
                              ?? course,
            CurrentSemester = currentSemester,
            CurrentRequest  = currentRequest,
            PriorAdoptions  = priorAdoptions,
            ActiveTab       = tab ?? "selected"
        };

        ViewData["Title"]     = course;
        ViewData["PageTitle"] = course;
        return View(vm);
    }

    // ── POST /Courses/RemoveItem — hard delete for pending items ─────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(int itemId, string course)
    {
        var item = await _db.RequestItems
            .Include(i => i.CourseRequest)
            .FirstOrDefaultAsync(i => i.Id == itemId);

        if (item is null) return NotFound();

        var requestId = item.CourseRequestId;
        _db.RequestItems.Remove(item);

        // If this was the last item on the request, delete the request too
        var remainingCount = await _db.RequestItems
            .CountAsync(i => i.CourseRequestId == requestId && i.Id != itemId);
        if (remainingCount == 0)
            _db.CourseRequests.Remove(item.CourseRequest);

        await _db.SaveChangesAsync();
        TempData["Success"] = $"\"{item.Title}\" has been removed from the list.";
        return RedirectToAction("Materials", new { course });
    }

    // ── POST /Courses/RequestRemoval — email manager for approved/verified ─
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestRemoval(int itemId, string course)
    {
        var item = await _db.RequestItems
            .Include(i => i.CourseRequest)
                .ThenInclude(r => r.Submitter)
            .FirstOrDefaultAsync(i => i.Id == itemId);

        if (item is null) return NotFound();

        var professor = item.CourseRequest.Submitter;
        var req       = item.CourseRequest;

        // Mark the item as removal-requested in DB
        item.RemovalRequestedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Build the email
        var managerEmail  = _cfg["Email:ManagerAddress"] ?? "manager@byui.edu";
        var subject       = $"[BYUI Bookstore] Removal Request — {item.Title} from {req.CourseNumber} {req.Semester}";
        var body = $@"<p>Dear Material Manager,</p>

<p>Professor <strong>{professor.FullName}</strong> has requested removal of the following
material from <strong>{req.CourseNumber} — {req.CourseName}</strong>:</p>

<table style=""border-collapse:collapse;width:100%;max-width:520px;font-family:sans-serif;font-size:14px;"">
  <tr><td style=""padding:6px 12px;background:#f5f5f5;font-weight:bold;"">Title</td>
      <td style=""padding:6px 12px;"">{item.Title}</td></tr>
  <tr><td style=""padding:6px 12px;background:#f5f5f5;font-weight:bold;"">Author</td>
      <td style=""padding:6px 12px;"">{item.Author}</td></tr>
  <tr><td style=""padding:6px 12px;background:#f5f5f5;font-weight:bold;"">ISBN</td>
      <td style=""padding:6px 12px;"">{item.Isbn}</td></tr>
  <tr><td style=""padding:6px 12px;background:#f5f5f5;font-weight:bold;"">Course</td>
      <td style=""padding:6px 12px;"">{req.CourseNumber} — {req.CourseName}</td></tr>
  <tr><td style=""padding:6px 12px;background:#f5f5f5;font-weight:bold;"">Section</td>
      <td style=""padding:6px 12px;"">{req.Section ?? "—"}</td></tr>
  <tr><td style=""padding:6px 12px;background:#f5f5f5;font-weight:bold;"">Semester</td>
      <td style=""padding:6px 12px;"">{req.Semester}</td></tr>
  <tr><td style=""padding:6px 12px;background:#f5f5f5;font-weight:bold;"">Requested By</td>
      <td style=""padding:6px 12px;"">{professor.FullName} ({professor.Email})</td></tr>
</table>

<p>Please review and process this removal in the system.</p>

<p>Thank you,<br>
<em>BYUI University Store — VerbaCollect System</em></p>";

        try
        {
            await _email.SendAsync(managerEmail, "Material Manager", subject, body);
            TempData["Success"] = $"Removal request for \"{item.Title}\" has been sent to the material manager.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send removal email for item {ItemId}", itemId);
            TempData["Warning"] = "Removal request was saved but the email failed to send. Please contact the material manager directly.";
        }

        return RedirectToAction("Materials", new { course });
    }
}

public class CourseGroup
{
    public string CourseNumber { get; set; } = "";
    public string CourseName   { get; set; } = "";
    public List<CourseRequest> Sections { get; set; } = new();

    /// <summary>True when the course exists in the catalog but has no submitted request yet.</summary>
    public bool HasNoRequest => !Sections.Any();

    public string Department =>
        CourseNumber.Contains(' ')
            ? CourseNumber.Split(' ')[0].Trim()
            : CourseNumber;

    public RequestStatus OverallStatus =>
        !Sections.Any()                                               ? RequestStatus.PendingVerification :
        Sections.All(s => s.Status == RequestStatus.Approved)        ? RequestStatus.Approved  :
        Sections.Any(s => s.Status == RequestStatus.Rejected)        ? RequestStatus.Rejected  :
        Sections.Any(s => s.Status == RequestStatus.Verified)        ? RequestStatus.Verified  :
        RequestStatus.PendingVerification;
}
