using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Novcic.Core;

namespace Novcic.Custody;
public class ChainWorker(IServiceScopeFactory scopes, Ethereum chain, IHttpClientFactory clients, IConfiguration config, ILogger<ChainWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while(await timer.WaitForNextTickAsync(stop)) {
            try { await Tick(); }
            catch(Exception e) when(!stop.IsCancellationRequested) { log.LogWarning("Chain reconciliation failed: {Message}", e.Message); }
        }
    }
    private async Task Tick()
    {
        using var scope=scopes.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<ExchangeDb>();
        await db.Database.OpenConnectionAsync();
        // A session lock serializes signing/nonces across service replicas without
        // holding the financial authority's separate transaction lock during RPC.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(74001236)");
        try {
            if(Ethereum.Long((await chain.Rpc("eth_chainId")).GetString()!)!=31337) throw new InvalidOperationException("Unexpected network identity.");
            var head=Ethereum.Long((await chain.Rpc("eth_blockNumber")).GetString()!);
            var cursor=await db.ChainCursors.FindAsync(chain.Generation);
            if(cursor==null) {cursor=new ChainCursor{Generation=chain.Generation,Block=0};db.ChainCursors.Add(cursor);}
            if(cursor.Quarantined) return;
            if(cursor.BlockHash!="") {
                var block=await chain.Rpc("eth_getBlockByNumber",Ethereum.Hex(cursor.Block),false);
                if(block.ValueKind==JsonValueKind.Null || block.GetProperty("hash").GetString()!=cursor.BlockHash) {
                    cursor.Quarantined=true; await db.SaveChangesAsync(); log.LogError("Chain identity/reorganization mismatch. Custody quarantined.");return;
                }
            }
            await Scan(db,cursor,head);
            foreach(var deposit in await db.ChainTransfers.Where(x=>x.Generation==chain.Generation&&x.Kind=="Deposit"&&(x.Status=="Pending"||x.Status=="Confirmed")).ToListAsync()) {
                if(deposit.Block>head-2) continue;
                var canonical=await chain.Rpc("eth_getBlockByNumber",Ethereum.Hex(deposit.Block!.Value),false);
                if(canonical.GetProperty("hash").GetString()!=deposit.BlockHash) {cursor.Quarantined=true;await db.SaveChangesAsync();return;}
                deposit.Status="Confirmed";await db.SaveChangesAsync();
                await Authority($"/deposit-credit/{deposit.UserId}/{deposit.Id}",new{},deposit.Id+"credit");
                db.Entry(deposit).State=EntityState.Detached;
            }
            await Outgoing(db,head);
            await Sweeps(db);
        } finally { await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(74001236)"); }
    }
    private async Task Scan(ExchangeDb db,ChainCursor cursor,long head)
    {
        var addresses=await db.DepositAddresses.AsNoTracking().ToDictionaryAsync(x=>x.Address);
        var end=Math.Min(head,cursor.Block+500);
        if(end<=cursor.Block)return;
        await using var tx=await db.Database.BeginTransactionAsync();
        foreach(var asset in new[]{"BTC","USDT"}) {
            var logs=await chain.Rpc("eth_getLogs",new {fromBlock=Ethereum.Hex(cursor.Block+1),toBlock=Ethereum.Hex(end),address=chain.Token(asset),topics=new[]{"0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef"}});
            foreach(var entry in logs.EnumerateArray()) {
                var destination="0x"+entry.GetProperty("topics")[2].GetString()![26..];
                if(!addresses.TryGetValue(destination,out var user))continue;
                var units=Ethereum.Integer(entry.GetProperty("data").GetString()!);
                if(units<=0)continue;
                var amount=(decimal)units/(decimal)BigInteger.Pow(10,chain.Decimals(asset));
                db.ChainTransfers.Add(new ChainTransfer{Generation=chain.Generation,UserId=user.UserId,Asset=asset,Amount=amount,Address=destination,
                    Hash=entry.GetProperty("transactionHash").GetString(),LogIndex=Ethereum.Long(entry.GetProperty("logIndex").GetString()!),Block=Ethereum.Long(entry.GetProperty("blockNumber").GetString()!),BlockHash=entry.GetProperty("blockHash").GetString()});
            }
        }
        cursor.Block=end;cursor.BlockHash=(await chain.Rpc("eth_getBlockByNumber",Ethereum.Hex(end),false)).GetProperty("hash").GetString()!;
        await db.SaveChangesAsync();await tx.CommitAsync();
    }
    private async Task Outgoing(ExchangeDb db,long head)
    {
        var jobs=await db.ChainTransfers.Where(x=>x.Generation==chain.Generation&&x.Kind!="Deposit"&&x.Status!="Completed"&&x.Status!="Failed").OrderBy(x=>x.CreatedAt).ToListAsync();
        foreach(var job in jobs) {
            if(job.Status=="Requested") {
                var key=chain.TreasuryKey;var destination=job.Address;
                if(job.Kind=="Sweep") { var owner=await db.DepositAddresses.FindAsync(job.UserId);key=chain.Decrypt(owner!.EncryptedKey);destination=chain.Treasury; }
                var native=job.Kind=="Gas"; var units=new BigInteger(job.Amount*(decimal)BigInteger.Pow(10,native?18:chain.Decimals(job.Asset)));
                if(!native && await chain.Balance(job.Asset,job.Kind=="Sweep"?job.Address:chain.Treasury)<units)continue;
                job.Sender=new Nethereum.Web3.Accounts.Account(key,31337).Address.ToLowerInvariant();
                var networkNonce=Ethereum.Long((await chain.Rpc("eth_getTransactionCount",job.Sender,"pending")).GetString()!);
                var durableNonce=await db.ChainTransfers.Where(x=>x.Sender==job.Sender).MaxAsync(x=>x.Nonce)??-1;
                job.Nonce=Math.Max(networkNonce,durableNonce+1);
                var signed=await chain.Sign(key,destination,native?null:job.Asset,units,job.Nonce.Value);
                job.RawTransaction=signed.Raw;job.Hash=signed.Hash;job.Status="Signed";await db.SaveChangesAsync();
            }
            if(job.Status is "Signed" or "Broadcast") {
                var receipt=await chain.Rpc("eth_getTransactionReceipt",job.Hash!);
                if(receipt.ValueKind==JsonValueKind.Null) {
                    try{await chain.Rpc("eth_sendRawTransaction",job.RawTransaction!);job.Status="Broadcast";await db.SaveChangesAsync();}
                    catch(Exception e){job.Error=e.Message;await db.SaveChangesAsync();} // Unknown outcome: retain reservation and retry same signed bytes.
                    continue;
                }
                job.Block=Ethereum.Long(receipt.GetProperty("blockNumber").GetString()!);job.BlockHash=receipt.GetProperty("blockHash").GetString();
                if(job.Block>head-2)continue;
                var canonical=await chain.Rpc("eth_getBlockByNumber",Ethereum.Hex(job.Block.Value),false);
                if(canonical.GetProperty("hash").GetString()!=job.BlockHash)throw new InvalidOperationException("Outgoing receipt is not canonical.");
                job.Status=Ethereum.Long(receipt.GetProperty("status").GetString()!)==1?"Confirmed":"Reverted";await db.SaveChangesAsync();
            }
            if(job.Status is "Confirmed" or "Reverted") {
                if(job.Kind=="Withdrawal") {
                    await Authority($"/withdraw-finish/{job.UserId}/{job.Id}/{(job.Status=="Confirmed"?"true":"false")}",new{},job.Id+"finish");
                    db.Entry(job).State=EntityState.Detached;
                } else {job.Status=job.Status=="Confirmed"?"Completed":"Failed";await db.SaveChangesAsync();}
            }
        }
    }
    private async Task Sweeps(ExchangeDb db)
    {
        foreach(var user in await db.DepositAddresses.ToListAsync()) {
            if(await db.ChainTransfers.AnyAsync(x=>x.UserId==user.UserId&&x.Kind=="Deposit"&&x.Status!="Credited"))continue;
            if(await db.ChainTransfers.AnyAsync(x=>x.UserId==user.UserId&&(x.Kind=="Sweep"||x.Kind=="Gas")&&x.Status!="Completed"&&x.Status!="Failed"))continue;
            foreach(var asset in new[]{"BTC","USDT"}) {
                var balance=await chain.Balance(asset,user.Address);if(balance==0)continue;
                var eth=Ethereum.Integer((await chain.Rpc("eth_getBalance",user.Address,"latest")).GetString()!);
                if(eth<BigInteger.Pow(10,15)) {
                    db.ChainTransfers.Add(new ChainTransfer{UserId=user.UserId,Generation=chain.Generation,Kind="Gas",Asset="ETH",Amount=0.01m,Address=user.Address,Status="Requested"});
                } else {
                    db.ChainTransfers.Add(new ChainTransfer{UserId=user.UserId,Generation=chain.Generation,Kind="Sweep",Asset=asset,Amount=(decimal)balance/(decimal)BigInteger.Pow(10,chain.Decimals(asset)),Address=user.Address,Status="Requested"});
                }
                await db.SaveChangesAsync();break;
            }
        }
    }
    private async Task Authority(string path,object body,string identity)
    {
        var client=clients.CreateClient(); using var request=new HttpRequestMessage(HttpMethod.Post,config["MatchingUrl"]+path);
        request.Headers.Add("X-Internal-Key",config["InternalKey"]);
        request.Headers.Add("Idempotency-Key",new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).Take(16).ToArray()).ToString());
        request.Content=JsonContent.Create(body,options:Wire.Json);using var result=await client.SendAsync(request);
        if(!result.IsSuccessStatusCode)throw new InvalidOperationException("Financial reconciliation rejected: "+await result.Content.ReadAsStringAsync());
    }
}
