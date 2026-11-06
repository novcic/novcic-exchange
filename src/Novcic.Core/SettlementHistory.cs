using System.Globalization;
using System.Text.Json;

namespace Novcic.Core;

// Historical candles are a distinct, disclosed fallback, never synthetic live index samples.
public class SettlementReference
{
    public DateTime Expiry { get; set; }
    public decimal Price { get; set; }
    public string Source { get; set; } = SettlementHistory.HistoricalSource;
    public DateTime CapturedAt { get; set; }
    public string EvidenceJson { get; set; } = "";
}

public static class SettlementHistory
{
    public const string LocalSource = "LocalIndexTwap";
    public const string HistoricalSource = "BybitIndex1mCloseAverage";

    public static SettlementReference Parse(JsonElement response, DateTime expiry, DateTime now)
    {
        if (expiry.Kind != DateTimeKind.Utc || now.Kind != DateTimeKind.Utc ||
            expiry.Ticks % TimeSpan.TicksPerMinute != 0 || now < expiry.AddMinutes(1))
            throw new InvalidDataException("Recovery requires a minute-aligned expiry and finalized historical candles.");
        if (response.GetProperty("retCode").GetInt32() != 0)
            throw new InvalidDataException("Historical index request was rejected.");
        var serverTime = DateTimeOffset.FromUnixTimeMilliseconds(response.GetProperty("time").GetInt64()).UtcDateTime;
        if (serverTime < expiry.AddMinutes(1) || serverTime > now.AddSeconds(5))
            throw new InvalidDataException("Historical response timestamp is invalid.");
        var result = response.GetProperty("result");
        if (result.GetProperty("category").GetString() != "linear" || result.GetProperty("symbol").GetString() != "BTCUSDT")
            throw new InvalidDataException("Historical index response does not describe linear BTCUSDT.");
        var candles = new SortedDictionary<long, decimal>();
        var start = new DateTimeOffset(expiry.AddMinutes(-30)).ToUnixTimeMilliseconds();
        foreach (var row in result.GetProperty("list").EnumerateArray())
        {
            if (row.GetArrayLength() != 5) throw new InvalidDataException("Invalid index candle shape.");
            var time = long.Parse(row[0].GetString()!, CultureInfo.InvariantCulture);
            var prices = Enumerable.Range(1, 4).Select(i => decimal.Parse(row[i].GetString()!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)).ToArray();
            var (open, high, low, close) = (prices[0], prices[1], prices[2], prices[3]);
            if (prices.Any(p => p <= 0 || p > 1_000_000_000m) || low > high || open < low || open > high || close < low || close > high)
                throw new InvalidDataException("Invalid historical index candle prices.");
            if (time < start || time >= start + 30 * 60_000 || (time - start) % 60_000 != 0 || !candles.TryAdd(time, close))
                throw new InvalidDataException("Historical candles contain duplicate or out-of-window timestamps.");
        }
        if (candles.Count != 30 || Enumerable.Range(0, 30).Any(i => !candles.ContainsKey(start + i * 60_000)))
            throw new InvalidDataException("The complete 30-minute historical index window is required.");
        return new SettlementReference
        {
            Expiry = expiry, Price = decimal.Round(candles.Values.Average(), 8, MidpointRounding.ToEven),
            CapturedAt = now, EvidenceJson = response.GetRawText()
        };
    }
}
