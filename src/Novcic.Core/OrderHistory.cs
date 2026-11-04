using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Novcic.Core;

public partial class TradingService
{
    public async Task<List<Order>> OrderHistory(Guid user, CancellationToken cancellationToken = default)
    {
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var query = db.Orders.AsNoTracking().Where(x => x.UserId == user);
        var active = await query.Where(x => x.Status == "Open" || x.Status == "PartiallyFilled").ToListAsync(cancellationToken);
        // The partial (ClosingJobId, Sequence) index is essential: without it,
        // every closing retry scans the entire exchange's order history.
        var history = await query.Where(x => x.Status != "Open" && x.Status != "PartiallyFilled" &&
            (x.ClosingJobId == null || !db.Orders.Any(n => n.ClosingJobId == x.ClosingJobId && n.Sequence > x.Sequence)))
            .OrderByDescending(x => x.Sequence).Take(200).ToListAsync(cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return active.Concat(history).OrderByDescending(x => x.Sequence).ToList();
    }
}
