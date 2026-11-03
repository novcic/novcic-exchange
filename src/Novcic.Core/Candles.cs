namespace Novcic.Core;

public static class Candles
{
    public static readonly Dictionary<string, int> Intervals = new() { ["1m"] = 60, ["5m"] = 300, ["15m"] = 900, ["1h"] = 3600 };
    public static IReadOnlyList<Candle> Build(IEnumerable<Trade> source, int seconds, DateTime now, int limit = 500)
    {
        var trades = source.OrderBy(x => x.CreatedAt).ThenBy(x => x.Sequence).ToArray();
        if (trades.Length == 0) return [];
        long Bucket(DateTime d) => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)).ToUnixTimeSeconds() / seconds * seconds;
        var first = Bucket(trades[0].CreatedAt);
        var end = Bucket(now);
        var start = Math.Max(first, end - (limit - 1L) * seconds);
        var previous = trades.Where(x => Bucket(x.CreatedAt) < start).LastOrDefault()?.Price ?? trades[0].Price;
        var buckets = trades.Where(x => Bucket(x.CreatedAt) >= start).GroupBy(x => Bucket(x.CreatedAt)).ToDictionary(x => x.Key, x => x.ToArray());
        var result = new List<Candle>();
        for (var t = start; t <= end; t += seconds)
        {
            var group = buckets.GetValueOrDefault(t) ?? [];
            var open = previous;
            var close = group.LastOrDefault()?.Price ?? open;
            result.Add(new Candle(t, open, Math.Max(open, group.Select(x => x.Price).DefaultIfEmpty(open).Max()),
                Math.Min(open, group.Select(x => x.Price).DefaultIfEmpty(open).Min()), close, group.Sum(x => x.Quantity)));
            previous = close;
        }
        return result;
    }
}
