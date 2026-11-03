using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Novcic.Core;
namespace Novcic.Api;
public record ProfileUpdate(string Username, string Avatar, DateOnly? DateOfBirth);
public record PasswordChange(string CurrentPassword, string NewPassword);
public record EmailChange(string CurrentPassword, string NewEmail);
public partial class AuthService
{
    public static readonly string[] Avatars=["default","fern","sunrise","river","mountain","moon","oak","meadow","fox"];
    public static object Summary(User user)=>new{user.Id,user.Email,user.Country,user.Username,user.Avatar};
    private static void AssignUsername(User user){user.Username="User"+RandomNumberGenerator.GetInt32(10000000,100000000);user.NormalizedUsername=user.Username.ToUpperInvariant();}
    public async Task<object> Profile(Guid id){var u=await db.Users.FindAsync(id)??throw new RuleException("Account not found.");return new{u.Id,u.Email,u.Country,u.Username,u.Avatar,u.DateOfBirth};}
    public async Task<object> UpdateProfile(Guid id,ProfileUpdate r)
    {
        var name=r.Username?.Trim()??"";
        if(!Regex.IsMatch(name,"^[A-Za-z0-9_]{3,24}$"))throw new RuleException("Username must contain 3-24 letters, digits or underscores.");
        if(!Avatars.Contains(r.Avatar))throw new RuleException("Select an available avatar.");
        if(r.DateOfBirth>DateOnly.FromDateTime(DateTime.UtcNow))throw new RuleException("Date of birth cannot be in the future.");
        var u=await db.Users.FindAsync(id)??throw new RuleException("Account not found.");
        u.Username=name;u.NormalizedUsername=name.ToUpperInvariant();u.Avatar=r.Avatar;u.DateOfBirth=r.DateOfBirth;
        try{await db.SaveChangesAsync();}catch(DbUpdateException e) when(e.InnerException is Npgsql.PostgresException {SqlState:"23505"}){throw new RuleException("This username is already taken.");}
        return await Profile(id);
    }
    public async Task<object> ChangeSecurity(Guid id,string password,string? newPassword,string? email,HttpContext ctx)
    {
        await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74001235)");
        var u=await db.Users.FindAsync(id)??throw new RuleException("Account not found.");
        if(ctx.User.FindFirstValue("sv")!=u.SecurityVersion.ToString())throw new RuleException("Session revoked. Sign in again.");
        if(string.IsNullOrEmpty(password)||password.Length>128||hasher.VerifyHashedPassword(u,u.PasswordHash,password)==PasswordVerificationResult.Failed)throw new RuleException("Current password is incorrect.");
        if(newPassword!=null){
            if(newPassword.Length<10||newPassword.Length>128||!newPassword.Any(char.IsLetter)||!newPassword.Any(char.IsDigit))throw new RuleException("Use 10-128 characters including a letter and a number.");
            u.PasswordHash=hasher.HashPassword(u,newPassword);
        }else{var normalized=Normalize(email??"");if(normalized==u.Email)throw new RuleException("Enter a different email address.");u.Email=normalized;}
        u.SecurityVersion++;
        await db.Sessions.Where(x=>x.UserId==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Revoked,true));
        await db.ApiKeys.Where(x=>x.UserId==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Revoked,true));
        try{await db.SaveChangesAsync();}catch(DbUpdateException e) when(e.InnerException is Npgsql.PostgresException {SqlState:"23505"}){throw new RuleException("This email is already in use.");}
        await tx.CommitAsync();Clear(ctx);return new{message="Account security updated. Sign in again. All API keys and sessions were revoked."};
    }
}
public static class ProfileRoutes
{
    public static void MapProfiles(this WebApplication app)
    {
        var g=app.MapGroup("/api/profile").RequireAuthorization(new AuthorizeAttribute{AuthenticationSchemes=JwtBearerDefaults.AuthenticationScheme}).RequireRateLimiting("auth");
        static Guid Id(HttpContext c)=>Guid.Parse(c.User.FindFirstValue("sub")!);
        g.MapGet("",(HttpContext c,AuthService s)=>s.Profile(Id(c)));
        g.MapPost("",(ProfileUpdate r,HttpContext c,AuthService s)=>s.UpdateProfile(Id(c),r));
        g.MapPost("/password",(PasswordChange r,HttpContext c,AuthService s)=>s.ChangeSecurity(Id(c),r.CurrentPassword,r.NewPassword,null,c));
        g.MapPost("/email",(EmailChange r,HttpContext c,AuthService s)=>s.ChangeSecurity(Id(c),r.CurrentPassword,null,r.NewEmail,c));
    }
}
