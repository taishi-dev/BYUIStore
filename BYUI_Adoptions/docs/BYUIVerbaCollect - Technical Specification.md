---
title: BYUIVerbaCollect — Technical Specification
created: 2026-03-16
updated: 2026-03-16
tags:
  - architecture
  - spec
  - dotnet
  - csharp
  - byui
status: living-document
related:
  - "[[2026-03-16-course-materials-page-design]]"
---

# BYUIVerbaCollect — Technical Specification

> **Related Specs:** [[2026-03-16-course-materials-page-design]]

BYUIVerbaCollect is a web application for managing course-material adoption requests at BYU–Idaho University Store. Professors submit book/supply lists each semester; the system routes those requests through a verification → approval workflow, checks external marketplaces for pricing and digital availability, and surfaces the results to bookstore staff.

---

## Table of Contents

1. [[#1. Architecture Overview]]
2. [[#2. Core Modules]]
   - [[#2.1 Controllers]]
   - [[#2.2 Services]]
   - [[#2.3 Data Layer]]
   - [[#2.4 View Models]]
   - [[#2.5 Views]]
3. [[#3. API Endpoints]]
4. [[#4. Data Flow]]
   - [[#4.1 Request Submission Flow]]
   - [[#4.2 Verification & Approval Flow]]
   - [[#4.3 VitalSource Digital-Match Flow]]
   - [[#4.4 Remove-from-List Flow]]
5. [[#5. Database Schema]]
6. [[#6. Authentication & Authorization]]
7. [[#7. External Integrations]]
8. [[#8. Caching Strategy]]
9. [[#9. Key Design Decisions]]

---

## 1. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        Browser (Client)                         │
│  Razor Views (.cshtml)  +  Vanilla JS / Bootstrap 5 / AJAX     │
└───────────────────────────────┬─────────────────────────────────┘
                                │ HTTP / HTTPS
┌───────────────────────────────▼─────────────────────────────────┐
│              ASP.NET Core 10 MVC  (Kestrel / IIS)               │
│                                                                 │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────────────┐ │
│  │  MVC          │  │  API          │  │  Static Files         │ │
│  │  Controllers  │  │  Controller   │  │  wwwroot/             │ │
│  └──────┬───────┘  └──────┬───────┘  └───────────────────────┘ │
│         │                 │                                     │
│  ┌──────▼─────────────────▼──────────────────────────────────┐  │
│  │                   Service Layer                            │  │
│  │  IsbnLookupService  │  IsbnDirectLookupService             │  │
│  │  BookAvailabilityService  │  EmailService                  │  │
│  └──────────────────────────┬──────────────────────────────  ┘  │
│                             │                                   │
│  ┌──────────────────────────▼──────────────────────────────  ┐  │
│  │                 Data Layer (EF Core)                        │  │
│  │  AppDbContext  →  SQLite (verba_collect.db)                 │  │
│  └────────────────────────────────────────────────────────── ┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
        ┌─────────────────────┼─────────────────────┐
        ▼                     ▼                     ▼
 Google Books API      Open Library API      VitalSource
 (pricing, eBook)      (title/author         (digital match,
                        search)               price scrape)
```

**Stack:**

| Layer | Technology |
|---|---|
| Runtime | .NET 10 / ASP.NET Core |
| Language | C# 13 |
| UI | Razor Views (cshtml) + Bootstrap 5 + Bootstrap Icons |
| Database | SQLite via EF Core 10 |
| Auth | Cookie-based (ASP.NET Core Identity-lite — custom AppUser, no Identity framework) |
| Caching | `IMemoryCache` (24-hour TTL for ISBN checks) |
| HTTP Clients | `IHttpClientFactory` (named client "GoogleBooks", typed `IsbnLookupService`) |
| Email | `IEmailService` / `EmailService` (SMTP) |
| Default Route | `/{controller=Account}/{action=Login}/{id?}` — unauthenticated users land on login |

---

## 2. Core Modules

### 2.1 Controllers

#### `AccountController`
- **Login / Logout** — cookie authentication with role-based redirect
- Roles recognized: `Professor`, `OfficeManager`, `BookstoreStaff`
- On login success → redirects to role-appropriate dashboard

#### `DashboardController`
- `/Dashboard/Professor` — professor's personal submission list
- `/Dashboard/OfficeManager` — office manager queue (verify/approve)
- `/Dashboard/BookstoreStaff` — bookstore staff queue

#### `RequestsController`
- `GET /Requests/Submit` — professor submits new adoption request
- `POST /Requests/Submit` — saves `CourseRequest` + `RequestItem` rows
- `GET /Requests/Details/{id}` — full details view for a single request
- `GET /Requests/Verify/{id}` — verification page (runs book checklist)
- `POST /Requests/Verify/{id}` — marks request `Verified`
- `GET /Requests/Approve/{id}` — approval page
- `POST /Requests/Approve/{id}` — marks request `Approved` or `Rejected`

#### `CoursesController`
See [[2026-03-16-course-materials-page-design]] for full UX spec.

- `GET /Courses` — paginated course list with status tabs + department filter
- `GET /Courses/Materials?course=RM+342` — per-course material management page (4 tabs)
- `POST /Courses/RemoveItem` — **hard deletes** a `RequestItem` (only for `PendingVerification` requests)
- `POST /Courses/RequestRemoval` — emails material manager and stamps `RemovalRequestedAt` (for already-approved/verified items)

#### `AvailabilityController`
- `GET /Availability` — book availability report (all approved ISBNs, last semester by default)
- `GET /Availability/Item?isbn=...` — single-ISBN detail view with full checklist

#### `ApiController`
- Pure JSON REST endpoints — all require `[Authorize]`
- See [[#3. API Endpoints]] for the full reference

#### `HomeController`
- Minimal — `/Home/Privacy`, error page

---

### 2.2 Services

#### `IsbnLookupService`
- **Purpose:** Title / author keyword search for the Add Materials form
- **Upstream:** Open Library Search API (`https://openlibrary.org/search.json?title=…&author=…`)
- **Returns:** Up to 10 `IsbnSearchResult` items with title, author, isbn, publisher, year
- **DI registration:** `AddHttpClient<IsbnLookupService>` (typed client)

#### `IsbnDirectLookupService`
- **Purpose:** Lookup by ISBN — no typing needed on the submit form
- **Lookup order:**
  1. Local DB catalog (`CourseBookAssignments` — 5,000+ pre-loaded ISBNs) — instant
  2. Google Books API (`https://www.googleapis.com/books/v1/volumes?q=isbn:…`)
  3. Open Library (`https://openlibrary.org/isbn/{isbn}.json`)
- **DI registration:** `AddScoped`

#### `BookAvailabilityService`
The most complex service. Two main public entry points:

| Method | Purpose |
|---|---|
| `CheckSingleIsbnAsync(isbn)` | One-shot check: Google Books + VitalSource in parallel. Result cached 24 h. |
| `CheckBookChecklistAsync(isbn, courseNumber, isRequired, requestId)` | Full 4-point checklist: availability, price, digital, required-change detection. Used by Verify/Approve pages. |
| `GetReportAsync(semester?)` | Batch report for all approved ISBNs in a semester. Used by Availability page. |
| `TryGetFromCache(isbn)` | Sync cache probe — returns null if not cached. Used by `/api/vitalsource-check` fast path. |

**VitalSource scraping strategy (3-tier fallback):**
1. Search page HTML → extract first `/products/…` href
2. `__NEXT_DATA__` JSON in search page → parse product path
3. Direct VBID URL `https://www.vitalsource.com/products/v{isbn13}` → verify via `og:title`

**Price extraction from VitalSource:**
1. `__NEXT_DATA__` JSON walk (recursive, looks for `duration` + `price` fields)
2. Regex near day-count keywords (`120-day`, `180-day`, `Lifetime`)
3. Fallback `"price":` JSON regex

**Caching:** `IMemoryCache` with key `isbn_avail:{isbn}`, 24-hour absolute expiration.

#### `EmailService` / `IEmailService`
- Sends HTML emails via SMTP (configured in `appsettings.json`)
- Used by `CoursesController.RequestRemoval` to email the material manager when a professor requests removal of an approved item

---

### 2.3 Data Layer

#### `AppDbContext`

```csharp
// Core workflow
DbSet<AppUser>              Users
DbSet<CourseRequest>        CourseRequests
DbSet<RequestItem>          RequestItems

// University reference data
DbSet<Student>              Students              // ~20,000 rows
DbSet<Course>               Courses
DbSet<Enrollment>           Enrollments
DbSet<CourseBookAssignment> CourseBookAssignments // ~5,000 ISBNs
```

**Key indexes:**
- `AppUser.Username` (unique)
- `Student.StudentId` (unique), `Student.Email`
- `CourseBookAssignment.Isbn`, `CourseBookAssignment.CourseId`
- `Enrollment.(StudentId, CourseId)` (unique composite)
- `CourseRequest.Status`, `CourseRequest.SubmitterId`

**Cascade rules:**
- `CourseRequest` → `RequestItem`: `DeleteBehavior.Cascade`
- `Course` → `Enrollment`, `CourseBookAssignment`: `DeleteBehavior.Cascade`
- `CourseRequest.SubmitterId` → `AppUser`: `DeleteBehavior.Restrict`
- `VerifiedById`, `ApprovedById`: `DeleteBehavior.SetNull`

#### Data Models

##### `AppUser`
| Field | Type | Notes |
|---|---|---|
| `Id` | int PK | |
| `Username` | string | unique index |
| `PasswordHash` | string | bcrypt/hash |
| `FullName` | string | display name |
| `Email` | string | for removal request emails |
| `Role` | string | `Professor` / `OfficeManager` / `BookstoreStaff` |

##### `CourseRequest`
| Field | Type | Notes |
|---|---|---|
| `Id` | int PK | |
| `SubmitterId` | int FK → AppUser | |
| `CourseName` | string | |
| `CourseNumber` | string | e.g. `RM 342` |
| `Semester` | string | e.g. `Spring 2026` |
| `Section` | string? | |
| `Status` | enum `RequestStatus` | PendingVerification / Verified / Approved / Rejected |
| `SubmittedAt` | DateTime | UTC |
| `VerifiedById` | int? FK | |
| `VerifiedAt` | DateTime? | |
| `ApprovedById` | int? FK | |
| `ApprovedAt` | DateTime? | |
| `RejectionNote` | string? | |
| `Items` | ICollection\<RequestItem\> | nav property |

##### `RequestItem`
| Field | Type | Notes |
|---|---|---|
| `Id` | int PK | |
| `CourseRequestId` | int FK | cascade delete |
| `ItemType` | enum `ItemType` | Book / Supply |
| `Title` | string? | |
| `Author` | string? | |
| `Isbn` | string? | cleaned (no dashes) |
| `Publisher` | string? | |
| `Edition` | string? | |
| `PublicationYear` | int? | |
| `SupplyDescription` | string? | for Supply items |
| `Quantity` | int | default 1 |
| `IsRequired` | bool | default true |
| `Notes` | string? | |
| `RemovalRequestedAt` | DateTime? | set when professor requests removal of approved item |

---

### 2.4 View Models

| ViewModel | Used by | Purpose |
|---|---|---|
| `LoginViewModel` | AccountController | username + password |
| `SubmitRequestViewModel` | RequestsController.Submit | full submission form |
| `ReviewRequestViewModel` | RequestsController.Verify/Approve | verify + approve form |
| `CourseMaterialsViewModel` | CoursesController.Materials | course materials page with current + prior requests |

##### `CourseMaterialsViewModel`
```csharp
string               CourseNumber
string               CourseName
string               CurrentSemester
CourseRequest?       CurrentRequest     // null if no submission for current semester
List<CourseRequest>  PriorAdoptions     // status=Approved, ordered newest-first
string               ActiveTab          // "selected" | "copy" | "add" | "messages"
```

---

### 2.5 Views

```
Views/
├── Account/
│   ├── Login.cshtml
│   └── AccessDenied.cshtml
├── Availability/
│   ├── Index.cshtml          ← availability report table
│   └── Item.cshtml           ← single-ISBN detail
├── Courses/
│   ├── Index.cshtml          ← course list with status tabs
│   └── Materials.cshtml      ← per-course material management (4-tab layout)
├── Dashboard/
│   ├── Professor.cshtml
│   ├── OfficeManager.cshtml
│   └── BookstoreStaff.cshtml
├── Requests/
│   ├── Submit.cshtml         ← professor submission form
│   ├── Details.cshtml
│   ├── Verify.cshtml         ← runs book-checklist AJAX on load
│   ├── Approve.cshtml
│   ├── _BookChecklist.cshtml ← partial: 4-point checklist widget
│   └── _RequestDetails.cshtml← partial: request summary
└── Shared/
    └── _Layout.cshtml        ← Bootstrap 5, Bootstrap Icons, site nav
```

---

## 3. API Endpoints

All endpoints require an authenticated session (`[Authorize]`). All responses are `application/json` with camelCase keys (configured via `JsonNamingPolicy.CamelCase`).

### `GET /api/isbn-search`

Title/author keyword search via Open Library.

| Query Param | Type | Required | Description |
|---|---|---|---|
| `title` | string | ✅ | Book title (partial match OK) |
| `author` | string | ❌ | Author name filter |

**Response:**
```json
[
  {
    "title": "...",
    "author": "...",
    "isbn": "9780123456789",
    "publisher": "...",
    "year": 2023
  }
]
```

---

### `GET /api/isbn-direct`

Single ISBN lookup — local DB first, then Google Books, then Open Library.

| Query Param | Type | Required |
|---|---|---|
| `isbn` | string | ✅ |

**Response:** same shape as isbn-search item, or `404` with `{ "error": "..." }`.

---

### `GET /api/check-availability`

Full Google Books + VitalSource check for one ISBN. Used by the "ADD MATERIALS" tab.

| Query Param | Required |
|---|---|
| `isbn` | ✅ |

**Response:**
```json
{
  "digitalVitalSource": true,
  "digitalGoogle": false,
  "googlePrice": null,
  "printPrice": 54.99,
  "vitalSourcePrice": 29.99,
  "amazonUrl": "https://...",
  "vitalsourceUrl": "https://...",
  "googleBuyLink": null,
  "coverThumbnail": "https://..."
}
```

---

### `GET /api/vitalsource-check`

**Lightweight** digital-match indicator for the Course Materials page. Serves from cache if available (no network call).

| Query Param | Required |
|---|---|
| `isbn` | ✅ |

**Response:**
```json
{ "hasDigital": true }
```

> **Design note:** VitalSource only shows *digital* books. A hit = digital version exists 100%.

---

### `GET /api/book-checklist`

Full 4-point automated review checklist. Called when Verify or Approve page loads.

| Query Param | Type | Required | Notes |
|---|---|---|---|
| `isbn` | string | ✅ | |
| `courseNumber` | string | ❌ | Enables required-change detection |
| `isRequired` | bool | ❌ | Default `true` |
| `requestId` | int | ❌ | Excludes current request from prior-semester lookup |

**Response:**
```json
{
  "isbn": "9780...",
  "printAvailable": true,
  "printPrice": 54.99,
  "priceFlagged": false,
  "digitalOnVitalSource": true,
  "digitalOnGoogle": false,
  "vitalSourceUrl": "https://...",
  "vitalSourcePriceDays": 120,
  "amazonUrl": "https://...",
  "googleBuyLink": "https://...",
  "coverThumbnail": "https://...",
  "requiredChanged": false,
  "previousIsRequired": true,
  "previousSemester": "Fall 2025"
}
```

**`vitalSourcePriceDays`** interpretation:
- `120` → 120-day rental
- `180` → 180-day rental
- `0` → Lifetime / perpetual
- `null` → could not determine

---

## 4. Data Flow

### 4.1 Request Submission Flow

```
Professor
  │
  ├─ GET /Requests/Submit
  │     └── SubmitRequestViewModel rendered
  │
  ├─ Types book title → AJAX GET /api/isbn-search
  │     └── Open Library → up to 10 results → professor selects
  │
  ├─ Or scans ISBN → AJAX GET /api/isbn-direct
  │     └── LocalDB → Google Books → Open Library → auto-fills form
  │
  └─ POST /Requests/Submit
        └── Creates CourseRequest (Status=PendingVerification)
              + one or more RequestItem rows
              └── Redirect to Dashboard
```

### 4.2 Verification & Approval Flow

```
OfficeManager/BookstoreStaff
  │
  ├─ GET /Requests/Verify/{id}
  │     └── Page load triggers per-item AJAX: GET /api/book-checklist
  │           ├── BookAvailabilityService.CheckBookChecklistAsync()
  │           │     ├── Google Books API (parallel)
  │           │     └── VitalSource scrape (parallel)
  │           └── Returns 4-point checklist card per book
  │
  ├─ POST /Requests/Verify/{id}
  │     └── Status → Verified, VerifiedById/VerifiedAt stamped
  │
  ├─ GET /Requests/Approve/{id}
  │     └── Same AJAX checklist as above
  │
  └─ POST /Requests/Approve/{id}
        └── Status → Approved or Rejected + RejectionNote
```

### 4.3 VitalSource Digital-Match Flow

```
Browser loads /Courses/Materials?course=RM+342
  │
  └─ DOMContentLoaded → for each [data-isbn] element:
        fetch('/api/vitalsource-check?isbn=…')
          │
          ├─ FAST PATH: IMemoryCache hit → return { hasDigital: true/false } instantly
          │
          └─ SLOW PATH: BookAvailabilityService.CheckSingleIsbnAsync()
                ├── VitalSource search page scrape (3-tier fallback)
                │     Hit → DigitalAvailableOnVitalSource = true
                │     Miss → false
                └── Result cached for 24 h
                    → UI updates: ✓ DIGITAL MATCH (green) or ✗ NO DIGITAL (gray)
```

### 4.4 Remove-from-List Flow

```
Professor on /Courses/Materials (SELECTED MATERIALS tab)
  │
  ├─ Clicks "✕ REMOVE FROM LIST"
  │     └─ JS openRemoveModal(itemId, title, action)
  │           ├─ action='direct'  → request is PendingVerification
  │           └─ action='request' → request is Verified or Approved
  │
  ├─ Confirms in modal
  │
  ├─ action='direct':
  │     POST /Courses/RemoveItem
  │       └── _db.RequestItems.Remove(item)
  │             If last item → also removes parent CourseRequest
  │             TempData["Success"] → redirect
  │
  └─ action='request':
        POST /Courses/RequestRemoval
          └── item.RemovalRequestedAt = DateTime.UtcNow
                EmailService.SendAsync(managerEmail, subject, htmlBody)
                TempData["Success" | "Warning"] → redirect
                UI shows "⏳ REMOVAL REQUESTED" amber badge
```

---

## 5. Database Schema

```
Users
  Id, Username(unique), PasswordHash, FullName, Email, Role

CourseRequests
  Id, SubmitterId(FK→Users), CourseName, CourseNumber, Semester, Section
  Status, SubmittedAt
  VerifiedById(FK→Users nullable), VerifiedAt
  ApprovedById(FK→Users nullable), ApprovedAt, RejectionNote

RequestItems
  Id, CourseRequestId(FK→CourseRequests cascade)
  ItemType, Title, Author, Isbn, Publisher, Edition, PublicationYear
  SupplyDescription, Quantity, IsRequired, Notes
  RemovalRequestedAt     ← added migration: 20260316181358_AddRemovalRequestedAt

Courses
  Id, CourseNumber, CourseName, Section, Semester
  DaysOfWeek, StartTime, EndTime, Room
  ProfessorId, ProfessorName, Department, MaxEnrollment

Students
  Id, StudentId(unique), Email, FirstName, LastName, ...

Enrollments
  Id, StudentId(FK→Students cascade), CourseId(FK→Courses cascade)
  [unique composite: StudentId+CourseId]

CourseBookAssignments
  Id, CourseId(FK→Courses cascade), Isbn(indexed), Title, Author, ...
```

**Migration history:**
- `20260316181358_AddRemovalRequestedAt` — adds `RemovalRequestedAt` nullable DateTime to `RequestItems`
- `20260316183831_InitialCreate` — full schema baseline

---

## 6. Authentication & Authorization

- **Mechanism:** ASP.NET Core Cookie Authentication (`CookieAuthenticationDefaults`)
- **Session TTL:** 8 hours (`ExpireTimeSpan`)
- **Login path:** `/Account/Login`
- **Access denied path:** `/Account/AccessDenied`
- **Roles** (stored as claim in cookie):

| Role | Access |
|---|---|
| `Professor` | Submit requests, view own requests, manage materials for own courses |
| `OfficeManager` | Verify requests, view all courses |
| `BookstoreStaff` | Approve/reject requests, full availability report, all courses |

- **API endpoints:** `[Authorize]` on the entire `ApiController` — same session cookie
- **No Identity framework** — custom `AppUser` model with `PasswordHash`, role stored as plain string

---

## 7. External Integrations

### Open Library
- **Used by:** `IsbnLookupService` (title/author search), `IsbnDirectLookupService` (fallback)
- **Endpoints:**
  - `https://openlibrary.org/search.json?title=…&author=…`
  - `https://openlibrary.org/isbn/{isbn}.json`
- **Rate limiting:** None enforced server-side; `User-Agent: BYUIVerbaCollect/1.0`

### Google Books API
- **Used by:** `BookAvailabilityService`, `IsbnDirectLookupService`
- **Endpoint:** `https://www.googleapis.com/books/v1/volumes?q=isbn:{isbn}&maxResults=1`
- **Data extracted:** title, authors, publisher, year, `saleInfo` (print price, eBook availability, buy link), `imageLinks.thumbnail`
- **Auth:** No API key (public quota, unauthenticated)
- **Timeout:** 10 seconds

### VitalSource
- **Used by:** `BookAvailabilityService`
- **No official API** — HTML scraping with browser-like `User-Agent` headers
- **Data extracted:** product existence (= digital availability), price (120-day/180-day/Lifetime), cover image
- **Scraping approach:** 3-tier fallback (see [[#4.3 VitalSource Digital-Match Flow]])
- **Price parsing:** `__NEXT_DATA__` JSON preferred; regex fallback

### Email / SMTP
- **Used by:** `EmailService` for removal request notifications
- **Config keys:** `Email:SmtpHost`, `Email:SmtpPort`, `Email:Username`, `Email:Password`, `Email:ManagerAddress`
- **Content:** HTML table email with book + course details

---

## 8. Caching Strategy

| Cache Key | TTL | Content | Invalidation |
|---|---|---|---|
| `isbn_avail:{isbn}` | 24 hours absolute | `BookAvailabilityItem` (Google Books + VitalSource results) | Never explicit — expires naturally |

- **Implementation:** `Microsoft.Extensions.Caching.Memory.IMemoryCache`
- **Registered:** `builder.Services.AddMemoryCache()`
- **Fast path:** `TryGetFromCache(isbn)` — synchronous, no async overhead
- **Scope:** In-process only (single server). Not distributed.

---

## 9. Key Design Decisions

### Why SQLite?
Development simplicity. The DB file (`verba_collect.db`) is committed to the repo and seeded from `SeedData.cs` on startup if empty. Production can swap to PostgreSQL/SQL Server by changing the connection string and EF provider.

### Why cookie auth instead of ASP.NET Core Identity?
Keeps the user model simple (no AspNetUsers/AspNetRoles tables). Roles are a plain string field on `AppUser`. No email confirmation, no external providers needed for an internal bookstore tool.

### Why scrape VitalSource instead of using an API?
VitalSource does not expose a public pricing API. The scraper uses `__NEXT_DATA__` JSON (Next.js hydration data) as the primary source, which is more stable than CSS-class-based HTML parsing.

### Why is `RemovalRequestedAt` a soft marker instead of a delete?
When a request is already Verified or Approved, the bookstore may have started ordering. The soft marker keeps the item visible with an amber "REMOVAL REQUESTED" badge until the material manager can safely process it. Hard deletes are only allowed while the request is still `PendingVerification`.

### Why per-ISBN in-memory caching with 24-hour TTL?
VitalSource and Google Books checks take 1–3 seconds each. The Verify page loads a full checklist for every book simultaneously. Caching avoids hammering external services on every page view and makes the Course Materials page's lazy AJAX checks feel instant on repeat visits.

---

*This document is auto-generated from codebase analysis on 2026-03-16. Keep in sync with significant architectural changes.*
