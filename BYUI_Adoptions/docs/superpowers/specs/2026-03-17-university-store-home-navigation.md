# UNIVERSITY STORE Brand Link — Home Navigation Design

**Date:** 2026-03-17  
**Status:** Approved  
**Scope:** Make "UNIVERSITY STORE" in the header navigate to the Courses page for all roles, and make Courses the post-login landing page.

---

## Problem

Clicking "UNIVERSITY STORE" in the top-left header currently redirects to `Dashboard/Index`, which routes users to role-specific dashboard views. The product requirement is for it to show the Courses list (as shown in the attached mockup), for all roles.

Additionally, after login, all roles should land on `Courses/Index` instead of their role-specific dashboards.

---

## Decision

**Option C — HomeController dispatcher** was chosen over minimal redirects (Option A) because it centralises all home-routing logic in a single place. Future per-role home destinations (e.g. Professors landing on a personal submissions view) require a single `if` statement in one file rather than changes scattered across `_Layout.cshtml` and `AccountController`.

---

## Design

### 1. `HomeController.cs` — dispatcher action

Add an `Index` action to the existing `HomeController`. It is decorated with `[Authorize]` so unauthenticated users are transparently redirected to the login page by the framework.

```csharp
[Authorize]
public IActionResult Index()
{
    // Future per-role routing goes here, e.g.:
    // var role = User.FindFirst(ClaimTypes.Role)?.Value;
    // if (role == "Professor") return RedirectToAction("Professor", "Dashboard");
    return RedirectToAction("Index", "Courses");
}
```

- No view is rendered — this is a pure redirect dispatcher.
- All roles currently dispatch to `Courses/Index`.
- The role claim is already available via `User.FindFirst(ClaimTypes.Role)` when future branching is needed.

### 2. `_Layout.cshtml` — brand link target

Change the `UNIVERSITY STORE` anchor from:
```html
<a asp-controller="Dashboard" asp-action="Index" …>
```
to:
```html
<a asp-controller="Home" asp-action="Index" …>
```

One attribute change. No visual change to the header.

### 3. `AccountController.cs` — two redirect targets

**After successful login** (POST handler, line ~51):
```csharp
// Before:
return RedirectToAction("Index", "Dashboard");
// After:
return RedirectToAction("Index", "Home");
```

**When an already-authenticated user hits `/Account/Login`** (GET handler, line ~16):
```csharp
// Before:
return RedirectToAction("Index", "Dashboard");
// After:
return RedirectToAction("Index", "Home");
```

### 4. `Program.cs` — no change

The default route remains `{controller=Account}/{action=Login}` so that unauthenticated direct navigation to `/` shows the login page rather than triggering a redirect loop through `Home/Index`.

---

## Files Changed

| File | Change | Lines |
|---|---|---|
| `Controllers/HomeController.cs` | Add `[Authorize] Index()` dispatcher | ~8 lines added |
| `Views/Shared/_Layout.cshtml` | `asp-controller` on brand link | 1 line |
| `Controllers/AccountController.cs` | 2 redirect targets | 2 lines |

---

## Data Flow

```
User clicks "UNIVERSITY STORE"
  → GET /Home/Index
      → [Authorize] passes (authenticated)
      → RedirectToAction("Index", "Courses")
          → GET /Courses  ← Courses list page shown

User submits login form
  → POST /Account/Login
      → credentials verified
      → RedirectToAction("Index", "Home")
          → GET /Home/Index
              → RedirectToAction("Index", "Courses")
                  → GET /Courses  ← same result

Already-authenticated user navigates to /Account/Login
  → GET /Account/Login
      → already authenticated check fires
      → RedirectToAction("Index", "Home")  → /Courses
```

---

## What Does NOT Change

- `DashboardController` and all three Dashboard views (`Professor`, `OfficeManager`, `BookstoreStaff`) are untouched. They remain accessible via sidebar links or direct URL.
- The sidebar `NEW SUBMISSION` link in `Courses/Index` already points to `Requests/Submit` — no change needed.
- The `COURSES` nav link in the top bar already points to `Courses/Index` — no change needed.
- Role-based `[Authorize(Roles=...)]` guards on any controller are unaffected.

---

## Future Extension Point

To give Professors a different landing page, add one condition to `HomeController.Index`:

```csharp
[Authorize]
public IActionResult Index()
{
    var role = User.FindFirst(ClaimTypes.Role)?.Value;
    if (role == "Professor")
        return RedirectToAction("Professor", "Dashboard");
    return RedirectToAction("Index", "Courses");
}
```

No other files need to change.
