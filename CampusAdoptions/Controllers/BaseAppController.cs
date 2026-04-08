using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CampusAdoptions.Controllers;

/// <summary>
/// Abstract base controller for all authenticated controllers in the app.
/// Provides shared user-context properties and virtual methods that
/// derived controllers override to customise behavior per area.
/// </summary>
public abstract class BaseAppController : Controller
{
    // ── Shared user-context properties ─────────────────────────────────────

    protected int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    protected string CurrentRole =>
        User.FindFirstValue(ClaimTypes.Role) ?? "";

    protected string CurrentUserName =>
        User.FindFirstValue("FullName") ?? User.Identity?.Name ?? "";

    // ── Virtual methods for controller-specific customisation ──────────────

    /// <summary>
    /// Display name shown in breadcrumbs and page titles.
    /// Each derived controller overrides this to return its own area name.
    /// </summary>
    public virtual string GetAreaDisplayName() => "Home";

    /// <summary>
    /// Returns the roles that are allowed to access this controller's primary content.
    /// Override in each controller to declare role-based visibility.
    /// </summary>
    public virtual IEnumerable<string> GetAllowedRoles() =>
        new[] { "Professor", "OfficeManager", "MaterialManager" };

    /// <summary>
    /// Populates common ViewData entries (area name, user display name, role).
    /// Controllers can override to add their own ViewData before views render.
    /// </summary>
    public virtual void SetCommonViewData()
    {
        ViewData["AreaName"]     = GetAreaDisplayName();
        ViewData["UserFullName"] = CurrentUserName;
        ViewData["UserRole"]     = CurrentRole;
    }
}
