using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Novcic.Core;

namespace Novcic.Api;
public record Registration(string Email, string Password, string Country);
public record Login(string Email, string Password);
public class AuthService(ExchangeDb db, IPasswordHasher<User> hasher, IConfiguration config)
{
    private static readonly HashSet<string> Countries = ("AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO JP KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS YE YT ZA ZM ZW").Split(' ').ToHashSet();
    private static string Normalize(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new RuleException("Enter a valid email address.");
        email = email.Trim().ToLowerInvariant();
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || !email.Contains('.'))
            throw new RuleException("Enter a valid email address.");
        return email;
    }
    public async Task<IResult> Register(Registration r, HttpContext ctx)
    {
        var email = Normalize(r.Email);
        if (string.IsNullOrEmpty(r.Password) || r.Password.Length < 10 || r.Password.Length > 128 || !r.Password.Any(char.IsLetter) || !r.Password.Any(char.IsDigit))
            throw new RuleException("Use 10–128 characters including a letter and a number.");
        if (!Countries.Contains(r.Country)) throw new RuleException("Select a valid country of residence.");
        var user = new User { Email = email, Country = r.Country };
        user.PasswordHash = hasher.HashPassword(user, r.Password); db.Users.Add(user);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { throw new RuleException("An account with this email already exists."); }
        return await Issue(user, ctx);
    }
    public async Task<IResult> Login(Login r, HttpContext ctx)
    {
        var email = Normalize(r.Email);
        if (string.IsNullOrEmpty(r.Password) || r.Password.Length > 128) return Results.Json(new { error = "Invalid email or password." }, statusCode: 401);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
        var subject = user ?? new User();
        var hash = user?.PasswordHash ?? hasher.HashPassword(subject, "Dummy-password-12345");
        if (hasher.VerifyHashedPassword(subject, hash, r.Password) == PasswordVerificationResult.Failed || user == null)
            return Results.Json(new { error = "Invalid email or password." }, statusCode: 401);
        return await Issue(user, ctx);
    }
    public async Task<IResult> Refresh(HttpContext ctx)
    {
        var token = ctx.Request.Cookies["novcic_refresh"];
        if (string.IsNullOrEmpty(token)) return Results.Unauthorized();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74001235)");
        var hash = Hash(token);
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (session == null || session.ExpiresAt <= DateTime.UtcNow || session.Revoked)
        { Clear(ctx); return Results.Unauthorized(); }
        if (session.UsedAt != null)
        {
            await db.Sessions.Where(x => x.FamilyId == session.FamilyId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true));
            await tx.CommitAsync(); Clear(ctx); return Results.Unauthorized();
        }
        session.UsedAt = DateTime.UtcNow;
        var user = await db.Users.FindAsync(session.UserId);
        if (user == null) return Results.Unauthorized();
        var result = await Issue(user, ctx, session.FamilyId);
        await tx.CommitAsync(); return result;
    }
    public async Task<IResult> Logout(HttpContext ctx)
    {
        if (ctx.Request.Cookies["novcic_refresh"] is string token)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74001235)");
            var hash = Hash(token);
            var session = await db.Sessions.SingleOrDefaultAsync(x => x.TokenHash == hash);
            if (session != null) await db.Sessions.Where(x => x.FamilyId == session.FamilyId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true));
            await tx.CommitAsync();
        }
        Clear(ctx); return Results.Ok(new { message = "Signed out." });
    }
    private CookieOptions Cookie() => new() { HttpOnly = true, Secure = config.GetValue<bool>("CookieSecure"), SameSite = SameSiteMode.Strict, Path = "/api/auth", MaxAge = TimeSpan.FromDays(7) };
    private void Clear(HttpContext ctx) => ctx.Response.Cookies.Delete("novcic_refresh", Cookie());
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private async Task<IResult> Issue(User user, HttpContext ctx, Guid? family = null)
    {
        var refresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
        db.Sessions.Add(new Session { UserId = user.Id, TokenHash = Hash(refresh), FamilyId = family ?? Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddDays(7) });
        await db.SaveChangesAsync();
        ctx.Response.Cookies.Append("novcic_refresh", refresh, Cookie());
        ctx.Response.Headers.CacheControl = "no-store";
        var expires = DateTime.UtcNow.AddMinutes(10);
        var token = new JwtSecurityToken("novcic", "novcic-web", [new Claim("sub", user.Id.ToString()), new Claim("jti", Guid.NewGuid().ToString())],
            expires: expires, signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JwtKey"]!)), SecurityAlgorithms.HmacSha256));
        return Results.Ok(new { accessToken = new JwtSecurityTokenHandler().WriteToken(token), expiresAt = expires, user = new { user.Id, user.Email, user.Country } });
    }
}
