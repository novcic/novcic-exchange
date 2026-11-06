using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Novcic.Core;

namespace Novcic.Tests;

public class SettlementHistoryTests
{
    internal static DateTime Expiry => new DateTime(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute, DateTimeKind.Utc).AddMinutes(-2);
    internal static JsonObject Response(DateTime expiry, decimal price = 52000)
    {
        var rows = new JsonArray();
        for (var minute = 29; minute >= 0; minute--)
        {
            var time = new DateTimeOffset(expiry.AddMinutes(-30 + minute)).ToUnixTimeMilliseconds();
            var p = price.ToString(CultureInfo.InvariantCulture);
            rows.Add(new JsonArray(time.ToString(CultureInfo.InvariantCulture), p, p, p, p));
        }
        return new JsonObject
        {
            ["retCode"] = 0, ["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["result"] = new JsonObject { ["category"] = "linear", ["symbol"] = "BTCUSDT", ["list"] = rows }
        };
    }
    internal static SettlementReference Parse(JsonObject response, DateTime expiry) =>
        SettlementHistory.Parse(JsonSerializer.SerializeToElement(response), expiry, DateTime.UtcNow);

    [Fact] public void UsesAllThirtyClosedMinutesWithExactArithmeticAndRetainsEvidence()
    {
        var expiry = Expiry;
        var response = Response(expiry, 52000.00000001m);
        var rows = (JsonArray)response["result"]!["list"]!;
        rows[0]![2] = "52001.00000001"; rows[0]![4] = "52001.00000001";
        var result = Parse(response, expiry);
        Assert.Equal(52000.03333334m, result.Price);
        Assert.Equal(SettlementHistory.HistoricalSource, result.Source);
        Assert.Equal(expiry, result.Expiry);
        Assert.Equal(30, JsonDocument.Parse(result.EvidenceJson).RootElement.GetProperty("result").GetProperty("list").GetArrayLength());
    }

    [Theory]
    [InlineData("missing")][InlineData("duplicate")][InlineData("outside")]
    [InlineData("unaligned")][InlineData("negative")][InlineData("ohlc")]
    [InlineData("symbol")][InlineData("category")][InlineData("rejected")]
    [InlineData("future-server")][InlineData("unclosed")]
    public void RejectsUnusableHistoricalData(string defect)
    {
        var expiry = Expiry; var response = Response(expiry);
        var rows = (JsonArray)response["result"]!["list"]!;
        switch (defect)
        {
            case "missing": rows.RemoveAt(0); break;
            case "duplicate": rows[0] = rows[1]!.DeepClone(); break;
            case "outside": rows[0]![0] = new DateTimeOffset(expiry).ToUnixTimeMilliseconds().ToString(); break;
            case "unaligned": rows[0]![0] = (long.Parse(rows[0]![0]!.GetValue<string>()) + 1).ToString(); break;
            case "negative": rows[0]![3] = "0"; break;
            case "ohlc": rows[0]![4] = "53000"; break;
            case "symbol": response["result"]!["symbol"] = "ETHUSDT"; break;
            case "category": response["result"]!["category"] = "inverse"; break;
            case "rejected": response["retCode"] = 10001; break;
            case "future-server": response["time"] = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds(); break;
            case "unclosed": response["time"] = new DateTimeOffset(expiry).ToUnixTimeMilliseconds(); break;
        }
        Assert.Throws<InvalidDataException>(() => Parse(response, expiry));
    }

    [Fact] public void DoesNotUseCurrentOrNotFinalizedCandles()
    {
        var expiry = Expiry;
        Assert.Throws<InvalidDataException>(() => SettlementHistory.Parse(JsonSerializer.SerializeToElement(Response(expiry)), expiry, expiry.AddSeconds(30)));
        Assert.Throws<InvalidDataException>(() => Parse(Response(expiry), expiry.AddSeconds(1)));
    }
}

public partial class ProductTests
{
    [Theory][InlineData("Option")][InlineData("Future")]
    public async Task MissedExpiryRecoversFromHistoricalWindowExactlyOnceAfterRestart(string kind)
    {
        var instrument = kind == "Option" ? "CALL" : "DELIVERY";
        if (kind == "Future") { await using var db = Db(); db.Instruments.Add(new Instrument { Id = instrument, Kind = kind, Expiry = DateTime.UtcNow.AddDays(1) }); await db.SaveChangesAsync(); }
        await Order(bob, "Sell", .1m, kind == "Option" ? 1000 : 50000, instrument);
        await Order(alice, "Buy", .1m, null, instrument);
        var pendingOrder = await Order(bob, "Sell", .01m, kind == "Option" ? 1000 : 50000, instrument);
        var expiry = SettlementHistoryTests.Expiry;
        await using (var db = Db()) { (await db.Instruments.FindAsync(instrument))!.Expiry = expiry; await db.SaveChangesAsync(); }
        // Expiry transition works even without a fresh live feed; it cannot release collateral.
        await Price(70000, true);
        await Cmd(alice, "expiry", new { }, async s => { await s.ProcessMarkets(); return new { }; });
        await using (var db = Db())
        {
            Assert.Equal("AwaitingSettlement", (await db.Instruments.FindAsync(instrument))!.Status);
            Assert.Equal("Cancelled", (await db.Orders.FindAsync(pendingOrder.Id))!.Status);
            Assert.Equal(.1m, (await db.Positions.FindAsync(alice, instrument))!.Quantity);
            Assert.False((await new TradingService(db).Risk(bob)).PriceFresh);
            Assert.Null(await new TradingService(db).PublicMark(instrument));
            db.SettlementReferences.Add(SettlementHistoryTests.Parse(SettlementHistoryTests.Response(expiry), expiry));
            await db.SaveChangesAsync();
        }
        // New service/DbContext represents a restart; the stored window survives it.
        await Cmd(alice, "recover", new { }, async s => { await s.ProcessMarkets(); return new { }; });
        decimal cash; int journalCount;
        await using (var db = Db())
        {
            var settled = (await db.Instruments.FindAsync(instrument))!;
            Assert.Equal("Settled", settled.Status); Assert.Equal(52000, settled.SettlementPrice);
            Assert.Equal(SettlementHistory.HistoricalSource, settled.SettlementSource);
            Assert.Equal(0, (await db.Positions.FindAsync(alice, instrument))!.Quantity);
            cash = (await db.MarginBalances.FindAsync(alice))!.Cash;
            Assert.Equal(200, cash); journalCount = await db.Journal.CountAsync();
            Assert.Equal(70000, (await db.IndexSamples.SingleAsync()).Price); // No fake historical live samples.
        }
        await Cmd(alice, "retry-recovery", new { }, async s => { await s.ProcessMarkets(); return new { }; });
        await using (var db = Db()) { Assert.Equal(cash, (await db.MarginBalances.FindAsync(alice))!.Cash); Assert.Equal(journalCount, await db.Journal.CountAsync()); }
        await Conserve();
    }

