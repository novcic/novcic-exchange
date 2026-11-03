using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Matching;
public class OptionReferenceWorker(IServiceScopeFactory scopes,IHttpClientFactory clients,IConfiguration config,ILogger<OptionReferenceWorker> log):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(20));
        while(await timer.WaitForNextTickAsync(stop))try {
            using var client=clients.CreateClient();client.Timeout=TimeSpan.FromSeconds(5);
            var json=await client.GetFromJsonAsync<JsonElement>((config["BybitUrl"]??"https://api.bybit.com")+"/v5/market/tickers?category=option&baseCoin=BTC",stop);
            if(json.GetProperty("retCode").GetInt32()!=0)continue;
            var time=DateTimeOffset.FromUnixTimeMilliseconds(json.GetProperty("time").GetInt64()).UtcDateTime;
            if(time<DateTime.UtcNow.AddSeconds(-30)||time>DateTime.UtcNow.AddSeconds(5))continue;
            using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ExchangeDb>();
            await using var tx=await db.Database.BeginTransactionAsync(stop);
            var records=await db.OptionReferences.ToDictionaryAsync(x=>x.Symbol,stop);
            foreach(var item in json.GetProperty("result").GetProperty("list").EnumerateArray()){
                var symbol=item.GetProperty("symbol").GetString()!;var parts=symbol.Split('-');
                if(parts.Length<4||parts[0]!="BTC"||parts[3]!="C"||!DateTime.TryParseExact(parts[1],new[]{"dMMMyy","ddMMMyy"},CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var date))continue;
                if(!decimal.TryParse(parts[2],CultureInfo.InvariantCulture,out var strike)||!decimal.TryParse(item.GetProperty("markIv").GetString(),CultureInfo.InvariantCulture,out var iv)||iv<=0||iv>5)continue;
                if(!records.TryGetValue(symbol,out var row)){row=new OptionReference{Symbol=symbol};db.OptionReferences.Add(row);}
                row.Strike=strike;row.Expiry=date.Date.AddHours(8);row.Iv=iv;row.Time=time;
            }
            await db.SaveChangesAsync(stop);await tx.CommitAsync(stop);
        }catch(Exception e) when(!stop.IsCancellationRequested){log.LogWarning(e,"Option volatility reference unavailable; affected AMM quotes will expire.");}
    }
}
