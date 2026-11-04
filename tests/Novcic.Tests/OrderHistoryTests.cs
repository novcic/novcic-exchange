using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Novcic.Core;

namespace Novcic.Tests;

public partial class ProductTests
{
    [Fact]
    public async Task OrderHistoryKeepsAllActiveOrdersAndLatestClosingAttemptsBeforeApplyingLimit()
    {
        await using var db = Db();
        var active = Enumerable.Range(0, 205).Select(n => new Order {
            UserId = alice, Status = n % 2 == 0 ? "Open" : "PartiallyFilled", Quantity = 1, Remaining = 0.5m
        }).ToList();
        db.Orders.AddRange(active);
        await db.SaveChangesAsync();
        var manual = Enumerable.Range(0, 220).Select(_ => new Order { UserId = alice, Status = "Filled" }).ToList();
        db.Orders.AddRange(manual);
        await db.SaveChangesAsync();
        // Many unrelated orders and repeated retries must not leak into the response
        // or crowd ordinary history out before closing attempts have been grouped.
        db.Orders.AddRange(Enumerable.Range(0, 12000).Select(_ => new Order { UserId = bob, Status = "Cancelled" }));
        var jobs = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var retries = Enumerable.Range(0, 4000).Select(n => new Order {
            UserId = alice, ClosingJobId = jobs[n % 2], Status = "Cancelled", ReduceOnly = true
        }).ToList();
        db.Orders.AddRange(retries);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.Database.SetCommandTimeout(5);

        var rows = await new TradingService(db).OrderHistory(alice);
        var expected = active.Concat(manual.Concat(retries.GroupBy(x => x.ClosingJobId).Select(g => g.MaxBy(x => x.Sequence)!))
            .OrderByDescending(x => x.Sequence).Take(200)).OrderByDescending(x => x.Sequence).Select(x => x.Id);
        Assert.Equal(expected, rows.Select(x => x.Id));
        Assert.Equal(405, rows.Count);
        Assert.All(rows, row => Assert.Equal(alice, row.UserId));
        Assert.All(jobs, job => Assert.Single(rows, x => x.ClosingJobId == job));
        Assert.Equal(4000, await db.Orders.CountAsync(x => x.UserId == alice && x.ClosingJobId != null));
    }

    [Fact]
    public async Task AbandonedOrderHistoryCancelsItsInFlightDatabaseQuery()
    {
        await using var locker = Db();
        await using var tx = await locker.Database.BeginTransactionAsync();
        await locker.Database.ExecuteSqlRawAsync("LOCK TABLE exchange.\"Orders\" IN ACCESS EXCLUSIVE MODE");
        await using var reader = Db();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var elapsed = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TradingService(reader).OrderHistory(alice, cancellation.Token));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), "Abandoned read should cancel rather than wait for the database command timeout.");
        await tx.RollbackAsync();
        Assert.Empty(await new TradingService(reader).OrderHistory(alice));
    }
}
