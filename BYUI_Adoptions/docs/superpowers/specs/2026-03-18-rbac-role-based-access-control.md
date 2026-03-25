# RBAC — Role-Based Access Control Design

**Date:** 2026-03-18  
**Status:** Approved  
**Scope:** Strict per-role routing, data scoping, authorization guards, role rename (BookstoreStaff → MaterialManager), and new MaterialManager dashboard.

---

## Role Table

| Role | Landing Page | Data Scope | Key Actions |
|---|---|---|---|
| Professor | `/Courses/MyCourses` | Own submitted courses only | Submit, manage materials, request removal |
| OfficeManager | `/Dashboard/OfficeManager` | All courses (verification phase) | Verify requests |
| MaterialManager | `/Dashboard/MaterialManager` | All courses (approval phase) | Review & Approve/Reject, Export |

---

## 1. Role Rename: BookstoreStaff → MaterialManager

**Files changed:**
- `SeedData.cs` — `staff_brown` Role: `"BookstoreStaff"` → `"MaterialManager"`. All 3 seed approver queries updated.
- `DashboardController.cs` — `if (role == "BookstoreStaff")` → `"MaterialManager"`, returns `View("MaterialManager")`
- Any `[Authorize(Roles = "...")]` attributes updated throughout

---

## 2. HomeController Dispatcher (role-based routing)

`HomeController.Index` dispatches based on role claim:

```
Professor       → RedirectToAction("MyCourses", "Courses")
OfficeManager   → RedirectToAction("OfficeManager", "Dashboard")
MaterialManager → RedirectToAction("MaterialManager", "Dashboard")
_               → RedirectToAction("Login", "Account")
```

No view is rendered — pure redirect dispatcher.

---

## 3. Professor "MyCourses" — CoursesController

**New action:** `CoursesController.MyCourses()`
- Queries `CourseRequests` where `SubmitterId == currentUserId` only
- Same grouped/status-tab layout as `Courses/Index`
- Sidebar shows only: MY COURSES, NEW SUBMISSION (no verify/approve links)

**Guard on `Courses/Index`:**
- If role is Professor → `TempData["Warning"] = "You can only view your own courses."` + redirect to `MyCourses`

---

## 4. Authorization Guards (redirect + warning, not 403)

| Scenario | Action |
|---|---|
| Professor hits `/Requests/Verify/{id}` | Redirect to `MyCourses` + TempData warning |
| Professor hits `/Requests/Approve/{id}` | Redirect to `MyCourses` + TempData warning |
| Any role hits `/Availability` | Allowed. Export controls hidden unless `role == "MaterialManager"` |

Implementation: guard checks at the top of `Verify` and `Approve` actions in `RequestsController`.

---

## 5. MaterialManager Dashboard (new view, built from scratch)

**Controller action:** `DashboardController.MaterialManager()`

**Data queries:**
- `cntAwaitingApproval` = count of `Status == Verified`
- `cntApprovedToday` = count of `Status == Approved && ApprovedAt.Date == today`
- `cntRejectedThisWeek` = count of `Status == Rejected && ApprovedAt >= startOfWeek`
- `queue` = all requests where `Status == Verified`, ordered by `VerifiedAt` ascending (oldest first)

**View layout:**
1. 3 summary cards in a row (dark card style matching the app theme)
2. Queue list table: Course Number, Course Name, Section, Semester, Submitted By, Verified At
3. Each row: single "Review & Approve →" button → `/Requests/Approve/{id}`

---

## 6. Navbar / Sidebar Role Filtering

**Top navbar (all roles):** COURSES link → `Home/Index` (dispatcher routes correctly per role). ITEMS visible to all.

**Sidebar in Courses views (Professors):**
- Show: MY COURSES, NEW SUBMISSION
- Hide: SECTIONS TO VERIFY, SECTIONS TO APPROVE, APPROVED SECTIONS, REJECTED SECTIONS

**Sidebar in Courses views (OfficeManager / MaterialManager):**
- Show all existing sidebar links

Implementation: `@if (role != "Professor")` guards around staff-only sidebar items in the shared layout or partial views.

---

## Files Changed Summary

| File | Change |
|---|---|
| `Data/SeedData.cs` | Rename BookstoreStaff → MaterialManager (3 places) |
| `Controllers/DashboardController.cs` | Rename BookstoreStaff branch, add MaterialManager action with 3 stat queries + queue |
| `Controllers/HomeController.cs` | Role-based switch in `Index()` |
| `Controllers/CoursesController.cs` | Add `MyCourses()` action; guard on `Index()` for Professor |
| `Controllers/RequestsController.cs` | Guards on `Verify` + `Approve` for Professor role |
| `Controllers/AvailabilityController.cs` | Pass role to ViewBag for export control visibility |
| `Views/Dashboard/MaterialManager.cshtml` | New view (summary cards + queue list) |
| `Views/Courses/Index.cshtml` | Sidebar role-conditional links |
| `Views/Availability/Index.cshtml` | Conditional export controls |

---

## Data Flow Examples

```
Prof. Smith logs in
  → POST /Account/Login → Home/Index → MyCourses
  → Sees only CSE 210, CSE 212 (his SubmitterId rows)
  → Clicks COURSES in navbar → Home/Index → MyCourses (same result)
  → Types /Courses directly → warning + redirect to MyCourses
  → Types /Requests/Verify/1 → warning + redirect to MyCourses

Amy Jones (MaterialManager) logs in
  → POST /Account/Login → Home/Index → Dashboard/MaterialManager
  → Sees: "5 Awaiting Approval" | "2 Approved Today" | "1 Rejected This Week"
  → Queue shows Verified requests with "Review & Approve →" buttons
```
