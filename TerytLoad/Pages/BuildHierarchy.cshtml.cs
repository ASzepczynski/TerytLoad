using AddressLibrary;
using AddressLibrary.Services.HierarchyBuilders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using System.Text;
using TerytLoad.Configuration;
using TerytLoad.Hubs;
using TerytLoad.Services;

namespace TerytLoad.Pages
{
    public class BuildHierarchyModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly IHubContext<ProgressHub> _hubContext;

        public BuildHierarchyModel(
            IConfiguration configuration,
            IWebHostEnvironment environment,
            IHubContext<ProgressHub> hubContext)
        {
            _configuration = configuration;
            _environment = environment;
            _hubContext = hubContext;
        }

        public void OnGet()
        {
        }

        public IActionResult OnPostBuild()
        {
            var connectionString = _configuration.GetConnectionString("AddressDatabase")
                ?? DatabaseConfig.DefaultConnectionString;
            var appDataPath = _environment.ContentRootPath;

            _ = Task.Run(async () => await RunBuildAsync(connectionString, appDataPath));

            return Page();
        }

        private async Task RunBuildAsync(string connectionString, string appDataPath)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "build-hierarchy", 0, 10,
                    "Krok 1/2: Ladowanie slownikow ulic...");

                var slownikService = new S³ownikUlicLoaderService();
                await slownikService.LoadAsync(
                    connectionString,
                    appDataPath,
                    new Progress<(string op, int current, int total)>(async p =>
                    {
                        await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                            "build-hierarchy", 0, 10, $"[1/2] {p.op}");
                    })
                );

                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "build-hierarchy", 1, 10,
                    $"Krok 1/2: Slowniki zaladowane.{Environment.NewLine}Krok 2/2: Budowanie struktury hierarchicznej...");

                var database = new AddressDatabase(connectionString, appDataPath);

                var progress = new Progress<BuildProgressInfo>(async info =>
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                        "build-hierarchy",
                        1 + info.CurrentStep,
                        10,
                        $"[2/2] {info.CurrentOperation}");
                });

                await database.BuildHierarchicalStructureAsync(progress);

                var context = database.GetContext();
                var wojCount = context.Wojewodztwa.Count(w => w.Id != -1);
                var powCount = context.Powiaty.Count(p => p.Id != -1);
                var gmCount = context.Gminy.Count(g => g.Id != -1);
                var mjsCount = context.Miasta.Count(m => m.Id != -1);
                var ulCount = context.Ulice.Count(u => u.Id != -1);

                var summary =
                    $"SUKCES_OK Ladowanie hierarchii zakonczone:{Environment.NewLine}{Environment.NewLine}" +
                    $"Wojewodztw: {wojCount}{Environment.NewLine}" +
                    $"Powiatow: {powCount}{Environment.NewLine}" +
                    $"Gmin: {gmCount}{Environment.NewLine}" +
                    $"Miejscowosci: {mjsCount}{Environment.NewLine}" +
                    $"Ulic: {ulCount}{Environment.NewLine}{Environment.NewLine}" +
                    $"Aby zaladowac kody pocztowe, przejdz do strony 'Kody pocztowe'";

                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "build-hierarchy", 10, 10, summary);
            }
            catch (Exception ex)
            {
                var messageBuilder = new StringBuilder();
                messageBuilder.AppendLine("BLAD:");
                messageBuilder.AppendLine(ex.Message);
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("Stack trace:");
                messageBuilder.AppendLine(ex.StackTrace);

                if (ex.InnerException != null)
                {
                    messageBuilder.AppendLine();
                    messageBuilder.AppendLine("=== INNER EXCEPTION ===");
                    messageBuilder.AppendLine(ex.InnerException.Message);

                    if (ex.InnerException.InnerException != null)
                    {
                        messageBuilder.AppendLine();
                        messageBuilder.AppendLine("=== INNER INNER EXCEPTION ===");
                        messageBuilder.AppendLine(ex.InnerException.InnerException.Message);
                    }
                }

                var errorMessage = messageBuilder.ToString();
                Console.WriteLine($"[BuildHierarchy ERROR] {errorMessage}");
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "build-hierarchy", 0, 10,
                    $"BLAD_ERR {errorMessage}");
            }
        }
    }
}
