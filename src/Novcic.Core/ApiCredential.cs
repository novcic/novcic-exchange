namespace Novcic.Core;
public class ApiCredential
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; }="";
    public string Scope { get; set; }="Read";
    public string[] Products { get; set; } = ["Spot", "Perpetual", "Future", "Option"];
    public string EncryptedSecret { get; set; }="";
    public DateTime CreatedAt { get; set; }=DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }=DateTime.UtcNow.AddDays(90);
    public bool Revoked { get; set; }
}
public class ApiNonce
{
    public Guid KeyId { get; set; }
    public string Nonce { get; set; }="";
    public DateTime CreatedAt { get; set; }
}
