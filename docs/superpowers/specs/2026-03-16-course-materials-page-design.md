# Course Materials Page — Design Spec
**Date:** 2026-03-16  
**Project:** BYUI VerbaCollect  
**Status:** Approved

---

## Summary

Add a dedicated **Course Materials** page (`Courses/Materials?course=RM+342`) that mirrors the Verba/VitalSource adoption management UI. Three features ship together:

1. **Sub-header shows course number** instead of "COURSES" when a course is opened
2. **REMOVE FROM LIST** button on each book card with two-path logic
3. **DIGITAL MATCH** indicator in the AUTO ACCESS panel, loaded lazily via AJAX

---

## 1. Architecture & Routes

### New route
```
GET  /Courses/Materials?course=RM+342
POST /Courses/RemoveItem       (pending books — immediate delete)
POST /Courses/RequestRemoval   (verified/approved books — email + badge)
GET  /api/vitalsource-check?isbn=XXXX  (AJAX digital check, existing ApiController)
```

### CoursesController — new `Materials` action
- Accept `string course` query param (e.g. `"RM 342"`)
- Set `ViewData["PageTitle"] = course` → sub-header renders course number
- Query **SELECTED MATERIALS**: `CourseRequest` where `CourseNumber == course` AND `Semester == currentSemester` (Spring 2026), with `RequestItems`
- Query **COPY ANOTHER ADOPTION**: `CourseRequest` records for the same `CourseNumber` from *previous* semesters, status = Approved, ordered by most recent semester
- Pass both to `CourseMaterialsViewModel`

### ViewModel: `CourseMaterialsViewModel`
```csharp
public class CourseMaterialsViewModel {
    public string CourseNumber   { get; set; }
    public string CourseName     { get; set; }
    public string CurrentSemester { get; set; }
    public CourseRequest? CurrentRequest  { get; set; }   // SELECTED MATERIALS
    public List<CourseRequest> PriorAdoptions { get; set; } // COPY ANOTHER ADOPTION
}
```

---

## 2. View: `Views/Courses/Materials.cshtml`

### Layout
- **Top bar**: ← BACK TO COURSES | NEXT COURSE → | status pill (✓ APPROVED / PENDING etc.)
- **Left panel**: Instructor name + section number (from `CurrentRequest.Submitter.FullName`)
- **Main panel**: 4 tabs
  - **SELECTED MATERIALS** (default active) — books in `CurrentRequest.Items`
  - **COPY ANOTHER ADOPTION** — books from `PriorAdoptions`
  - **ADD MATERIALS** — links to Submit page
  - **MESSAGES & ACTIVITY** — placeholder for now

### Book card layout (SELECTED MATERIALS)
Each `RequestItem` renders as a card with:
- Left: book thumbnail (placeholder image)
- Center: title, author, ISBN details, publisher, edition, publication date, list price, links
- Right panel:
  - `✕ REMOVE FROM LIST` button
  - Required/Optional dropdown
  - ITEM QUESTIONS section
  - **AUTO ACCESS** section with `DIGITAL MATCH` placeholder (filled by AJAX)

---

## 3. REMOVE FROM LIST

### Two-path logic based on request status

| Request Status | Behavior |
|---|---|
| `PendingVerification` | Show confirmation modal → POST `/Courses/RemoveItem` → hard delete `RequestItem` → if last item, delete `CourseRequest` too |
| `Verified` or `Approved` | POST `/Courses/RequestRemoval` → send email professor → material manager → show `⏳ REMOVAL REQUESTED` badge; item stays in DB |

### Email content (Verified/Approved path)
```
To: material_manager@byui.edu
From: professor@byui.edu
Subject: Removal Request — [Book Title] from [Course Number] [Semester]

Professor [FullName] has requested removal of:
  Title:  [Title]
  Author: [Author]
  ISBN:   [ISBN]
  Course: [CourseNumber] — [CourseName]
  Section: [Section]
  Semester: [Semester]

Please review and process this removal in the system.
```

---

## 4. DIGITAL MATCH (Lazy AJAX)

### Flow
1. Page loads immediately — each book card's AUTO ACCESS panel shows a spinner
2. After DOM ready, JavaScript fires `fetch('/api/vitalsource-check?isbn=XXXX')` per book (parallel)
3. API returns `{ "hasDigital": true/false }`
4. JS updates each card's AUTO ACCESS panel:
   - `true`  → `✓ DIGITAL MATCH` (green checkmark)
   - `false` → `✗ NO DIGITAL` (gray, muted)

### API endpoint
Reuse/extend existing `ApiController` — add or verify `/api/vitalsource-check?isbn=` that:
- Calls `BookAvailabilityService.CheckVitalSourceAsync(isbn)`
- Returns `{ "hasDigital": true }` if HTTP 200, `{ "hasDigital": false }` otherwise
- Uses existing in-memory cache (24-hour TTL) to avoid duplicate calls

---

## 5. Courses/Index Update

- Clicking a course row (or the ACTIONS → View Details) navigates to `/Courses/Materials?course=RM+342`
- The expand (+) button still works for the quick section list inline view

---

## 6. Edge Cases

| Scenario | Handling |
|---|---|
| No current semester request exists | SELECTED MATERIALS tab shows empty state + "Add Materials" CTA |
| Remove last item from pending request | Delete `RequestItem` + `CourseRequest`, redirect to course list |
| VitalSource rate-limited (429) | Show spinner → retry once after 2s, then show `— CHECKING` if still failing |
| Email service unavailable | Log error, show warning toast "Removal request saved but email failed to send" |
| Course not found | Return 404 with friendly message |
