using BYUIVerbaCollect.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BYUIVerbaCollect.Controllers;

[Authorize]
public class AvailabilityController : Controller
{
    private readonly BookAvailabilityService _svc;

    public AvailabilityController(BookAvailabilityService svc) => _svc = svc;

    public async Task<IActionResult> Index(string? semester)
    {
        var semesters = await _svc.GetAvailableSemestersAsync();
        ViewBag.Semesters      = semesters;
        ViewBag.SelectedSemester = semester ?? semesters.FirstOrDefault();

        var items = await _svc.GetReportAsync(ViewBag.SelectedSemester as string);
        return View(items);
    }
}
