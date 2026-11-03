using Microsoft.AspNetCore.SignalR;
namespace Novcic.Api;
// Only revision notifications are public. Private data is fetched using each client's JWT.
public class MarketHub : Hub { }
public class MarketRelay(IHttpClientFactory factory, IHubContext<MarketHub> hub, ILogger<MarketRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        string? previous = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stop))
        {
            try
            {
                using var response = await factory.CreateClient("matching").GetAsync("/revision", stop);
                response.EnsureSuccessStatusCode();
                var revision = await response.Content.ReadAsStringAsync(stop);
                if (previous != revision) { previous = revision; await hub.Clients.All.SendAsync("MarketChanged", System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(revision), stop); }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            { logger.LogDebug(ex, "Waiting for matching service."); }
        }
    }
}
