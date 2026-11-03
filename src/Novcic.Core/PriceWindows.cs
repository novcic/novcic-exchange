namespace Novcic.Core;

public static class PriceWindows
{
    // Piecewise-constant time weighting, with no extrapolation through feed gaps.
    public static decimal? Twap(IEnumerable<IndexSample> source, DateTime start, DateTime end)
    {
        if (end <= start) return null;
        var samples = source.Where(x => x.Time <= end).OrderBy(x => x.Time).ToList();
        var prior = samples.LastOrDefault(x => x.Time <= start);
        if (prior == null || start - prior.Time > TimeSpan.FromSeconds(5)) return null;
        decimal integral = 0;
        var cursor = start;
        foreach (var next in samples.Where(x => x.Time > start))
        {
            if (next.Time - prior.Time > TimeSpan.FromSeconds(5)) return null;
            integral += prior.Price * (next.Time.Ticks - cursor.Ticks);
            cursor = next.Time;
            prior = next;
        }
        if (end - prior.Time > TimeSpan.FromSeconds(5)) return null;
        integral += prior.Price * (end.Ticks - cursor.Ticks);
        return decimal.Round(integral / (end.Ticks - start.Ticks), 8, MidpointRounding.ToEven);
    }
}
