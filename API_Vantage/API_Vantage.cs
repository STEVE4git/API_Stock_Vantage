using API_Vantage.Helpers;
using Microsoft.AspNetCore.Http.Json;
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

    string url = Helpers.BuildAlphaVantageUrl(symbol, apiKey);

    string json;
    try
    {
        json = await Helpers.FetchAlphaVantageDataAsync(url, ct);
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: $"Error fetching data from AlphaVantage: {ex.Message}");
    }
    // Parsing json response
    using JsonDocument doc = JsonDocument.Parse(json);
    IResult? validationError = Helpers.ValidateAlphaVantageResponse(doc);
    if (validationError is not null)
    {
        return validationError;
    }

    JsonElement? timeSeries = Helpers.ExtractTimeSeriesElement(doc);
    if (timeSeries is null)
    {
        return Results.Problem(detail: "Could not find a 'Time Series' property in the API response.");
    }

    Dictionary<string, (double highSum, double lowSum, long volumeSum, int count)> groupedData = Helpers.AggregateTimeSeriesData(timeSeries.Value);
    List<object> results = Helpers.FormatAggregatedResults(groupedData);

    return Results.Json(results);
});

app.Run("http://0.0.0.0:5000");





