using Microsoft.AspNetCore.SignalR;

namespace TerytLoad.Hubs
{
    public class ProgressHub : Hub
    {
        public async Task SendProgress(string operation, int current, int total, string message)
        {
            await Clients.All.SendAsync("ReceiveProgress", operation, current, total, message);
        }
    }
}