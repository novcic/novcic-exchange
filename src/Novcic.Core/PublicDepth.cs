namespace Novcic.Core;

public record DepthLevel(decimal Price, decimal Quantity, decimal Total);
public static class PublicDepth
{
    public static IReadOnlyList<DepthLevel> Build(IEnumerable<(decimal Price, decimal Quantity)> orders, bool bids)
    {
        var groups = orders.Where(x => x.Quantity > 0).GroupBy(x => x.Price);
        var sorted = bids ? groups.OrderByDescending(x => x.Key) : groups.OrderBy(x => x.Key);
        decimal cumulative = 0;
        return sorted.Select(g => { var quantity = g.Sum(x => x.Quantity); cumulative += quantity; return new DepthLevel(g.Key, quantity, cumulative); }).ToArray();
    }
}