    [Fact] public async Task CompleteLocalTwapTakesPrecedenceOverHistoricalFallback()
    {
        var expiry = SettlementHistoryTests.Expiry;
        await using (var db = Db())
        {
            (await db.Instruments.FindAsync("CALL"))!.Expiry = expiry;
            for (var time = expiry.AddMinutes(-30); time <= expiry; time = time.AddSeconds(4))
                db.IndexSamples.Add(new IndexSample { Time = time, Price = 51000, Spot = 51000 });
            db.SettlementReferences.Add(SettlementHistoryTests.Parse(SettlementHistoryTests.Response(expiry), expiry));
            await db.SaveChangesAsync();
        }
        await Cmd(alice, "expiry-local", new { }, async s => { await s.ProcessMarkets(); return new { }; });
        await using var verify = Db(); var settled = (await verify.Instruments.FindAsync("CALL"))!;
        Assert.Equal(51000, settled.SettlementPrice); Assert.Equal(SettlementHistory.LocalSource, settled.SettlementSource);
    }

    [Theory][InlineData("Option")][InlineData("Future")]
    public async Task PoolQuotesResumeAfterMissedExpiryIsRecovered(string kind)
    {
        if (kind == "Future") { await using var db = Db(); db.Instruments.Add(new Instrument { Id = "DELIVERY", Kind = kind, Expiry = DateTime.UtcNow.AddDays(1) }); await db.SaveChangesAsync(); }
        var instrument = kind == "Option" ? "CALL" : "DELIVERY";
        var deposit = new QueueDeposit(.1m, 5000);
        await Cmd(alice, "liquidity-deposit", deposit, s => s.QueueLiquidity(alice, deposit)); await Price(50000); await Cycle();
        await Order(bob, "Buy", .0001m, null, instrument);
        var expiry = SettlementHistoryTests.Expiry;
        await using (var db = Db()) { (await db.Instruments.FindAsync(instrument))!.Expiry = expiry; await db.SaveChangesAsync(); }
        await Cmd(alice, "expiry-pending", new { }, async s => { await s.ProcessMarkets(); return new { }; });
        await Price(50000); await Cycle();
        await using (var db = Db())
        {
            Assert.False(await db.Orders.AnyAsync(o => o.UserId == TradingService.AmmAccount && o.Status == "Open"));
            Assert.NotEqual(0, (await db.Positions.FindAsync(TradingService.AmmAccount, instrument))!.Quantity);
            Assert.Null((await new TradingService(db).LiquidityPerformance(alice)).CurrentValueUsd);
            db.SettlementReferences.Add(SettlementHistoryTests.Parse(SettlementHistoryTests.Response(expiry), expiry));
            await db.SaveChangesAsync();
        }
        await Price(50000);
        await Cmd(alice, "expiry-recovery", new { }, async s => { await s.ProcessMarkets(); return new { }; });
        await Price(50000); await Cycle();
        await using (var db = Db())
        {
            Assert.Equal(0, (await db.Positions.FindAsync(TradingService.AmmAccount, instrument))!.Quantity);
            foreach (var symbol in new[] { "BTCUSDT", "BTC-PERP" }) foreach (var side in new[] { "Buy", "Sell" })
                Assert.True(await db.Orders.AnyAsync(o => o.UserId == TradingService.AmmAccount && o.Status == "Open" && o.InstrumentId == symbol && o.Side == side));
            Assert.False(await db.Orders.AnyAsync(o => o.Status == "Open" && o.InstrumentId == instrument));
        }
        await Conserve();
    }
}
