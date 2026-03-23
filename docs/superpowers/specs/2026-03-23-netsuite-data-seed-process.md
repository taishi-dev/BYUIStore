# NetSuite Data Seed Process

> How to copy real adoption data from BYUI's NetSuite system into your local development database (`TaishiOnly_CourseMaterials_dev`).

---

## Overview

The CampusAdoptions app has a built-in `DbInitializer` that automatically fetches real course adoption data from BYUI's NetSuite API and saves it into your SQL Server database on startup. **No new code is needed** — you just need to configure Azure credentials and run the app.

### How It Works

```
You run the app
    → Program.cs calls DbInitializer.SeedAsync()
        → DbInitializer calls NetSuite API (using Azure credentials)
            → NetSuite returns real adoption data (courses, textbooks, ISBNs)
                → DbInitializer saves it all into TaishiOnly_CourseMaterials_dev
```

---

## Prerequisites

- Access to BYUI's internal network (on-campus or via **GlobalProtect VPN**)
- Azure AD credentials from BYUI IT department
- SQL Server Management Studio (SSMS) or Azure Data Studio
- .NET 10 SDK installed

---

## Step 1: Get Azure Credentials from BYUI IT

Contact your IT department and request:

| Credential | Description | Example Format |
|---|---|---|
| **Client ID** | Azure AD application registration ID | `a1b2c3d4-e5f6-7890-abcd-ef1234567890` |
| **Client Secret** | Application secret key | Long random string |
| **Tenant ID** | BYUI's Azure AD tenant identifier | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |
| **API Environment** | Which NetSuite environment to use | `"Staging"` or `"Production"` |

---

## Step 2: Add Credentials to Configuration

### Option A: `appsettings.json` (quick but less secure)

Open `CampusAdoptions/appsettings.json` and add the `ByuiApis` and `Azure` sections:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=ussql;Database=TaishiOnly_CourseMaterials_dev;TrustServerCertificate=True;Integrated Security=True;"
  },
  "ByuiApis": {
    "Environment": "Staging",
    "TimeoutSeconds": 30
  },
  "Azure": {
    "ClientId": "PASTE-YOUR-CLIENT-ID-HERE",
    "ClientSecret": "PASTE-YOUR-CLIENT-SECRET-HERE",
    "TenantId": "PASTE-YOUR-TENANT-ID-HERE"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

> ⚠️ **WARNING:** Never commit secrets to git! Make sure `appsettings.json` with real credentials is in `.gitignore`, or use Option B instead.

### Option B: `dotnet user-secrets` (recommended — secure)

```bash
cd CampusAdoptions
dotnet user-secrets init
dotnet user-secrets set "Azure:ClientId" "your-client-id-here"
dotnet user-secrets set "Azure:ClientSecret" "your-client-secret-here"
dotnet user-secrets set "Azure:TenantId" "your-tenant-id-here"
dotnet user-secrets set "ByuiApis:Environment" "Staging"
dotnet user-secrets set "ByuiApis:TimeoutSeconds" "30"
```

User secrets are stored outside the project folder and never get committed to git.

---

## Step 3: Connect to BYUI Network

The NetSuite API is an internal BYUI service. Your computer must be:
- **On campus** (connected to BYUI's network), OR
- **Connected via GlobalProtect VPN** from home

Without network access, the API calls will timeout or fail with connection errors.

---

## Step 4: Clear Existing Dummy Data

The `DbInitializer.SeedAsync()` has a guard that **skips seeding if any CourseRequests already exist**:

```csharp
if (await _context.CourseRequests.AnyAsync())
    return;  // ← Exits early, no data fetched
```

To allow fresh seeding, clear the existing data. Open **SSMS** or **Azure Data Studio**, connect to `ussql`, and run:

```sql
USE TaishiOnly_CourseMaterials_dev;

-- Clear existing adoption data (order matters due to foreign keys)
DELETE FROM RequestItems;
DELETE FROM CourseRequests;

-- Verify tables are empty
SELECT COUNT(*) AS RemainingRequests FROM CourseRequests;
SELECT COUNT(*) AS RemainingItems FROM RequestItems;
```

Both counts should return `0`.

---

## Step 5: Run the Application

```bash
dotnet run --project CampusAdoptions
```

Watch the terminal output. On startup, `DbInitializer.SeedAsync()` will:

1. ✅ Apply any pending EF migrations
2. ✅ Check that `CourseRequests` is empty → proceeds to fetch
3. ✅ Call `_netsuite.GetAdoptionsWithTextbookForAutoAccessByTermAsync("Spring2026")`
4. ✅ Receive real adoption data from NetSuite (courses, ISBNs, textbooks, authors, publishers)
5. ✅ Group adoptions by course number + section
6. ✅ Map each adoption to `CourseRequest` + `RequestItem` objects
7. ✅ Save everything to `TaishiOnly_CourseMaterials_dev`

You should see EF Core `INSERT` statements in the terminal logs.

---

## Step 6: Verify the Data

In SSMS, run these queries to confirm real data was imported:

```sql
USE TaishiOnly_CourseMaterials_dev;

-- How many course requests were imported?
SELECT COUNT(*) AS TotalRequests FROM CourseRequests;

-- How many textbook items?
SELECT COUNT(*) AS TotalItems FROM RequestItems;

-- Preview the imported courses
SELECT TOP 20 
    CourseNumber, 
    Section, 
    Semester, 
    Status, 
    SubmittedAt
FROM CourseRequests
ORDER BY CourseNumber;

-- Preview the imported textbooks
SELECT TOP 20 
    ri.Isbn, 
    ri.Title, 
    ri.Author, 
    ri.Publisher, 
    ri.IsRequired,
    cr.CourseNumber
FROM RequestItems ri
JOIN CourseRequests cr ON ri.CourseRequestId = cr.Id
ORDER BY cr.CourseNumber;
```

You should see real Spring 2026 course adoptions with actual ISBNs, titles, and authors.

---

## Troubleshooting

| Problem | Cause | Solution |
|---|---|---|
| `401 Unauthorized` | Invalid or missing Azure credentials | Double-check ClientId, ClientSecret, TenantId |
| `Connection timeout` | Not on BYUI network | Connect via GlobalProtect VPN |
| No data imported (0 rows) | `CourseRequests` table wasn't empty | Run the `DELETE` statements from Step 4 |
| No data imported (0 rows) | No adoptions exist for "Spring2026" | Change the `termCode` in `DbInitializer.cs` to the correct term |
| `AddNetsuiteApi` error | Missing `ByuiApis` config section | Add the `ByuiApis` section to `appsettings.json` |

---

## Changing the Term

The current code fetches **Spring 2026** data. To change the term, edit `CampusAdoptions/Data/DbInitializer.cs`:

```csharp
// Line ~38: Change this value
const string termCode = "Spring2026";  // Format: "Season" + "Year" (no space)
```

Valid examples: `"Fall2025"`, `"Winter2026"`, `"Spring2026"`, `"Summer2026"`

---

## Architecture Reference

```
CampusAdoptions/
├── Program.cs                    ← Calls SeedData + DbInitializer on startup
├── Data/
│   ├── DbInitializer.cs          ← Fetches from NetSuite API, saves to SQL Server
│   └── SeedData.cs               ← Seeds static dev data (users, courses, students)
├── appsettings.json              ← Connection string + Azure credentials
└── nuget.config                  ← Points to BYUI's NuGet feed for API packages
```

**NuGet packages used:**
- `Byui.ApiClients.Netsuite` — C# client for NetSuite adoption API
- `Byui.ApiClients.UniversityStoreData` — C# client for University Store data API
