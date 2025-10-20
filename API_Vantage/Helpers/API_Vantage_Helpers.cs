using System.Globalization;
using System.Net;
using System.Text.Json;

namespace API_Vantage.Helpers
{

    public static class Helpers
    {

        /// Builds the AlphaVantage API URL for a given symbol.
        public static string BuildAlphaVantageUrl(string symbol, string apiKey) =>
            $"https://www.alphavantage.co/query?function=TIME_SERIES_INTRADAY" +
            $"&symbol={WebUtility.UrlEncode(symbol)}&interval=15min&outputsize=full" +
            $"&apikey={WebUtility.UrlEncode(apiKey)}&datatype=json";



        /// Fetches raw JSON data from AlphaVantage.
        public static async Task<string> FetchAlphaVantageDataAsync(string url, CancellationToken ct)
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
            return await client.GetStringAsync(url, ct);
        }


        /// Validates the API response for errors or rate-limit notes.
        /// Returns a problem result if invalid, otherwise null.
        public static IResult? ValidateAlphaVantageResponse(JsonDocument doc)
        {
            return doc.RootElement.TryGetProperty("Note", out JsonElement note)
                ? Results.Problem(detail: note.GetString() ?? "AlphaVantage returned a note (rate limit or other).")
                : doc.RootElement.TryGetProperty("Error Message", out JsonElement error)
                ? Results.BadRequest(new { error = error.GetString() ?? "AlphaVantage returned an error." })
                : null;
        }


        /// Extracts the "Time Series ..." JSON element from the API response.
        public static JsonElement? ExtractTimeSeriesElement(JsonDocument doc)
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
        public static Dictionary<string, (double highSum, double lowSum, long volumeSum, int count)> AggregateTimeSeriesData(JsonElement series)
        {
            Dictionary<string, (double highSum, double lowSum, long volumeSum, int count)> grouped = new(StringComparer.Ordinal);

            foreach (JsonProperty point in series.EnumerateObject())
            {
                string timestamp = point.Name;
                JsonElement fields = point.Value;

                (double? high, double? low, long? volume) = ExtractMetrics(fields);
                if (high is null || low is null || volume is null)
                {
                    continue;
                }

                if (!DateTime.TryParseExact(timestamp, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime ts))
                {
                    continue;
                }

                string day = ts.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);


                if (!grouped.TryGetValue(day, out (double highSum, double lowSum, long volumeSum, int count) agg))
                {
                    agg = (0, 0, 0, 0);
                }

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
        private static (double? high, double? low, long? volume) ExtractMetrics(JsonElement obj)
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
        public static List<object> FormatAggregatedResults(Dictionary<string, (double highSum, double lowSum, long volumeSum, int count)> grouped)
        {
            return grouped
                .Select(kv =>
                {
                    string day = kv.Key;
                    (double highSum, double lowSum, long volumeSum, int count) = kv.Value;

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
    }

}
