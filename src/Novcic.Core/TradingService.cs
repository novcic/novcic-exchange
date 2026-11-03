using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Novcic.Core;

public partial class TradingService(ExchangeDb db, AmmSettings? settings = null)
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
        var collateralTopUp = body is TransferRequest { To: "Unified Trading" };
        if (!collateralTopUp && operation is "place" or "transfer" or "settle-cash" or "supply" or "borrow" or "repay" or "add-collateral" or "add-liquidity" or "swap") {
            if(body is not PlaceOrder { ReduceOnly: true }) await CheckRisk(userId);
        }
        await AdvanceRevision();
        await tx.CommitAsync();
        return response;
    }
    public Task<int> AdvanceRevision()=>db.Database.ExecuteSqlRawAsync("UPDATE exchange.\"Revision\" SET \"Revision\"=\"Revision\"+1 WHERE \"Id\"=1");
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
        if (decimal.Round(available, 8) != available || decimal.Round(locked, 8) != locked)
            throw new InvalidOperationException("Accounting movements must be quantized before posting.");
        if (w.Available + available < 0 || w.Locked + locked < 0) throw new RuleException($"Insufficient available {w.Asset} in {w.Kind}.");
        const decimal maximum = 99999999999999999999.99999999m;
        if (w.Available + available > maximum || w.Locked + locked > maximum)
            throw new RuleException("This operation would exceed the supported balance range.");
        w.Available += available; w.Locked += locked;
        foreach (var (account, delta) in new[] {
            ($"user:{w.UserId}:{w.Kind}:available", available),
            ($"user:{w.UserId}:{w.Kind}:reserved", locked),
            ("system:clearing", -(available + locked)) })
            if (delta != 0) db.Journal.Add(new JournalLine { ReferenceId = reference, Account = account, Asset = w.Asset, Delta = delta });
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
    public async Task<object> CreditDeposit(Guid user, Guid id)
    {
        var deposit = await db.ChainTransfers.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user && x.Kind == "Deposit")
            ?? throw new RuleException("Deposit not found.");
        if (deposit.Status == "Credited") return deposit;
        if (deposit.Status != "Confirmed") throw new RuleException("Deposit is not confirmed.");
        Move(await Wallet(user, deposit.Asset, "Funding"), deposit.Amount, 0, "On-chain deposit", deposit.Id);
        deposit.Status = "Credited";
        return deposit;
    }
    public async Task<object> Withdraw(Guid user, WithdrawalRequest request, string generation)
    {
        if (!Assets.Contains(request.Asset)) throw new RuleException("Unsupported asset.");
        Amount(request.Amount, request.Asset == "BTC" ? 8 : 6, "Withdrawal amount");
        if (request.Address == null || !System.Text.RegularExpressions.Regex.IsMatch(request.Address, "^0x[0-9a-fA-F]{40}$") || request.Address.ToLowerInvariant() == "0x0000000000000000000000000000000000000000")
            throw new RuleException("Enter a valid nonzero Ethereum address.");
        var withdrawal = new ChainTransfer { UserId = user, Kind = "Withdrawal", Asset = request.Asset, Amount = request.Amount,
            Address = request.Address.ToLowerInvariant(), Generation = generation, Status = "Requested" };
        Move(await Wallet(user, request.Asset, "Funding"), -request.Amount, request.Amount, "Withdrawal reserve", withdrawal.Id);
        db.ChainTransfers.Add(withdrawal);
        return withdrawal;
    }
    public async Task<object> FinishWithdrawal(Guid user, Guid id, bool success)
    {
        var item = await db.ChainTransfers.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user && x.Kind == "Withdrawal") ?? throw new RuleException("Withdrawal not found.");
        if (item.Status is "Completed" or "Failed") return item;
        if (item.Status != "Confirmed" && item.Status != "Reverted") throw new RuleException("Withdrawal has no definitive receipt.");
        if (success != (item.Status == "Confirmed")) throw new RuleException("Receipt outcome does not match.");
        Move(await Wallet(user, item.Asset, "Funding"), success ? 0 : item.Amount, -item.Amount, success ? "On-chain withdrawal" : "Withdrawal release", id);
        item.Status = success ? "Completed" : "Failed";
        return item;
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
        if (r.Instrument != "BTCUSDT") return await PlaceDerivative(user, r);
        if (r.ReduceOnly || r.Leverage is not (null or 1)) throw new RuleException("Spot orders do not accept derivative leverage or reduce-only flags.");
        if (r.Side is not ("Buy" or "Sell") || r.Type is not ("Limit" or "Market")) throw new RuleException("Choose Buy/Sell and Limit/Market.");
        Amount(r.Quantity, 6, "BTC quantity");
        if (r.Type == "Limit") Amount(r.Price ?? 0, 2, "Limit price");
        if (r.Type == "Market" && r.Price != null) throw new RuleException("Market orders do not accept a limit price.");
        var opposing = await db.Orders.Where(x => x.InstrumentId == "BTCUSDT" && x.Remaining > 0 && (x.Status == "Open" || x.Status == "PartiallyFilled") && x.Side != r.Side).ToListAsync();
        opposing = (r.Side == "Buy" ? opposing.OrderBy(x => x.Price).ThenBy(x => x.Sequence) : opposing.OrderByDescending(x => x.Price).ThenBy(x => x.Sequence)).ToList();
        var candidates = opposing.TakeWhile(x => r.Type == "Market" || (r.Side == "Buy" ? x.Price <= r.Price : x.Price >= r.Price)).ToList();
        var strategyAccounts=(await db.LiquidityOperations.Where(x=>x.Status=="Pending").Select(x=>x.Id).ToListAsync()).ToHashSet();strategyAccounts.Add(AmmAccount);
        var executable = new List<(Order Maker, decimal Qty)>();
        var remaining = r.Quantity;
        bool selfTrade = false;
        foreach (var maker in candidates)
        {
            if (remaining == 0) break;
            if(strategyAccounts.Contains(maker.UserId)&&(maker.QuoteValidatedAt??maker.CreatedAt)<DateTime.UtcNow.AddSeconds(-3))continue;
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
            var tradeId=Guid.NewGuid();
            var buyerFee=await ChargeExecutionFee(buyer.UserId,seller.UserId,new Instrument(),"Buy",qty,price,tradeId);
            var sellerFee=await ChargeExecutionFee(seller.UserId,buyer.UserId,new Instrument(),"Sell",qty,price,tradeId);
            buyer.FilledNotional+=cost;seller.FilledNotional+=cost;
            buyer.Reserved -= buyerReserved; seller.Reserved -= qty;
            maker.Remaining -= qty; order.Remaining -= qty;
            maker.Status = maker.Remaining == 0 ? "Filled" : "PartiallyFilled";
            db.Trades.Add(new Trade { Id=tradeId, BuyerFee=buyerFee, SellerFee=sellerFee, BuyerFeeAsset="BTC", SellerFeeAsset="USDT", MakerOrderId = maker.Id, TakerOrderId = order.Id, BuyerId = buyer.UserId, SellerId = seller.UserId,
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
        if(order.InstrumentId == "BTCUSDT") Move(await Wallet(user, order.Side == "Buy" ? "USDT" : "BTC"), order.Reserved, -order.Reserved, "Order cancellation", order.Id);
        order.Reserved = 0; order.Status = "Cancelled"; order.CancelReason = "User cancellation";
        return order;
    }
    public async Task<object> CancelAll(Guid user)
    {
        var ids=await db.Orders.Where(x=>x.UserId==user&&(x.Status=="Open"||x.Status=="PartiallyFilled")).Select(x=>x.Id).ToListAsync();
        foreach(var id in ids)await Cancel(user,id);
        return new {cancelled=ids.Count};
    }
}
