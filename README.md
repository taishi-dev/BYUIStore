# CampusAdoptions — Course Material Adoption Management System

> **CSE 210 — Programming with Classes | Open-ended Final Project**

A full-stack ASP.NET Core Blazor Server web application that streamlines the course material adoption workflow at BYU-Idaho University Store. Professors submit textbook/supply requests, office managers verify them, and material managers approve with automated availability checks powered by real-time API integrations.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- SQL Server (the app connects to a shared BYUI SQL Server instance by default)

### Running the Application

```bash
cd CampusAdoptions
dotnet restore
dotnet run
```

The application launches at `https://localhost:5001` (or the port shown in the terminal).

### Demo Accounts

The database is seeded automatically on first run with the following test accounts:

| Username         | Password   | Role              | Description                      |
|------------------|------------|-------------------|----------------------------------|
| `prof_smith`     | `byui1234` | Professor         | Submits book/supply requests     |
| `coord_lee`      | `byui1234` | Professor         | Second professor account         |
| `manager_jones`  | `byui1234` | Office Manager    | Verifies submitted requests      |
| `staff_brown`    | `byui1234` | Material Manager  | Approves requests, runs checks   |

### Configuration

Connection strings and email settings are in `CampusAdoptions/appsettings.json`. For local development with SQLite, uncomment `await db.Database.EnsureCreatedAsync();` in `Data/SeedData.cs`.

---

## How to Use

### Professor Workflow
1. **Log in** — automatically redirected to **My Courses** page
2. **Submit a new adoption** — enter course info and add books/supplies
3. **ISBN auto-fill** — type a book title and the system searches Open Library API, or paste an ISBN for instant lookup via Google Books + VitalSource
4. **Track request status** — view your submissions with live status badges (Pending -> Verified -> Approved)

### Office Manager Workflow
1. **Log in** — redirected to **Office Manager Dashboard**
2. **Verify pending requests** — review submission details and mark as "Verified" or "Rejected"
3. **Submit requests on behalf of professors** — office managers can also create new requests

### Material Manager Workflow
1. **Log in** — redirected to **Material Manager Dashboard**
2. **Approve verified requests** — the system runs an automated 4-point checklist per book:
   - Is the book still available? (Amazon + Google Books)
   - What does it cost? (Price flagged if > $60)
   - Is there a digital/eBook edition? (VitalSource + Google)
   - Has the required/optional status changed from last semester?
3. **View Availability Report** — batch report for all approved ISBNs with affordability scores
4. **Auto-email notifications** — when a book exceeds $60, the system automatically emails the professor suggesting cheaper alternatives

---

## Architecture & OOP Principles

### Abstraction — Single Responsibility Classes

The project is cleanly separated into layers, each with focused responsibilities:

```
CampusAdoptions/
├── Components/           # 29 Blazor components (pages, shared, layouts)
│   ├── Pages/
│   │   ├── Dashboard/    ← Role-specific dashboards (4 variants)
│   │   ├── Requests/     ← Submit/Verify/Approve workflow (4 pages)
│   │   ├── Courses/      ← Course & material management (4 pages)
│   │   ├── Availability/ ← Book availability reports (2 pages)
│   │   └── Account/      ← Login/Logout (3 pages)
│   └── Shared/           ← MaterialCard, SuggestionCard, StatusBadge, CourseRow
├── Models/               # Domain entities with clear boundaries
│   ├── AppUser.cs, Course.cs, Student.cs, Enrollment.cs
│   ├── CourseRequest.cs, RequestItem.cs
│   ├── DerivedRequestItems.cs    ← BookRequestItem, SupplyRequestItem (polymorphic)
│   ├── CourseBookAssignment.cs, MaterialSuggestion.cs
│   └── Verba/                    ← VerbaCourse, Section, Material, TermSummary
├── Services/             # Business logic separated from UI
│   ├── BookLookupServiceBase.cs  ← Abstract base with shared ISBN logic
│   ├── IsbnLookupService.cs      ← Open Library title/author search (inherits base)
│   ├── IsbnDirectLookupService.cs ← 4-tier ISBN lookup (inherits base)
│   ├── BookAvailabilityService.cs ← Automated availability & price checking
│   ├── MaterialReviewService.cs  ← Auto-suggestions after submission
│   ├── AdoptionDiffService.cs    ← Semester-to-semester comparison
│   ├── CourseService.cs          ← Verba course data management
│   └── EmailService.cs           ← SMTP notifications with IEmailService interface
├── ViewModels/           # View-specific data transfer objects
├── Data/                 # EF Core context, seed data, DB initializer
└── Helpers/              # Extension methods (StringExtensions)
```

### Encapsulation

- All service classes use **`private readonly`** fields for dependencies (`_db`, `_logger`, `_http`, `_cache`, etc.)
- Internal helper methods like `TryGoogleBooksAsync()`, `TryVitalSourceAsync()`, `FetchHtmlAsync()` are **`private`**
- Protected members in `BookLookupServiceBase` — only accessible to derived lookup services
- Models expose data through **C# auto-properties** (`{ get; set; }`) — never raw public fields

### Inheritance

| Base Class | Derived Classes | Shared Behavior |
|---|---|---|
| `BookLookupServiceBase` (abstract) | `IsbnLookupService`, `IsbnDirectLookupService` | `NormalizeIsbn()`, `SelectBestIsbn()`, `LogLookupFailure()`, `ServiceName` |
| `RequestItem` | `BookRequestItem`, `SupplyRequestItem` | Item fields + virtual display methods (`GetDisplayTitle`, `GetDisplaySummary`, `GetRequirementLabel`) |
| `DbContext` (EF Core) | `AppDbContext` | Database access with `OnModelCreating` override |
| `IEmailService` (interface) | `EmailService` | Email abstraction for dependency injection |

