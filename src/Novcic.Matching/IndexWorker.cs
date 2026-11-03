using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Matching;
public class IndexWorker(IServiceScopeFactory scopes,IHttpClientFactory clients,IConfiguration config,ILogger<IndexWorker> log):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
        while(await timer.WaitForNextTickAsync(stop))try {
            using var client=clients.CreateClient();client.Timeout=TimeSpan.FromSeconds(3);
            async Task<(decimal Price,DateTime Time)> Quote(string category) {
                var json=await client.GetFromJsonAsync<JsonElement>((config["BybitUrl"]??"https://api.bybit.com")+$"/v5/market/tickers?category={category}&symbol=BTCUSDT",stop);
                if(json.GetProperty("retCode").GetInt32()!=0)throw new InvalidOperationException("Bybit rejected market-data query.");
                var ticker=json.GetProperty("result").GetProperty("list")[0];
                var bid=decimal.Parse(ticker.GetProperty("bid1Price").GetString()!,CultureInfo.InvariantCulture);var ask=decimal.Parse(ticker.GetProperty("ask1Price").GetString()!,CultureInfo.InvariantCulture);
                if(bid<=0||ask<bid||(ask-bid)/bid>0.01m)throw new InvalidOperationException("Invalid market-data quote.");
                return((bid+ask)/2,DateTimeOffset.FromUnixTimeMilliseconds(json.GetProperty("time").GetInt64()).UtcDateTime);
            }
            var spot=await Quote("spot");if(DateTime.UtcNow-spot.Time>TimeSpan.FromSeconds(5)||spot.Time>DateTime.UtcNow.AddSeconds(5))continue;
            decimal? perp=null;
            try{var quote=await Quote("linear");if(DateTime.UtcNow-quote.Time<=TimeSpan.FromSeconds(5)&&quote.Time<=DateTime.UtcNow.AddSeconds(5)&&Math.Abs(quote.Price/spot.Price-1)<=0.02m)perp=quote.Price;}catch(Exception e) when(!stop.IsCancellationRequested){log.LogDebug(e,"Perpetual contribution unavailable.");}
            using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ExchangeDb>();
            db.IndexSamples.Add(new IndexSample{Time=spot.Time,Spot=spot.Price,Perpetual=perp,Price=perp.HasValue?(spot.Price+perp.Value)/2:spot.Price});await db.SaveChangesAsync(stop);
        }catch(Exception e) when(!stop.IsCancellationRequested){log.LogDebug(e,"Reference feed unavailable; no synthetic price substituted.");}
    }
}
