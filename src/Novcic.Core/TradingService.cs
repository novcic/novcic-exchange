using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Novcic.Core;

public class TradingService(ExchangeDb db)
{
    public static readonly string[] Kinds = ["Funding", "Unified Trading"];
    public static readonly string[] Assets = ["BTC", "USDT"];

    // One database-level command lock makes retries and multiple service instances safe.
    // It intentionally favors correctness over parallel symbol processing for this single-pair MVP.
    public async Task<string> Command(Guid userId, string key, string operation, object body, Func<Task<object>> action)
    {
        if (!Guid.TryParse(key, out _)) throw new RuleException("A UUID Idempotency-Key header is required.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operation + JsonSerializer.Serialize(body, Wire.Json))));
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74001234)");
        var existing = await db.Receipts.FindAsync(userId, key);
        if (existing != null)
        {
            if (existing.Fingerprint != fingerprint) throw new RuleException("This idempotency key was already used for a different request.");
            await tx.CommitAsync();
            return existing.Response;
        }
        await EnsureWallets(userId);
        var response = JsonSerializer.Serialize(await action(), Wire.Json);
        db.Receipts.Add(new Receipt { UserId = userId, Key = key, Fingerprint = fingerprint, Response = response });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return response;
    }
    private async Task EnsureWallets(Guid user)
    {
        var wallets = await db.Wallets.Where(x => x.UserId == user).ToListAsync();
        foreach (var kind in Kinds)
        foreach (var asset in Assets)
            if (!wallets.Any(x => x.Kind == kind && x.Asset == asset))
                db.Wallets.Add(new Wallet { UserId = user, Kind = kind, Asset = asset });
        await db.SaveChangesAsync();
    }
    public async Task<object> Balances(Guid user)
    {
        var wallets = await db.Wallets.AsNoTracking().Where(x => x.UserId == user).ToListAsync();
        return Kinds.SelectMany(k => Assets.Select(a => wallets.SingleOrDefault(x => x.Kind == k && x.Asset == a) ?? new Wallet { UserId = user, Kind = k, Asset = a }));
    }
    private async Task<Wallet> Wallet(Guid user, string asset, string kind = "Unified Trading") =>
        await db.Wallets.FindAsync(user, kind, asset) ?? throw new RuleException("Wallet does not exist.");
    private void Move(Wallet w, decimal available, decimal locked, string reason, Guid reference)
    {
        if (w.Available + available < 0 || w.Locked + locked < 0) throw new RuleException($"Insufficient available {w.Asset} in {w.Kind}.");
        const decimal maximum = 99999999999999999999.99999999m;
        if (w.Available + available > maximum || w.Locked + locked > maximum)
            throw new RuleException("This operation would exceed the supported balance range.");
        w.Available += available; w.Locked += locked;
        db.Ledger.Add(new LedgerEntry { UserId = w.UserId, Wallet = w.Kind, Asset = w.Asset,
            AvailableDelta = available, LockedDelta = locked, Reason = reason, ReferenceId = reference });
    }
    public async Task<object> Faucet(Guid user)
    {
        var reference = Guid.NewGuid();
        Move(await Wallet(user, "BTC", "Funding"), 100, 0, "Demo credit", reference);
        Move(await Wallet(user, "USDT", "Funding"), 1_000_000, 0, "Demo credit", reference);
        return new { message = "Test funds added to Funding.", reference };
    }
    private static void Amount(decimal value, int places, string name)
    {
        if (value <= 0 || value > 1_000_000_000m || decimal.Round(value, places) != value)
            throw new RuleException($"{name} must be positive, at most 1,000,000,000, with at most {places} decimal places.");
    }
    public async Task<object> Transfer(Guid user, TransferRequest r)
    {
        if (!Assets.Contains(r.Asset) || !Kinds.Contains(r.From) || !Kinds.Contains(r.To) || r.From == r.To)
            throw new RuleException("Choose a supported asset and two different wallets.");
        Amount(r.Amount, 8, "Amount");
        var reference = Guid.NewGuid();
        Move(await Wallet(user, r.Asset, r.From), -r.Amount, 0, "Transfer out", reference);
        Move(await Wallet(user, r.Asset, r.To), r.Amount, 0, "Transfer in", reference);
        return new { reference, message = "Transfer completed." };
    }
    public async Task<object> Place(Guid user, PlaceOrder r)
    {
        if (r.Side is not ("Buy" or "Sell") || r.Type is not ("Limit" or "Market")) throw new RuleException("Choose Buy/Sell and Limit/Market.");
        Amount(r.Quantity, 6, "BTC quantity");
        if (r.Type == "Limit") Amount(r.Price ?? 0, 2, "Limit price");
        if (r.Type == "Market" && r.Price != null) throw new RuleException("Market orders do not accept a limit price.");
        var opposing = await db.Orders.Where(x => x.Remaining > 0 && (x.Status == "Open" || x.Status == "PartiallyFilled") && x.Side != r.Side).ToListAsync();
        opposing = (r.Side == "Buy" ? opposing.OrderBy(x => x.Price).ThenBy(x => x.Sequence) : opposing.OrderByDescending(x => x.Price).ThenBy(x => x.Sequence)).ToList();
        var candidates = opposing.TakeWhile(x => r.Type == "Market" || (r.Side == "Buy" ? x.Price <= r.Price : x.Price >= r.Price)).ToList();
        var executable = new List<(Order Maker, decimal Qty)>();
        var remaining = r.Quantity;
        bool selfTrade = false;
        foreach (var maker in candidates)
        {
            if (remaining == 0) break;
            if (maker.UserId == user) { selfTrade = true; break; }
            var qty = Math.Min(remaining, maker.Remaining);
            executable.Add((maker, qty)); remaining -= qty;
        }
        if (r.Type == "Market" && executable.Count == 0 && !selfTrade) throw new RuleException("No liquidity is available for this market order.");
        var reserve = r.Side == "Sell" ? r.Quantity : r.Type == "Limit" ? r.Quantity * r.Price!.Value : executable.Sum(x => x.Qty * x.Maker.Price!.Value);
        var order = new Order { UserId = user, Side = r.Side, Type = r.Type, Price = r.Price, Quantity = r.Quantity, Remaining = r.Quantity, Reserved = reserve };
        db.Orders.Add(order);
        var funds = await Wallet(user, r.Side == "Buy" ? "USDT" : "BTC");
        Move(funds, -reserve, reserve, "Order reserve", order.Id);
        await db.SaveChangesAsync();
        foreach (var (maker, qty) in executable)
        {
            var price = maker.Price!.Value;
            var buyer = order.Side == "Buy" ? order : maker;
            var seller = order.Side == "Sell" ? order : maker;
            var cost = price * qty;
            var buyerReserved = buyer.Type == "Limit" ? qty * buyer.Price!.Value : cost;
            Move(await Wallet(buyer.UserId, "USDT"), buyerReserved - cost, -buyerReserved, "Trade settlement", order.Id);
            Move(await Wallet(buyer.UserId, "BTC"), qty, 0, "Trade settlement", order.Id);
            Move(await Wallet(seller.UserId, "BTC"), 0, -qty, "Trade settlement", order.Id);
            Move(await Wallet(seller.UserId, "USDT"), cost, 0, "Trade settlement", order.Id);
            buyer.Reserved -= buyerReserved; seller.Reserved -= qty;
            maker.Remaining -= qty; order.Remaining -= qty;
            maker.Status = maker.Remaining == 0 ? "Filled" : "PartiallyFilled";
            db.Trades.Add(new Trade { MakerOrderId = maker.Id, TakerOrderId = order.Id, BuyerId = buyer.UserId, SellerId = seller.UserId,
                Price = price, Quantity = qty, TakerSide = order.Side });
            // EF may reorder batched inserts by primary key. Persist each fill here so
            // the durable trade sequence follows execution order. The outer transaction
            // still commits every fill, balance movement and receipt atomically.
            await db.SaveChangesAsync();
        }
        order.Status = order.Remaining == 0 ? "Filled" : order.Remaining < order.Quantity ? "PartiallyFilled" : "Open";
        if (order.Remaining > 0 && (r.Type == "Market" || selfTrade))
        {
            order.Status = "Cancelled";
            order.CancelReason = selfTrade ? "Self-trade prevention" : "Unfilled market remainder";
            Move(funds, order.Reserved, -order.Reserved, "Order release", order.Id);
            order.Reserved = 0;
        }
        return order;
    }
    public async Task<object> Cancel(Guid user, Guid id)
    {
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user) ?? throw new RuleException("Order not found.");
        if (order.Status is not ("Open" or "PartiallyFilled")) throw new RuleException("This order is no longer open.");
        Move(await Wallet(user, order.Side == "Buy" ? "USDT" : "BTC"), order.Reserved, -order.Reserved, "Order cancellation", order.Id);
        order.Reserved = 0; order.Status = "Cancelled"; order.CancelReason = "User cancellation";
        return order;
    }
}
