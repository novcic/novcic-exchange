using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Novcic.Core;
namespace Novcic.Api;

public class ApiKeyCrypto(IConfiguration config)
{
    private byte[] Key=>SHA256.HashData(Encoding.UTF8.GetBytes("novcic-api-key-encryption-v1:"+config["JwtKey"]));
    public string Encrypt(string secret)
    {
        var bytes=Encoding.UTF8.GetBytes(secret);var nonce=RandomNumberGenerator.GetBytes(12);var tag=new byte[16];var cipher=new byte[bytes.Length];
        using var aes=new AesGcm(Key,16);aes.Encrypt(nonce,bytes,cipher,tag);
        return Convert.ToBase64String([..nonce,..tag,..cipher]);
    }
    public string Decrypt(string encrypted)
    {
        var bytes=Convert.FromBase64String(encrypted);var plain=new byte[bytes.Length-28];
        using var aes=new AesGcm(Key,16);aes.Decrypt(bytes.AsSpan(0,12),bytes.AsSpan(28),bytes.AsSpan(12,16),plain);
        return Encoding.UTF8.GetString(plain);
    }
}
public class SignedKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder,ExchangeDb db,ApiKeyCrypto crypto)
    :AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if(!Guid.TryParse(Request.Headers["X-Api-Key"],out var id))return AuthenticateResult.Fail("Invalid API key.");
        var key=await db.ApiKeys.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&!x.Revoked&&x.ExpiresAt>DateTime.UtcNow);
        if(key==null)return AuthenticateResult.Fail("Expired or revoked API key.");
        if(!long.TryParse(Request.Headers["X-Api-Timestamp"],out var milliseconds)||milliseconds<DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-30000||milliseconds>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+30000)return AuthenticateResult.Fail("Timestamp outside the 30-second window.");
        var nonce=Request.Headers["X-Api-Nonce"].ToString();
        if(!Guid.TryParse(nonce,out _))return AuthenticateResult.Fail("Nonce must be a UUID.");
        var path=Request.Path.Value??"";
        var permittedPost=path=="/api/orders"||path=="/api/cancel-all"||(path.StartsWith("/api/orders/")&&path.EndsWith("/cancel"));
        if(Request.Method!="GET"&&(Request.Method!="POST"||key.Scope!="Trade"||!permittedPost))return AuthenticateResult.Fail("API key scope does not allow this operation.");
        if(Request.ContentLength>8192)return AuthenticateResult.Fail("Body too large.");
        Request.EnableBuffering();
        using var reader=new StreamReader(Request.Body,Encoding.UTF8,leaveOpen:true);
        var body=await reader.ReadToEndAsync();Request.Body.Position=0;
        if(Encoding.UTF8.GetByteCount(body)>8192)return AuthenticateResult.Fail("Body too large.");
        var payload=$"{Request.Headers["X-Api-Timestamp"]}\n{nonce}\n{Request.Method}\n{Request.Path}{Request.QueryString}\n{Request.Headers["Idempotency-Key"]}\n{body}";
        var expected=HMACSHA256.HashData(Encoding.UTF8.GetBytes(crypto.Decrypt(key.EncryptedSecret)),Encoding.UTF8.GetBytes(payload));
        byte[] supplied;try{supplied=Convert.FromHexString(Request.Headers["X-Api-Signature"].ToString());}catch(FormatException){return AuthenticateResult.Fail("Invalid signature.");}
        if(!CryptographicOperations.FixedTimeEquals(expected,supplied))return AuthenticateResult.Fail("Invalid signature.");
        await db.ApiNonces.Where(x=>x.CreatedAt<DateTime.UtcNow.AddMinutes(-2)).ExecuteDeleteAsync();
        db.ApiNonces.Add(new ApiNonce{KeyId=id,Nonce=nonce,CreatedAt=DateTime.UtcNow});
        try{await db.SaveChangesAsync();}catch(DbUpdateException e)when(e.InnerException is Npgsql.PostgresException{SqlState:"23505"}){return AuthenticateResult.Fail("Nonce already used.");}
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub",key.UserId.ToString()),new Claim("scope",key.Scope)],Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal,Scheme.Name));
    }
}
public static class ApiKeyRoutes
{
    public static void MapApiKeys(this WebApplication app)
    {
        var group=app.MapGroup("/api/keys").RequireAuthorization(new AuthorizeAttribute{AuthenticationSchemes=JwtBearerDefaults.AuthenticationScheme});
        group.MapGet("",async(HttpContext context,ExchangeDb db)=>await db.ApiKeys.AsNoTracking().Where(x=>x.UserId==Guid.Parse(context.User.FindFirstValue("sub")!)).OrderByDescending(x=>x.CreatedAt).Select(x=>new{x.Id,x.Name,x.Scope,x.CreatedAt,x.ExpiresAt,x.Revoked}).ToListAsync());
        group.MapPost("",async(CreateKey request,HttpContext context,ExchangeDb db,ApiKeyCrypto crypto)=>{
            if(string.IsNullOrWhiteSpace(request.Name)||request.Name.Length>60||request.Scope is not("Read" or "Trade"))throw new RuleException("Provide a name up to 60 characters and a Read or Trade scope.");
            var user=Guid.Parse(context.User.FindFirstValue("sub")!);
            await using var tx=await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({user.ToString()}))");
            if(await db.ApiKeys.CountAsync(x=>x.UserId==user&&!x.Revoked&&x.ExpiresAt>DateTime.UtcNow)>=10)throw new RuleException("Revoke an existing key before creating another (maximum 10).");
            var secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var key=new ApiCredential{UserId=user,Name=request.Name.Trim(),Scope=request.Scope,EncryptedSecret=crypto.Encrypt(secret)};
            db.ApiKeys.Add(key);await db.SaveChangesAsync();await tx.CommitAsync();
            return Results.Ok(new{key.Id,secret,key.Scope,key.ExpiresAt});
        });
        group.MapPost("/{id:guid}/revoke",async(Guid id,HttpContext context,ExchangeDb db)=>{
            var user=Guid.Parse(context.User.FindFirstValue("sub")!);
            await db.ApiKeys.Where(x=>x.Id==id&&x.UserId==user).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Revoked,true));
            return Results.Ok(new{message="Key revoked."});
        });
    }
}
public record CreateKey(string Name,string Scope);
