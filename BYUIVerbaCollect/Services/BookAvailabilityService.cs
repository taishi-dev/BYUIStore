using BYUIVerbaCollect.Data;
using BYUIVerbaCollect.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly IMemoryCache _cache;

    private static string CacheKey(string isbn) => $"isbn_avail:{isbn}";
    private static readonly MemoryCacheEntryOptions CacheOpts =
        new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromHours(24));

    public BookAvailabilityService(AppDbContext db, IHttpClientFactory httpFactory,
        ILogger<BookAvailabilityService> logger, IMemoryCache cache)
    {
        _db = db; _httpFactory = httpFactory; _logger = logger; _cache = cache;
    }

    /// <summary>
    /// Returns cached availability data for an ISBN without making any network call.
    /// Returns null if the ISBN has never been checked in this process lifetime.
    /// </summary>
    public BookAvailabilityItem? TryGetFromCache(string isbn)
    {
        var clean = isbn.Replace("-", "").Trim();
        _cache.TryGetValue(CacheKey(clean), out BookAvailabilityItem? result);
        return result;
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
                item.CoverThumbnailUrl = th.GetString()?.Replace("http://", "https://");
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
            // ── Step 1: Search page → extract first /products/... href ─────────────
            var searchUrl = $"https://www.vitalsource.com/search?term={Uri.EscapeDataString(isbn)}";
            var searchReq = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            searchReq.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            searchReq.Headers.Add("Accept", "text/html,application/xhtml+xml");
            searchReq.Headers.Add("Accept-Language", "en-US,en;q=0.9");

            var searchResp = await http.SendAsync(searchReq);
            if (!searchResp.IsSuccessStatusCode) return;

            var searchHtml = await searchResp.Content.ReadAsStringAsync();

            // ── Primary: href="/products/..." OR absolute href="https://www.vitalsource.com/products/..." ──
            var productPath = System.Text.RegularExpressions.Regex.Match(
                searchHtml,
                @"href=""(?:https://www\.vitalsource\.com)?(/products/[^""?#]+)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Groups[1].Value;

            // ── Fallback A: __NEXT_DATA__ JSON contains /products/ path ───
            if (string.IsNullOrEmpty(productPath))
            {
                var ndMatch = System.Text.RegularExpressions.Regex.Match(searchHtml,
                    @"<script id=""__NEXT_DATA__""[^>]*>([\s\S]*?)</script>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (ndMatch.Success)
                {
                    var pathM = System.Text.RegularExpressions.Regex.Match(
                        ndMatch.Groups[1].Value,
                        @"""(/products/[^""?#]+)""",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (pathM.Success)
                        productPath = pathM.Groups[1].Value;
                }
            }

            // ── Fallback B: direct VBID URL v+ISBN13 ─────────────────────
            string? preloadedHtml = null;
            if (string.IsNullOrEmpty(productPath))
            {
                var vbidUrl = $"https://www.vitalsource.com/products/v{isbn}";
                var vbidReq = new HttpRequestMessage(HttpMethod.Get, vbidUrl);
                vbidReq.Headers.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                vbidReq.Headers.Add("Accept", "text/html,application/xhtml+xml");
                vbidReq.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                var vbidResp = await http.SendAsync(vbidReq);
                if (vbidResp.IsSuccessStatusCode)
                {
                    var vbidHtml = await vbidResp.Content.ReadAsStringAsync();
                    // Confirm it's a real product page by checking og:title
                    var titleM = System.Text.RegularExpressions.Regex.Match(vbidHtml,
                        @"property=""og:title""[^>]*content=""([^""]+)""",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!titleM.Success)
                        titleM = System.Text.RegularExpressions.Regex.Match(vbidHtml,
                            @"content=""([^""]+)""[^>]*property=""og:title""",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var ogTitle = titleM.Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(ogTitle) &&
                        !ogTitle.Contains("search", StringComparison.OrdinalIgnoreCase) &&
                        !ogTitle.Contains("vitalsource bookshelf", StringComparison.OrdinalIgnoreCase))
                    {
                        productPath   = $"/products/v{isbn}";
                        preloadedHtml = vbidHtml;
                    }
                }
            }

            if (string.IsNullOrEmpty(productPath))
            {
                _logger.LogWarning("VitalSource: no /products/ link found in search results for ISBN {Isbn}", isbn);
                return;
            }

            item.DigitalAvailableOnVitalSource = true;
            var productUrl = "https://www.vitalsource.com" + productPath;
            item.VitalSourceUrl = productUrl;

            // ── Step 2: Fetch product page → extract price + cover image ─────────
            string? productHtml = preloadedHtml;
            if (productHtml == null)
            {
                var prodReq = new HttpRequestMessage(HttpMethod.Get, productUrl);
                prodReq.Headers.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                prodReq.Headers.Add("Accept", "text/html,application/xhtml+xml");
                prodReq.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                var prodResp = await http.SendAsync(prodReq);
                if (prodResp.IsSuccessStatusCode)
                    productHtml = await prodResp.Content.ReadAsStringAsync();
            }

            if (productHtml != null)
            {
                // Extract best available price (120-day → 180-day → Lifetime)
                var (vsPrice, vsDays) = await FetchVsBestPriceAsync(http, productUrl, isbn, productHtml);
                item.VitalSourcePrice     = vsPrice;
                item.VitalSourcePriceDays = vsDays;

                // Extract cover image from og:image if Google Books didn't provide one
                if (string.IsNullOrEmpty(item.CoverThumbnailUrl))
                {
                    var ogImage = ExtractOgContent(productHtml, "og:image");
                    if (!string.IsNullOrEmpty(ogImage))
                        item.CoverThumbnailUrl = ogImage.Replace("http://", "https://");
                }
            }
            else
            {
                var (vsPrice2, vsDays2) = await FetchVsBestPriceAsync(http, productUrl, isbn, null);
                item.VitalSourcePrice     = vsPrice2;
                item.VitalSourcePriceDays = vsDays2;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VitalSource check failed for ISBN {Isbn}", isbn);
        }
    }

    /// <summary>
    /// Returns the best available VitalSource price: 120-day rental preferred,
    /// then 180-day, then Lifetime. Returns (price, days) where days=0 means Lifetime.
    /// </summary>
    private async Task<(decimal? price, int? days)> FetchVsBestPriceAsync(
        HttpClient http, string productUrl, string isbn, string? preloadedHtml = null)
    {
        try
        {
            string html;
            if (preloadedHtml != null)
            {
                html = preloadedHtml;
            }
            else
            {
                var req = new HttpRequestMessage(HttpMethod.Get, productUrl);
                req.Headers.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                req.Headers.Add("Accept", "text/html,application/xhtml+xml");
                req.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                var resp = await http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return (null, null);
                html = await resp.Content.ReadAsStringAsync();
            }

            // ── Try 1: __NEXT_DATA__ JSON (most reliable) ─────────────────
            var nextDataMatch = System.Text.RegularExpressions.Regex.Match(html,
                @"<script id=""__NEXT_DATA__""[^>]*>([\s\S]*?)</script>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (nextDataMatch.Success)
            {
                try
                {
                    using var doc = JsonDocument.Parse(nextDataMatch.Groups[1].Value);
                    var (jp, jd) = FindVsBestPriceInJson(doc.RootElement);
                    if (jp.HasValue) return (jp, jd);
                }
                catch { /* JSON parse error — fall through */ }
            }

            // ── Try 2: regex near day-count keywords ──────────────────────
            static decimal? RegexNearDays(string h, string pattern)
            {
                var m = System.Text.RegularExpressions.Regex.Match(h,
                    pattern + @"[^$\d]{0,200}\$(\d{1,4}\.\d{2})",
                    System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success && decimal.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v)
                    && v > 0 && v < 1500) return v;

                // price-before-label
                m = System.Text.RegularExpressions.Regex.Match(h,
                    @"\$(\d{1,4}\.\d{2})[^$\d]{0,200}" + pattern,
                    System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success && decimal.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v2)
                    && v2 > 0 && v2 < 1500) return v2;

                return null;
            }

            var p120  = RegexNearDays(html, @"120\s*[-–]?\s*[Dd]ay");
            if (p120.HasValue)  return (p120, 120);

            var p180  = RegexNearDays(html, @"180\s*[-–]?\s*[Dd]ay");
            if (p180.HasValue)  return (p180, 180);

            var pLife = RegexNearDays(html, @"(?:[Ll]ife\s*[Tt]ime|LIFETIME|[Pp]erpetual|[Oo]wn\s*[Ff]orever|[Pp]urchase)");
            if (pLife.HasValue) return (pLife, 0);

            // ── Try 3: JSON "price" field (last resort) ───────────────────
            var mJ = System.Text.RegularExpressions.Regex.Match(html,
                @"""price""\s*:\s*""?(\d{1,4}(?:\.\d{1,2})?)""?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mJ.Success && decimal.TryParse(mJ.Groups[1].Value,
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pJ)
                && pJ > 0 && pJ < 1500)
                return (pJ, null);

            return (null, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VitalSource price fetch failed for {Url}", productUrl);
            return (null, null);
        }
    }

    /// <summary>
    /// Recursively walks __NEXT_DATA__ JSON to find the best license price:
    /// 120-day preferred, then 180-day, then Lifetime (duration=null or type=LIFE).
    /// Returns (price, days) where days=0 means Lifetime.
    /// </summary>
    private static (decimal? price, int? days) FindVsBestPriceInJson(JsonElement element)
    {
        decimal? best120 = null, best180 = null, bestLife = null;

        void Walk(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                int? duration = null;
                bool isLifetime = false;
                decimal? price = null;

                foreach (var prop in el.EnumerateObject())
                {
                    var key = prop.Name.ToLowerInvariant();

                    if (key is "duration" or "days" or "numdays" or "num_days" or "licenselength")
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Number)
                            try { duration = prop.Value.GetInt32(); } catch { }
                        else if (prop.Value.ValueKind == JsonValueKind.String &&
                                 int.TryParse(prop.Value.GetString(), out var di))
                            duration = di;
                        else if (prop.Value.ValueKind == JsonValueKind.Null)
                            isLifetime = true;
                    }

                    if (key is "licensetype" or "type" or "durationtype" or "license_type")
                    {
                        var s = prop.Value.GetString()?.ToUpperInvariant() ?? "";
                        if (s.Contains("LIFE") || s.Contains("PERP") || s.Contains("OWN") || s == "PERM")
                            isLifetime = true;
                    }

                    if (key is "price" or "amount" or "retail_price" or "retailprice" or "listprice" or "list_price")
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Number)
                            try { price = prop.Value.GetDecimal(); } catch { }
                        else if (prop.Value.ValueKind == JsonValueKind.String &&
                                 decimal.TryParse(prop.Value.GetString(),
                                     System.Globalization.NumberStyles.Any,
                                     System.Globalization.CultureInfo.InvariantCulture, out var sp))
                            price = sp;
                    }
                }

                if (price.HasValue && price.Value >= 1m)
                {
                    if (duration == 120)          best120  = price;
                    else if (duration == 180)     best180  = price;
                    else if (isLifetime || duration == 0) bestLife = price;
                }

                foreach (var prop in el.EnumerateObject()) Walk(prop.Value);
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in el.EnumerateArray()) Walk(child);
            }
        }

        Walk(element);

        if (best120.HasValue)  return (best120, 120);
        if (best180.HasValue)  return (best180, 180);
        if (bestLife.HasValue) return (bestLife, 0);
        return (null, null);
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

        // Return cached result immediately if available (avoids redundant live calls)
        if (_cache.TryGetValue(CacheKey(clean), out BookAvailabilityItem? cached) && cached is not null)
            return cached;

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

        // Store in cache so subsequent page loads are instant
        _cache.Set(CacheKey(clean), item, CacheOpts);
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
            VitalSourcePriceDays = avail.VitalSourcePriceDays,
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
    /// <summary>Best available price from VitalSource (120-day preferred, then 180-day, then Lifetime).</summary>
    public decimal? VitalSourcePrice             { get; set; }
    /// <summary>Rental period in days (120 or 180), or 0 for Lifetime. Null if not determined.</summary>
    public int?     VitalSourcePriceDays         { get; set; }

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
    /// <summary>Rental period in days (120 or 180), or 0 for Lifetime. Null if not determined.</summary>
    public int?     VitalSourcePriceDays { get; set; }
    public string?  CoverThumbnail       { get; set; }
    public bool     RequiredChanged      { get; set; }
    public bool?    PreviousIsRequired   { get; set; }
    public string?  PreviousSemester     { get; set; }
}
