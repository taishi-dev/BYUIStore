using CampusAdoptions.Components;
using CampusAdoptions.Data;
using CampusAdoptions.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// ── EF Core / SQL Server ───────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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

// ── Book availability automation ─────────────────────────────────────────
builder.Services.AddScoped<BookAvailabilityService>();

// ── Material review (auto-suggestions after submission) ─────────────────
builder.Services.AddScoped<MaterialReviewService>();

// ── Adoption diff detection (current vs past semester comparison) ────────
builder.Services.AddScoped<AdoptionDiffService>();

// ── Email notifications ──────────────────────────────────────────────────
builder.Services.AddScoped<IEmailService, EmailService>();

// ── Verba Collect course service (in-memory) ─────────────────────────────
builder.Services.AddScoped<CourseService>();

// ── In-memory cache ──────────────────────────────────────────────────────
builder.Services.AddMemoryCache();

// ── Authentication (for legacy pages) ────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath        = "/Account/Login";
        options.LogoutPath       = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan   = TimeSpan.FromHours(8);
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// ── Blazor + MudBlazor ───────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

var app = builder.Build();

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
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
