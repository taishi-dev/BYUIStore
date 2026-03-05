using BYUIVerbaCollect.Data;
using BYUIVerbaCollect.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Net;

namespace BYUIVerbaCollect.Services;

/// <summary>
/// Automation checklist for books used last semester:
///   ✔ Is it still available to buy? (Amazon + publisher link)
///   ✔ What does it cost? (Google Books saleInfo)
///   ✔ Is there a digital/eBook edition? (VitalSource search)
///   ✔ How many students are enrolled in the course?
///   ✔ Is the book required or optional?
/// </summary>
public class BookAvailabilityService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<BookAvailabilityService> _logger;

    public BookAvailabilityService(AppDbContext db, IHttpClientFactory httpFactory,
        ILogger<BookAvailabilityService> logger)
    {
        _db = db; _httpFactory = httpFactory; _logger = logger;
    }

    // ── Main entry: pull books from approved requests, check everything ────
    public async Task<List<BookAvailabilityItem>> GetReportAsync(string? semester = null)
    {
        var query = _db.CourseRequests
            .Include(r => r.Items)
            .Where(r => r.Status == RequestStatus.Approved);

        if (!string.IsNullOrEmpty(semester))
            query = query.Where(r => r.Semester == semester);

        var requests = await query.AsNoTracking().ToListAsync();

        var bookItems = requests
            .SelectMany(r => r.Items
                .Where(i => i.ItemType == ItemType.Book && !string.IsNullOrEmpty(i.Isbn))
                .Select(i => new { Request = r, Item = i }))
            .ToList();

        var uniqueKeys = new HashSet<string>();
        var deduplicated = bookItems
            .Where(x => uniqueKeys.Add($"{x.Item.Isbn}|{x.Request.CourseNumber}"))
            .ToList();

        var courseNumbers = deduplicated.Select(x => x.Request.CourseNumber).Distinct().ToList();
        var enrollmentCounts = await _db.Courses
            .Where(c => courseNumbers.Contains(c.CourseNumber))
            .Select(c => new { c.CourseNumber, Count = c.Enrollments.Count() })
            .ToListAsync();
        var enrollMap = enrollmentCounts.ToDictionary(e => e.CourseNumber, e => e.Count);

        var http = _httpFactory.CreateClient("GoogleBooks");
        var tasks = deduplicated.Select(x => BuildItemAsync(x.Request, x.Item, enrollMap, http));
        var results = await Task.WhenAll(tasks);

        return results.OrderBy(r => r.CourseNumber).ThenBy(r => r.Title).ToList();
    }

    private async Task<BookAvailabilityItem> BuildItemAsync(
        CourseRequest req, RequestItem item,
        Dictionary<string, int> enrollMap, HttpClient http)
    {
        var isbn = item.Isbn!.Replace("-", "").Trim();

        var result = new BookAvailabilityItem
        {
            Isbn            = isbn,
            Title           = item.Title     ?? "(unknown title)",
            Author          = item.Author    ?? "",
            Publisher       = item.Publisher ?? "",
            Edition         = item.Edition   ?? "",
            IsRequired      = item.IsRequired,
            CourseNumber    = req.CourseNumber,
            CourseName      = req.CourseName,
            Semester        = req.Semester,
            EnrollmentCount = enrollMap.TryGetValue(req.CourseNumber, out var cnt) ? cnt : 0,
            AmazonUrl       = $"https://www.amazon.com/s?k={Uri.EscapeDataString(isbn)}&i=stripbooks",
            VitalSourceUrl  = $"https://www.vitalsource.com/search?term={Uri.EscapeDataString(isbn)}",
        };

        await Task.WhenAll(
            CheckGoogleBooksAsync(isbn, result, http),
            CheckVitalSourceAsync(isbn, result, http)
        );

        return result;
    }

    private async Task CheckGoogleBooksAsync(string isbn, BookAvailabilityItem item, HttpClient http)
    {
        try
        {
            var url  = $"https://www.googleapis.com/books/v1/volumes?q=isbn:{isbn}&maxResults=1";
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
                return;

            var vol  = items[0];
            var info = vol.GetProperty("volumeInfo");
            var sale = vol.TryGetProperty("saleInfo", out var si) ? si : (JsonElement?)null;

            if (sale.HasValue)
            {
                var saleability = Str(sale.Value, "saleability");
                item.PrintAvailableOnGoogle = saleability is "FOR_SALE" or "FREE";

                if (sale.Value.TryGetProperty("listPrice", out var lp) &&
                    lp.TryGetProperty("amount", out var amt))
                    item.PrintListPrice = amt.GetDecimal();

                if (sale.Value.TryGetProperty("retailPrice", out var rp) &&
                    rp.TryGetProperty("amount", out var ra))
                    item.PrintRetailPrice = ra.GetDecimal();

                if (sale.Value.TryGetProperty("buyLink", out var bl))
                    item.GoogleBuyLink = bl.GetString();

                if (sale.Value.TryGetProperty("isEbook", out var eb) && eb.GetBoolean())
                {
                    item.EbookAvailableOnGoogle = true;
                    if (sale.Value.TryGetProperty("listPrice", out var elp) &&
                        elp.TryGetProperty("amount", out var ea))
                        item.EbookPrice = ea.GetDecimal();
                }
            }

            if (info.TryGetProperty("pageCount", out var pc)) item.PageCount = pc.GetInt32();

            if (info.TryGetProperty("imageLinks", out var il) &&
                il.TryGetProperty("thumbnail", out var th))
                item.CoverThumbnailUrl = th.GetString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Books check failed for ISBN {Isbn}", isbn);
        }
    }

    private async Task CheckVitalSourceAsync(string isbn, BookAvailabilityItem item, HttpClient http)
    {
        try
        {
            var url = $"https://www.vitalsource.com/search?term={Uri.EscapeDataString(isbn)}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            req.Headers.Add("Accept", "text/html");

            var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return;

            var html = await resp.Content.ReadAsStringAsync();
            item.DigitalAvailableOnVitalSource =
                html.Contains("vitalsource.com/products/", StringComparison.OrdinalIgnoreCase)
                && !html.Contains("No results found", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VitalSource check failed for ISBN {Isbn}", isbn);
        }
    }

    private static string Str(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

    private static string? ExtractOgContent(string html, string property)
    {
        var pattern = $@"property=[""']{System.Text.RegularExpressions.Regex.Escape(property)}[""'][\s\S]*?content=[""']([\s\S]*?)[""']";
        var m = System.Text.RegularExpressions.Regex.Match(html, pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Replace("\n", " ").Trim();
    }

    public async Task<BookAvailabilityItem> CheckSingleIsbnAsync(string isbn)
    {
        var clean = isbn.Replace("-", "").Trim();
        var http  = _httpFactory.CreateClient("GoogleBooks");
        var item  = new BookAvailabilityItem
        {
            Isbn           = clean,
            AmazonUrl      = $"https://www.amazon.com/s?k={Uri.EscapeDataString(clean)}&i=stripbooks",
            VitalSourceUrl = $"https://www.vitalsource.com/search?term={Uri.EscapeDataString(clean)}"
        };
        await Task.WhenAll(
            CheckGoogleBooksAsync(clean, item, http),
            CheckVitalSourceAsync(clean, item, http)
        );
        return item;
    }

    public async Task<BookChecklistResult> CheckBookChecklistAsync(
        string isbn, string? courseNumber, bool isRequired, int? requestId)
    {
        var clean = isbn.Replace("-", "").Trim();
        var http  = _httpFactory.CreateClient("GoogleBooks");

        var avail = new BookAvailabilityItem
        {
            Isbn           = clean,
            AmazonUrl      = $"https://www.amazon.com/s?k={Uri.EscapeDataString(clean)}&i=stripbooks",
            VitalSourceUrl = $"https://www.vitalsource.com/search?term={Uri.EscapeDataString(clean)}"
        };
        await Task.WhenAll(
            CheckGoogleBooksAsync(clean, avail, http),
            CheckVitalSourceAsync(clean, avail, http)
        );

        var price        = avail.PrintRetailPrice ?? avail.PrintListPrice ?? avail.EbookPrice;
        var priceFlagged = price.HasValue && price.Value > 100m;

        bool  requiredChanged   = false;
        bool? previousIsRequired = null;
        string? previousSemester = null;

        if (!string.IsNullOrWhiteSpace(courseNumber))
        {
            var prev = await _db.RequestItems
                .Include(ri => ri.CourseRequest)
                .Where(ri =>
                    ri.Isbn == clean &&
                    ri.CourseRequest.CourseNumber == courseNumber &&
                    (ri.CourseRequest.Status == RequestStatus.Approved ||
                     ri.CourseRequest.Status == RequestStatus.Verified) &&
                    (requestId == null || ri.CourseRequestId != requestId))
                .OrderByDescending(ri => ri.CourseRequest.SubmittedAt)
                .Select(ri => new { ri.IsRequired, ri.CourseRequest.Semester })
                .FirstOrDefaultAsync();

            if (prev is not null)
            {
                previousIsRequired = prev.IsRequired;
                previousSemester   = prev.Semester;
                requiredChanged    = prev.IsRequired != isRequired;
            }
        }

        return new BookChecklistResult
        {
            Isbn                 = clean,
            PrintAvailable       = avail.PrintAvailableOnGoogle,
            PrintPrice           = price,
            PriceFlagged         = priceFlagged,
            DigitalOnVitalSource = avail.DigitalAvailableOnVitalSource,
            DigitalOnGoogle      = avail.EbookAvailableOnGoogle,
            VitalSourceUrl       = avail.VitalSourceUrl,
            AmazonUrl            = avail.AmazonUrl,
            GoogleBuyLink        = avail.GoogleBuyLink,
            CoverThumbnail       = avail.CoverThumbnailUrl,
            RequiredChanged      = requiredChanged,
            PreviousIsRequired   = previousIsRequired,
            PreviousSemester     = previousSemester
        };
    }

    public async Task<List<string>> GetAvailableSemestersAsync() =>
        await _db.CourseRequests
            .Where(r => r.Status == RequestStatus.Approved)
            .Select(r => r.Semester)
            .Distinct()
            .OrderByDescending(s => s)
            .ToListAsync();
}

// ════════════════════════════════════════════════════════════════════════════
// DATA MODELS
// ════════════════════════════════════════════════════════════════════════════

/// <summary>One book with all its availability data for the report.</summary>
public class BookAvailabilityItem
{
    public string Isbn         { get; set; } = "";
    public string Title        { get; set; } = "";
    public string Author       { get; set; } = "";
    public string Publisher    { get; set; } = "";
    public string Edition      { get; set; } = "";

    public string CourseNumber    { get; set; } = "";
    public string CourseName      { get; set; } = "";
    public string Semester        { get; set; } = "";
    public bool   IsRequired      { get; set; }
    public int    EnrollmentCount { get; set; }

    public bool     PrintAvailableOnGoogle { get; set; }
    public decimal? PrintListPrice         { get; set; }
    public decimal? PrintRetailPrice       { get; set; }
    public string?  GoogleBuyLink          { get; set; }
    public string   AmazonUrl             { get; set; } = "";

    public bool     EbookAvailableOnGoogle        { get; set; }
    public decimal? EbookPrice                    { get; set; }
    public bool     DigitalAvailableOnVitalSource { get; set; }
    public string   VitalSourceUrl               { get; set; } = "";

    public int?    PageCount         { get; set; }
    public string? CoverThumbnailUrl { get; set; }

    // ── Affordability Score ────────────────────────────────────────────────
    // $0=100 (free=best), $40=50 (medium), $80+=0 (expensive=worst)
    // Formula: score = max(0, min(100, round(100 - price * 1.25)))
    public int? AffordabilityScore
    {
        get
        {
            var price = PrintRetailPrice ?? PrintListPrice ?? EbookPrice;
            if (!price.HasValue) return null;
            return Math.Max(0, Math.Min(100,
                (int)Math.Round(100.0 - (double)price.Value * 1.25)));
        }
    }

    // True when book costs more than $60 — triggers auto-email to professor
    public bool PriceFlaggedOver60 =>
        (PrintRetailPrice ?? PrintListPrice ?? EbookPrice) is decimal p && p > 60m;

    // True when a price was found anywhere
    public bool HasIaPrice =>
        PrintRetailPrice.HasValue || PrintListPrice.HasValue || EbookPrice.HasValue;
}

/// <summary>Result of the automated 4-point review checklist for one book.</summary>
public class BookChecklistResult
{
    public string   Isbn                 { get; set; } = "";
    public bool     PrintAvailable       { get; set; }
    public string   AmazonUrl            { get; set; } = "";
    public string?  GoogleBuyLink        { get; set; }
    public decimal? PrintPrice           { get; set; }
    /// <summary>True when price exceeds $100.</summary>
    public bool     PriceFlagged         { get; set; }
    public bool     DigitalOnVitalSource { get; set; }
    public bool     DigitalOnGoogle      { get; set; }
    public string   VitalSourceUrl       { get; set; } = "";
    public string?  CoverThumbnail       { get; set; }
    public bool     RequiredChanged      { get; set; }
    public bool?    PreviousIsRequired   { get; set; }
    public string?  PreviousSemester     { get; set; }
}
