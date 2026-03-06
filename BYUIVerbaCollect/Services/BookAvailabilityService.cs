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
            // ── Step 1: Search page — confirms the book exists on VitalSource ──────
            var searchUrl = $"https://www.vitalsource.com/search?term={Uri.EscapeDataString(isbn)}";
            var searchReq = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            searchReq.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            searchReq.Headers.Add("Accept", "text/html,application/xhtml+xml");
            searchReq.Headers.Add("Accept-Language", "en-US,en;q=0.9");

            var searchResp = await http.SendAsync(searchReq);
            if (!searchResp.IsSuccessStatusCode) return;

            var searchHtml = await searchResp.Content.ReadAsStringAsync();
            bool found = searchHtml.Contains("vitalsource.com/products/", StringComparison.OrdinalIgnoreCase)
                      && !searchHtml.Contains("No results found", StringComparison.OrdinalIgnoreCase);

            item.DigitalAvailableOnVitalSource = found;
            if (!found) return;

            // ── Step 2: Extract the product page URL from the search results ────────
            // VitalSource VBID = "v" + ISBN13 (no dashes), e.g. v9780547750149
            var vbid  = "v" + isbn;
            string? productUrl = null;

            // 2a: Search the __NEXT_DATA__ JSON blob for a path containing the VBID
            var nextDataSearchMatch = System.Text.RegularExpressions.Regex.Match(searchHtml,
                @"<script id=""__NEXT_DATA__""[^>]*>([\s\S]*?)</script>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (nextDataSearchMatch.Success)
            {
                // Look for VBID anywhere in the JSON blob (may be URL-encoded or not)
                var pathInJson = System.Text.RegularExpressions.Regex.Match(
                    nextDataSearchMatch.Groups[1].Value,
                    $@"""(/products/[^""]*?{System.Text.RegularExpressions.Regex.Escape(vbid)}[^""]*?)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (pathInJson.Success)
                    productUrl = "https://www.vitalsource.com" + pathInJson.Groups[1].Value;
            }

            // 2b: Try href attributes in the raw HTML
            if (productUrl is null)
            {
                var hrefMatch = System.Text.RegularExpressions.Regex.Match(searchHtml,
                    $@"href=[""'](/products/[^""']*?{System.Text.RegularExpressions.Regex.Escape(vbid)}[^""']*?)[""']",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (hrefMatch.Success)
                    productUrl = "https://www.vitalsource.com" + hrefMatch.Groups[1].Value;
            }

            // 2c: Try any JSON string that contains the ISBN digits (VS sometimes omits 'v' prefix)
            if (productUrl is null)
            {
                var loosePath = System.Text.RegularExpressions.Regex.Match(searchHtml,
                    $@"""(/products/[^""]*?{System.Text.RegularExpressions.Regex.Escape(isbn)}[^""]*?)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (loosePath.Success)
                    productUrl = "https://www.vitalsource.com" + loosePath.Groups[1].Value;
            }

            // 2d: Hard-coded fallback path that VitalSource accepts via redirect
            productUrl ??= $"https://www.vitalsource.com/search?term={Uri.EscapeDataString(isbn)}";

            // ── Step 3: Fetch product page → parse 120-day rental price ──────────
            item.VitalSourcePrice = await FetchVs120DayPriceAsync(http, productUrl, isbn);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VitalSource check failed for ISBN {Isbn}", isbn);
        }
    }

    /// <summary>
    /// Fetches a VitalSource product page and returns the 120-day rental price.
    /// Tries __NEXT_DATA__ JSON first (most reliable), then regex fallbacks.
    /// </summary>
    private async Task<decimal?> FetchVs120DayPriceAsync(HttpClient http, string productUrl, string isbn)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, productUrl);
            req.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            req.Headers.Add("Accept", "text/html,application/xhtml+xml");
            req.Headers.Add("Accept-Language", "en-US,en;q=0.9");

            var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var html = await resp.Content.ReadAsStringAsync();

            // ── Try 1: Walk the Next.js __NEXT_DATA__ JSON for 120-day license ──
            var nextDataMatch = System.Text.RegularExpressions.Regex.Match(html,
                @"<script id=""__NEXT_DATA__""[^>]*>([\s\S]*?)</script>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (nextDataMatch.Success)
            {
                try
                {
                    using var doc = JsonDocument.Parse(nextDataMatch.Groups[1].Value);
                    var price = FindVs120DayPriceInJson(doc.RootElement);
                    if (price.HasValue) return price;
                }
                catch { /* JSON parse error — fall through */ }
            }

            // ── Try 2: "120" token close to a "$X.XX" price ──────────────────────
            var mA = System.Text.RegularExpressions.Regex.Match(html,
                @"120\s*[-–]?\s*[Dd]ay[s]?[^$\d]{0,200}\$(\d{1,4}\.\d{2})",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (mA.Success && decimal.TryParse(mA.Groups[1].Value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var pA))
                return pA;

            var mB = System.Text.RegularExpressions.Regex.Match(html,
                @"\$(\d{1,4}\.\d{2})[^$\d]{0,200}120\s*[-–]?\s*[Dd]ay[s]?",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (mB.Success && decimal.TryParse(mB.Groups[1].Value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var pB))
                return pB;

            // ── Try 3: First JSON "price":"X.XX" on the product page ─────────────
            var mJ = System.Text.RegularExpressions.Regex.Match(html,
                @"""price""\s*:\s*""?(\d{1,4}(?:\.\d{1,2})?)""?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mJ.Success && decimal.TryParse(mJ.Groups[1].Value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var pJ))
                return pJ;

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VitalSource product page fetch failed for {Url}", productUrl);
            return null;
        }
    }

    /// <summary>
    /// Recursively walks parsed Next.js __NEXT_DATA__ JSON to find a license object
    /// that has duration == 120 (days) and a price/amount field.
    /// </summary>
    private static decimal? FindVs120DayPriceInJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            bool has120 = false;
            decimal? price = null;

            foreach (var prop in element.EnumerateObject())
            {
                var key = prop.Name.ToLowerInvariant();

                // Detect duration = 120 days
                if (key is "duration" or "days" or "numdays" or "num_days" or "licenselength")
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                    {
                        try { if (prop.Value.GetInt32() == 120) has120 = true; } catch { }
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        if (prop.Value.GetString() is "120") has120 = true;
                    }
                }

                // Detect price field
                if (key is "price" or "amount" or "retail_price" or "retailprice" or "listprice" or "list_price")
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                    {
                        try { price = prop.Value.GetDecimal(); } catch { }
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.String &&
                             decimal.TryParse(prop.Value.GetString(),
                                 System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture, out var sp))
                        price = sp;
                }
            }

            if (has120 && price.HasValue && price.Value >= 1m)
                return price;

            // Recurse
            foreach (var prop in element.EnumerateObject())
            {
                var found = FindVs120DayPriceInJson(prop.Value);
                if (found.HasValue) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var found = FindVs120DayPriceInJson(child);
                if (found.HasValue) return found;
            }
        }
        return null;
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
    /// <summary>Scraped 120-day rental price from VitalSource search results page.</summary>
    public decimal? VitalSourcePrice             { get; set; }

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
