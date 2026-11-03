using Novcic.Core;
namespace Novcic.Tests;
public class CandleTests
{
    [Fact]
    public void EmptyMarketHasNoInventedPrice() => Assert.Empty(Candles.Build([], 60, DateTime.UtcNow));
    [Fact]
    public void EveryOpenEqualsPreviousCloseAndGapsAreFlat()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var candles = Candles.Build([
            new Trade { CreatedAt = start.AddSeconds(5), Price = 100.01m, Quantity = 0.000001m, Sequence = 1 },
            new Trade { CreatedAt = start.AddSeconds(20), Price = 103.02m, Quantity = 0.000002m, Sequence = 2 },
            new Trade { CreatedAt = start.AddMinutes(3), Price = 99.99m, Quantity = 1m, Sequence = 3 }
        ], 60, start.AddMinutes(4));
        Assert.Equal(5, candles.Count);
        for (int i = 1; i < candles.Count; i++) Assert.Equal(candles[i - 1].Close, candles[i].Open);
        Assert.Equal(0.000003m, candles[0].Volume);
        foreach (var i in new[] { 1, 2, 4 })
        { Assert.Equal(candles[i].Open, candles[i].High); Assert.Equal(candles[i].Open, candles[i].Low); Assert.Equal(candles[i].Open, candles[i].Close); Assert.Equal(0, candles[i].Volume); }
        Assert.Equal(103.02m, candles[3].High);
        Assert.Equal(99.99m, candles[3].Low);
    }
    [Theory]
    [InlineData(60)] [InlineData(300)] [InlineData(900)] [InlineData(3600)]
    public void LimitedHistoryRetainsPriceFromEarlierTrades(int seconds)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var candles = Candles.Build([new Trade { CreatedAt = start, Price = 12345.67m, Quantity = 1 }], seconds, start.AddSeconds(seconds * 1000L));
        Assert.Equal(500, candles.Count);
        Assert.All(candles, x => { Assert.Equal(12345.67m, x.Open); Assert.Equal(12345.67m, x.Close); Assert.Equal(0, x.Volume); });
    }
}
