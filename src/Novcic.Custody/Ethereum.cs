using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Nethereum.Util;

namespace Novcic.Custody;
public class Ethereum(IConfiguration config, IHttpClientFactory clients)
{
    public const string Abi = "[{\"type\":\"function\",\"name\":\"balanceOf\",\"stateMutability\":\"view\",\"inputs\":[{\"name\":\"owner\",\"type\":\"address\"}],\"outputs\":[{\"type\":\"uint256\"}]},{\"type\":\"function\",\"name\":\"transfer\",\"inputs\":[{\"name\":\"to\",\"type\":\"address\"},{\"name\":\"amount\",\"type\":\"uint256\"}],\"outputs\":[{\"type\":\"bool\"}]}]";
    public string Generation => config["ChainGeneration"]!;
    public string TreasuryKey => config["TreasuryKey"]!;
    public string Treasury => new Account(TreasuryKey, 31337).Address.ToLowerInvariant();
    public string Token(string asset) => config[asset] ?? throw new InvalidOperationException("Token contract missing.");
    public int Decimals(string asset) => asset == "BTC" ? 8 : 6;
    public Web3 Web(string? key = null) => key == null ? new Web3(config["RpcUrl"]) : new Web3(new Account(key, 31337), config["RpcUrl"]);
    public static string Hex(long value) => "0x" + value.ToString("x");
    public static long Long(string hex) => long.Parse(hex[2..], NumberStyles.HexNumber);
    public static BigInteger Integer(string hex) => BigInteger.Parse("0" + hex[2..], NumberStyles.HexNumber);
    public async Task<JsonElement> Rpc(string method, params object[] args)
    {
        using var response = await clients.CreateClient().PostAsJsonAsync(config["RpcUrl"], new { jsonrpc = "2.0", id = 1, method, @params = args });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (json.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.ToString());
        return json.GetProperty("result").Clone();
    }
    public async Task<BigInteger> Balance(string asset, string address) =>
        await Web().Eth.GetContract(Abi, Token(asset)).GetFunction("balanceOf").CallAsync<BigInteger>(address);
    public async Task<(string Raw, string Hash)> Sign(string key, string to, string? asset, BigInteger units, long reservedNonce)
    {
        var web = Web(key); var sender = new Account(key, 31337).Address;
        var gasPrice = await web.Eth.GasPrice.SendRequestAsync();
        var input = new TransactionInput {
            From = sender, To = asset == null ? to : Token(asset), Nonce = new HexBigInteger(reservedNonce),
            Gas = new HexBigInteger(asset == null ? 21000 : 100000), GasPrice = gasPrice,
            Value = new HexBigInteger(asset == null ? units : 0),
            Data = asset == null ? "0x" : web.Eth.GetContract(Abi, Token(asset)).GetFunction("transfer").GetData(to, units)
        };
        var raw = await web.TransactionManager.SignTransactionAsync(input);
        return (raw, "0x" + new Sha3Keccack().CalculateHashFromHex(raw));
    }
    public string Encrypt(string key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12); var bytes = Encoding.UTF8.GetBytes(key); var cipher = new byte[bytes.Length]; var tag = new byte[16];
        using var aes = new AesGcm(Convert.FromBase64String(config["CustodyKey"]!), 16); aes.Encrypt(nonce, bytes, cipher, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }
    public string Decrypt(string text)
    {
        var bytes = Convert.FromBase64String(text); var plain = new byte[bytes.Length - 28];
        using var aes = new AesGcm(Convert.FromBase64String(config["CustodyKey"]!), 16); aes.Decrypt(bytes.AsSpan(0,12), bytes.AsSpan(28), bytes.AsSpan(12,16), plain);
        return Encoding.UTF8.GetString(plain);
    }
}
