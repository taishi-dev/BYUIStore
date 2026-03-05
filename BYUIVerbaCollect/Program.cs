using BYUIVerbaCollect.Data;
using BYUIVerbaCollect.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── EF Core / SQLite ───────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")
        ?? "Data Source=verba_collect.db"));

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
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BYUIVerbaCollect/1.0");
});

// ── ISBN title/author search (Open Library) ────────────────────────────────
builder.Services.AddHttpClient<IsbnLookupService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BYUIVerbaCollect/1.0");
});

// ── ISBN direct lookup (local DB cache → Google Books → Open Library) ─────
builder.Services.AddScoped<IsbnDirectLookupService>();

// ── Book availability automation (Amazon/VitalSource/Google price check) ──
builder.Services.AddScoped<BookAvailabilityService>();

// ── Email notifications (Material Manager auto-emails) ────────────────────
builder.Services.AddScoped<IEmailService, EmailService>();

// ── MVC ────────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

var app = builder.Build();

// ── Seed Database ──────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await SeedData.InitializeAsync(db);
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
