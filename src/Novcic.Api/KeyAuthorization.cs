using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Api;
public static class KeyAuthorization
{
    public static readonly string[] Products=["Spot","Perpetual","Future","Option"];
    public static string[]? Allowed(HttpContext c)=>c.User.FindFirstValue("scope")==null?null:(c.User.FindFirstValue("products")??"").Split(',',StringSplitOptions.RemoveEmptyEntries);
    public static async Task<bool> Permit(HttpContext c,string body)
    {
        var allowed=Allowed(c);if(allowed==null)return true;
        var path=c.Request.Path.Value!;
        if(c.Request.Method=="GET")return new[]{"/api/balances","/api/orders","/api/trades","/api/ledger","/api/portfolio","/api/custody/history","/api/market","/api/config","/api/products","/api/candles","/api/network"}.Contains(path);
        var preview=path is "/api/order-preview" or "/api/leverage-preview";
        if(!preview&&c.User.FindFirstValue("scope")!="Trade")return false;
        if(path=="/api/cancel-all")return allowed.Length>0;
        if(path is "/api/leverage" or "/api/leverage-preview")return allowed.Contains("Perpetual")&&allowed.Contains("Future");
        var db=c.RequestServices.GetRequiredService<ExchangeDb>();string? instrument;
        if(path is "/api/orders" or "/api/order-preview" or "/api/position-protection" or "/api/stop-closing" or "/api/amend-order"){
            try{instrument=JsonNode.Parse(body)?["instrument"]?.GetValue<string>()??"BTCUSDT";}catch{throw new RuleException("Invalid order request.");}
        }else if(path.StartsWith("/api/orders/")&&path.EndsWith("/cancel")){
            if(!Guid.TryParse(path.Split('/')[3],out var id))return false;
            var user=Guid.Parse(c.User.FindFirstValue("sub")!);
            instrument=await db.Orders.Where(o=>o.Id==id&&o.UserId==user).Select(o=>o.InstrumentId).SingleOrDefaultAsync();
        }else return false;
        var kind=await db.Instruments.Where(i=>i.Id==instrument).Select(i=>i.Kind).SingleOrDefaultAsync();
        return kind!=null&&allowed.Contains(kind);
    }
    public static async Task<string> Filter(HttpContext c,string json)
    {
        var allowed=Allowed(c);if(allowed==null||c.Request.Method!="GET")return json;
        var path=c.Request.Path.Value;if(path is not ("/api/orders" or "/api/trades" or "/api/portfolio"))return json;
        var db=c.RequestServices.GetRequiredService<ExchangeDb>();
        var ids=(await db.Instruments.Where(i=>allowed.Contains(i.Kind)).Select(i=>i.Id).ToListAsync()).ToHashSet();
        var root=JsonNode.Parse(json)!;var rows=(path=="/api/portfolio"?root["positions"]:root)?.AsArray();
        if(rows!=null)for(int n=rows.Count-1;n>=0;n--)if(!ids.Contains(rows[n]?["instrumentId"]?.GetValue<string>()??""))rows.RemoveAt(n);
        return root.ToJsonString();
    }
}