### Polymorphism — Method Overriding

**Model-level polymorphism** — `RequestItem` defines `virtual` display methods overridden by `BookRequestItem` and `SupplyRequestItem`:

```csharp
// RequestItem (base class)
public virtual string GetDisplayTitle() => ...
public virtual string GetDisplaySummary() => ...
public virtual string GetRequirementLabel() => IsRequired ? "Required" : "Optional";

// BookRequestItem (override — includes author, edition, publisher, ISBN)
public override string GetDisplayTitle() => Title ?? "Untitled Book";
public override string GetDisplaySummary() => "Author · Ed. 5 · Publisher · ISBN 978..."
public override string GetRequirementLabel() => "Required Textbook" / "Optional Textbook";

// SupplyRequestItem (override — includes quantity)
public override string GetDisplayTitle() => SupplyDescription ?? "Untitled Supply";
public override string GetDisplaySummary() => "Quantity: 3";
public override string GetRequirementLabel() => "Required Supply" / "Optional Supply";
```

**Service-level polymorphism** — `BookLookupServiceBase` defines `abstract` property overridden by each lookup service:

```csharp
// BookLookupServiceBase (abstract base)
public abstract string ServiceName { get; }

// IsbnLookupService (override)
public override string ServiceName => "Open Library Search";

// IsbnDirectLookupService (override)
public override string ServiceName => "ISBN Direct Lookup";
```

**Framework-level overrides:**
- `AppDbContext.OnModelCreating()` — overrides EF Core's model configuration for indexes, relationships, and cascade rules

---

## Creativity & Exceeding Requirements

### 1. Real-Time External API Integration (3 services, 4 external APIs)
The system doesn't just store data — it actively reaches out to **Google Books**, **Open Library**, and **VitalSource** to auto-populate book metadata, check prices, and verify digital availability. Faculty never need to manually enter ISBNs.

### 2. VitalSource Web Scraping with 3-Tier Fallback
Since VitalSource has no public API, the app uses intelligent HTML scraping:
1. Search page — extract `/products/` links from HTML
2. `__NEXT_DATA__` JSON parsing (Next.js hydration data — most reliable)
3. Direct VBID URL fallback (`/products/v{ISBN13}`)

Price extraction supports 120-day, 180-day, and Lifetime rental tiers.

### 3. Automated Material Manager Checklist
When approving a request, the system automatically runs a **4-point automated review** for every book:
- Availability check (Amazon + Google Books)
- Price analysis with affordability scoring (0-100 scale)
- Digital edition detection (VitalSource + Google eBooks)
- Required/Optional status change detection vs. previous semester

### 4. Auto-Email Notifications
- Books over **$60** trigger an automatic email to the professor suggesting cheaper alternatives
- **Required <-> Optional** status changes between semesters generate confirmation emails
- Professors can request removal of approved books, which emails the material manager with full context

### 5. Multi-Role Authorization System
Cookie-based authentication with 3 distinct roles (Professor, Office Manager, Material Manager), each seeing a completely different dashboard and having different permissions throughout the workflow.

### 6. In-Memory Caching Strategy
ISBN availability results are cached for 24 hours using `IMemoryCache`, so repeated page views are instant and external APIs aren't hammered.

### 7. NetSuite API Integration
The `DbInitializer` connects to BYUI's NetSuite API to seed real adoption and textbook data from the production system — bridging this academic project with actual university infrastructure.

### 8. Production-Grade Database Design
7 tables with proper indexes for high-volume lookups (20,000+ students, 5,000+ ISBNs), foreign key relationships with appropriate cascade/restrict rules, and composite unique constraints.

### 9. OOP Design Patterns
- **Abstract Base Class Pattern**: `BookLookupServiceBase` centralizes ISBN normalization and logging, inherited by two specialized lookup services
- **Polymorphic Display**: `RequestItem` hierarchy uses virtual/override methods so Blazor components render book vs. supply items without conditional logic
- **Strategy Pattern via DI**: `IEmailService` interface allows swapping SMTP implementation without changing callers
- **Composition**: Services like `MaterialReviewService` compose `BookAvailabilityService` and `IHttpClientFactory` rather than inheriting

---

## Project Structure

| Directory | Purpose |
|---|---|
| `CampusAdoptions/` | Main ASP.NET Core Blazor Server project |
| `CampusAdoptions/Components/` | 29 Blazor components (pages + shared + layouts) |
| `CampusAdoptions/Models/` | 10 entity/model classes (including 2 derived) |
| `CampusAdoptions/Services/` | 8 service classes (including abstract base) |
| `CampusAdoptions/ViewModels/` | 5 view model classes |
| `CampusAdoptions/Data/` | EF Core context, seed data, NetSuite initializer |
| `CampusAdoptions/wwwroot/` | Static files (CSS, JS, images) |
| `CampusAdoptions.Tests/` | Unit tests for ISBN lookup and diff services |

---

## Technology Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 / ASP.NET Core |
| Language | C# 13 |
| UI | Blazor Server + MudBlazor component library |
| Database | SQL Server via EF Core 10 |
| Auth | Cookie-based authentication (role-based) |
| Caching | `IMemoryCache` (24-hour TTL) |
| HTTP Clients | `IHttpClientFactory` (named + typed clients) |
| Email | SMTP via `System.Net.Mail` |
| External APIs | Google Books, Open Library, VitalSource |
