using Novcic.Core;
namespace Novcic.Tests;
public class PriceWindowTests
{
    [Fact] public void UsesElapsedTimeInsteadOfSampleCount()
    {
        var start=DateTime.UtcNow;
        var samples=new[]{new IndexSample{Time=start,Price=100},new IndexSample{Time=start.AddSeconds(1),Price=200},new IndexSample{Time=start.AddSeconds(5),Price=200}};
        Assert.Equal(180,PriceWindows.Twap(samples,start,start.AddSeconds(5)));
    }
    [Fact] public void MissingCoverageDoesNotProduceSettlementPrice()
    {
        var start=DateTime.UtcNow;
        Assert.Null(PriceWindows.Twap([new IndexSample{Time=start,Price=100}],start,start.AddSeconds(10)));
        Assert.Null(PriceWindows.Twap([new IndexSample{Time=start.AddSeconds(1),Price=100}],start,start.AddSeconds(2)));
    }
}
