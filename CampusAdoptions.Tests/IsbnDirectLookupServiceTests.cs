using CampusAdoptions.Data;
using CampusAdoptions.Models;
using CampusAdoptions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;

namespace CampusAdoptions.Tests;

public class IsbnDirectLookupServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public IsbnDirectLookupServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // ═══════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private IsbnDirectLookupService CreateService(
        HttpResponseMessage? googleResponse = null,
        HttpResponseMessage? vitalSourceSearchResponse = null,
        HttpResponseMessage? vitalSourceProductResponse = null)
    {
        var handler = new MockHttpHandler(
            googleResponse, vitalSourceSearchResponse, vitalSourceProductResponse);
        var httpClient = new HttpClient(handler);

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        return new IsbnDirectLookupService(
            _db, factoryMock.Object,
            Mock.Of<ILogger<IsbnDirectLookupService>>());
    }

    // ═══════════════════════════════════════════════════════════════════
    // ISBN Normalization
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Lookup_EmptyIsbn_ReturnsNull()
    {
        var service = CreateService();
        var result = await service.LookupAsync("");
        Assert.Null(result);
    }

    [Fact]
    public async Task Lookup_WhitespaceIsbn_ReturnsNull()
    {
        var service = CreateService();
        var result = await service.LookupAsync("   ");
        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Local DB Cache Hit (Fallback Step 1)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Lookup_IsbnInLocalDb_ReturnsFromLocalCache()
    {
        _db.CourseBookAssignments.Add(new CourseBookAssignment
        {
            Isbn = "9781590282984",
            Title = "Python Programming",
            Author = "Zelle",
            Publisher = "Franklin Beedle",
            Edition = "4th",
            PublicationYear = 2024,
            CourseId = 0
        });
        await _db.SaveChangesAsync();

        // VitalSource returns 404 — but local DB should still work
        var service = CreateService(
            vitalSourceSearchResponse: new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await service.LookupAsync("9781590282984");

        Assert.NotNull(result);
        Assert.True(result.FromLocalCache);
        Assert.Equal("Python Programming", result.Title);
        Assert.Equal("Zelle", result.Author);
        Assert.Equal(2024, result.Year);
    }

    [Fact]
    public async Task Lookup_IsbnWithDashes_NormalizesAndFindsInDb()
    {
        _db.CourseBookAssignments.Add(new CourseBookAssignment
        {
            Isbn = "9781590282984",
            Title = "Python Programming",
            CourseId = 0
        });
        await _db.SaveChangesAsync();

        var service = CreateService(
            vitalSourceSearchResponse: new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await service.LookupAsync("978-1-590-28298-4");

        Assert.NotNull(result);
        Assert.True(result.FromLocalCache);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Fallback Order: Local DB → VitalSource → Google Books
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Lookup_NotInDb_FallsToVitalSource()
    {
        // No local DB entry. VitalSource returns a product page.
        var vsSearch = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                <html>
                <head>
                    <meta property="og:title" content="Python Programming | VitalSource" />
                    <meta property="og:description" content="Python Programming is written by John Zelle and published by Franklin Beedle." />
                    <meta property="og:image" content="https://example.com/cover.jpg" />
                </head>
                <body>
                    <a href="/products/python-programming-v9781590282984">Link</a>
                    <script id="__NEXT_DATA__" type="application/json">{"props":{}}</script>
                    $20.00 120 Day
                </body>
                </html>
            """)
        };

        var service = CreateService(vitalSourceSearchResponse: vsSearch, vitalSourceProductResponse: vsSearch);
        var result = await service.LookupAsync("9781590282984");

        Assert.NotNull(result);
        Assert.True(result.DigitalOnVitalSource);
    }

    [Fact]
    public async Task Lookup_NotInDbOrVitalSource_FallsToGoogleBooks()
    {
        // No local DB. VitalSource 404. Google Books returns data.
        var googleResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
                "totalItems": 1,
                "items": [{
                    "volumeInfo": {
                        "title": "Python Programming",
                        "authors": ["John Zelle"],
                        "publisher": "Franklin Beedle",
                        "publishedDate": "2024",
                        "industryIdentifiers": [
                            {"type": "ISBN_13", "identifier": "9781590282984"}
                        ]
                    }
                }]
            }
            """)
        };

        var service = CreateService(
            googleResponse: googleResponse,
            vitalSourceSearchResponse: new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await service.LookupAsync("9781590282984");

        Assert.NotNull(result);
        Assert.Equal("Python Programming", result.Title);
        Assert.Equal("Google Books", result.Source);
    }

    [Fact]
    public async Task Lookup_AllSourcesFail_ReturnsNull()
    {
        var service = CreateService(
            googleResponse: new HttpResponseMessage(HttpStatusCode.NotFound),
            vitalSourceSearchResponse: new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await service.LookupAsync("0000000000000");

        Assert.Null(result);
    }

    [Fact]
    public async Task Lookup_OpenLibrary_IsNotCalled()
    {
        // Verify the removed Open Library is never hit
        var handler = new TrackingHttpHandler();
        var httpClient = new HttpClient(handler);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var service = new IsbnDirectLookupService(
            _db, factoryMock.Object,
            Mock.Of<ILogger<IsbnDirectLookupService>>());

        await service.LookupAsync("9781234567890");

        Assert.DoesNotContain(handler.RequestedUrls,
            url => url.Contains("openlibrary.org"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // IsbnLookupResult defaults
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void IsbnLookupResult_HasCorrectDefaults()
    {
        var result = new IsbnLookupResult();
        Assert.Equal("", result.Isbn);
        Assert.Equal("", result.Title);
        Assert.False(result.DigitalOnVitalSource);
        Assert.False(result.FromLocalCache);
        Assert.Null(result.VitalSourcePrice);
        Assert.Null(result.VitalSourcePriceDays);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Test helpers: Mock HTTP handlers
    // ═══════════════════════════════════════════════════════════════════

    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _googleResponse;
        private readonly HttpResponseMessage? _vsSearchResponse;
        private readonly HttpResponseMessage? _vsProductResponse;

        public MockHttpHandler(
            HttpResponseMessage? googleResponse,
            HttpResponseMessage? vsSearchResponse,
            HttpResponseMessage? vsProductResponse)
        {
            _googleResponse = googleResponse;
            _vsSearchResponse = vsSearchResponse;
            _vsProductResponse = vsProductResponse;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.Contains("googleapis.com"))
                return Task.FromResult(_googleResponse ?? new HttpResponseMessage(HttpStatusCode.NotFound));

            if (url.Contains("vitalsource.com/search"))
                return Task.FromResult(_vsSearchResponse ?? new HttpResponseMessage(HttpStatusCode.NotFound));

            if (url.Contains("vitalsource.com/products"))
                return Task.FromResult(_vsProductResponse ?? _vsSearchResponse ?? new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private class TrackingHttpHandler : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri?.ToString() ?? "");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
