using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CampusAdoptions.Models;

namespace CampusAdoptions.Controllers;

public class HomeController : Controller
{
    /// <summary>
    /// Central home dispatcher — redirects authenticated users to their role-specific landing page.
    /// Add new role branches here; no other files need to change.
    /// </summary>
    [Authorize]
    public IActionResult Index()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        return role switch
        {
            "Professor"       => RedirectToAction("MyCourses",     "Courses"),
            "OfficeManager"   => RedirectToAction("OfficeManager", "Dashboard"),
            "MaterialManager" => RedirectToAction("MaterialManager", "Dashboard"),
            _                 => RedirectToAction("Login",         "Account")
        };
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
