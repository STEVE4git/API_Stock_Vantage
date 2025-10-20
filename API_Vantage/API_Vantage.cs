using Microsoft.AspNetCore.Http.Json;
using System.Globalization;
using System.Net;
using System.Text.Json;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Configure JSON serialization
builder.Services.Configure<JsonOptions>(opts =>
{
    opts.SerializerOptions.WriteIndented = true;
    opts.SerializerOptions.PropertyNamingPolicy = null;
});

WebApplication app = builder.Build();

app.MapGet("/", () => Results.Ok(new { message = "AlphaVantage intraday aggregator. Use /api/intraday/{symbol}" }));

app.MapGet("/api/intraday/{symbol}", async (string symbol, HttpContext http, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(symbol))
    {
        return Results.BadRequest(new { error = "Symbol is required." });
    }
    // API key from appsettings.json
    string? apiKey = app.Configuration["AlphaVantage:ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.Problem("AlphaVantage:ApiKey is not set in appsettings.json or environment.");
    }

    string url = BuildAlphaVantageUrl(symbol, apiKey);

    string json;
    try
    {
        json = await FetchAlphaVantageDataAsync(url, ct);
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: $"Error fetching data from AlphaVantage: {ex.Message}");
    }
    // Parsing json response
    using JsonDocument doc = JsonDocument.Parse(json);
    IResult? validationError = ValidateAlphaVantageResponse(doc);
    if (validationError is not null)
    {
        return validationError;
    }

    JsonElement? timeSeries = ExtractTimeSeriesElement(doc);
    if (timeSeries is null)
    {
        return Results.Problem(detail: "Could not find a 'Time Series' property in the API response.");
    }

    Dictionary<string, (double highSum, double lowSum, long volumeSum, int count)> groupedData = AggregateTimeSeriesData(timeSeries.Value);
    List<object> results = FormatAggregatedResults(groupedData);

    return Results.Json(results);
});

app.Run("http://0.0.0.0:5000");



// Helper Functions

/// Builds the AlphaVantage API URL for a given symbol.
static string BuildAlphaVantageUrl(string symbol, string apiKey) =>
    $"https://www.alphavantage.co/query?function=TIME_SERIES_INTRADAY" +
    $"&symbol={WebUtility.UrlEncode(symbol)}&interval=15min&outputsize=full" +
    $"&apikey={WebUtility.UrlEncode(apiKey)}&datatype=json";



/// Fetches raw JSON data from AlphaVantage.
static async Task<string> FetchAlphaVantageDataAsync(string url, CancellationToken ct)
{
    using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
    return await client.GetStringAsync(url, ct);
}


/// Validates the API response for errors or rate-limit notes.
/// Returns a problem result if invalid, otherwise null.
static IResult? ValidateAlphaVantageResponse(JsonDocument doc)
{
    return doc.RootElement.TryGetProperty("Note", out JsonElement note)
        ? Results.Problem(detail: note.GetString() ?? "AlphaVantage returned a note (rate limit or other).")
        : doc.RootElement.TryGetProperty("Error Message", out JsonElement error)
        ? Results.BadRequest(new { error = error.GetString() ?? "AlphaVantage returned an error." })
        : null;
}


/// Extracts the "Time Series ..." JSON element from the API response.
static JsonElement? ExtractTimeSeriesElement(JsonDocument doc)
{
    foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
    {
        if (prop.Name.StartsWith("Time Series", StringComparison.OrdinalIgnoreCase))
        {
            return prop.Value;
        }
    }
    return null;
}

/// Aggregates high, low, and volume data per day.
static Dictionary<string, (double highSum, double lowSum, long volumeSum, int count)> AggregateTimeSeriesData(JsonElement series)
{
    var grouped = new Dictionary<string, (double highSum, double lowSum, long volumeSum, int count)>(StringComparer.Ordinal);

    foreach (JsonProperty point in series.EnumerateObject())
    {
        string timestamp = point.Name;
        JsonElement fields = point.Value;

        (double? high, double? low, long? volume) = ExtractMetrics(fields);
        if (high is null || low is null || volume is null)
            continue;

        if (!DateTime.TryParseExact(timestamp, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime ts))
            continue;

        string day = ts.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

      
        if (!grouped.TryGetValue(day, out var agg))
            agg = (0, 0, 0, 0);

  
        agg = (
            highSum: agg.highSum + high.Value,
            lowSum: agg.lowSum + low.Value,
            volumeSum: agg.volumeSum + volume.Value,
            count: agg.count + 1
        );

        grouped[day] = agg;
    }

    return grouped;
}



/// Extracts high, low, and volume values from a single JSON data point.
static (double? high, double? low, long? volume) ExtractMetrics(JsonElement obj)
{
    double? high = null, low = null;
    long? volume = null;

    foreach (JsonProperty field in obj.EnumerateObject())
    {
        string name = field.Name.ToLowerInvariant();
        string valueStr = field.Value.GetString() ?? field.Value.ToString();

        if (name.Contains("high") && double.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double h))
        {
            high = h;
        }
        else if (name.Contains("low") && double.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double l))
        {
            low = l;
        }
        else if (name.Contains("volume") && long.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out long v))
        {
            volume = v;
        }
    }

    return (high, low, volume);
}

/// Converts the aggregated dictionary into a list of daily results.
static List<object> FormatAggregatedResults(Dictionary<string, (double highSum, double lowSum, long volumeSum, int count)> grouped)
{
    return grouped
        .Select(kv =>
        {
            string day = kv.Key;
            var (highSum, lowSum, volumeSum, count) = kv.Value; 

            double highAvg = count > 0 ? highSum / count : 0;
            double lowAvg = count > 0 ? lowSum / count : 0;

            return new
            {
                day,
                lowAverage = Math.Round(lowAvg, 6),
                highAverage = Math.Round(highAvg, 6),
                volume = volumeSum
            };
        })
        .OrderByDescending(x => x.day)
        .Cast<object>()
        .ToList();
}
