using CampusAdoptions.Data;
using CampusAdoptions.Services;
using Byui.Netsuite.ApiClient;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── EF Core / SQL Server ───────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Cookie Authentication (role-based) ────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath        = "/Account/Login";
        options.LogoutPath       = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan   = TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization();

// ── Named HttpClient for Google Books (ISBN direct lookup) ─────────────────
builder.Services.AddHttpClient("GoogleBooks", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CampusAdoptions/1.0");
});

// ── ISBN title/author search (Open Library) ────────────────────────────────
builder.Services.AddHttpClient<IsbnLookupService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CampusAdoptions/1.0");
});

// ── ISBN direct lookup (local DB cache → Google Books → Open Library) ─────
builder.Services.AddScoped<IsbnDirectLookupService>();

// ── Book availability automation (Amazon/VitalSource/Google price check) ──
builder.Services.AddScoped<BookAvailabilityService>();

// ── Email notifications (Material Manager auto-emails) ────────────────────
builder.Services.AddScoped<IEmailService, EmailService>();

// ── NetSuite API clients (adoptions, textbooks, terms, etc.) ──────────────
builder.Services.AddNetsuiteApi(builder.Configuration);

// ── DB seeder (NetSuite → CourseRequests on first startup) ────────────────
builder.Services.AddScoped<DbInitializer>();

// ── In-memory cache (ISBN availability results, 24-hour TTL) ──────────────
builder.Services.AddMemoryCache();

// ── MVC ────────────────────────────────────────────────────────────────────
// Program.cs
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        // ✅ これでC#のPascalCaseがJSONではcamelCaseになる
        options.JsonSerializerOptions.PropertyNamingPolicy = 
            System.Text.Json.JsonNamingPolicy.CamelCase;
    });

var app = builder.Build();

// ── Seed Database ──────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    // 1. Static dev seed (users, courses, students)
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await SeedData.InitializeAsync(db);

    // 2. NetSuite API-driven seed (CourseRequests from adoption data — runs once on empty DB)
    var netsuiteSeeder = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    await netsuiteSeeder.SeedAsync();
}

// ── Middleware Pipeline ────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
