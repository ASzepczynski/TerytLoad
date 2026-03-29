using AddressLibrary;
using AddressLibrary.Services.HierarchyBuilders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using System.Text;
using TerytLoad.Configuration;
using TerytLoad.Hubs;

namespace TerytLoad.Pages
{
    public class BuildHierarchyModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly IHubContext<ProgressHub> _hubContext;

        [BindProperty]
        public string Message { get; set; } = string.Empty;

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

        public async Task<IActionResult> OnPostBuildAsync()
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? DatabaseConfig.DefaultConnectionString;

                var appDataPath = _environment.ContentRootPath;
                var database = new AddressDatabase(connectionString, appDataPath);

                var messageBuilder = new StringBuilder();

                // Wyślij wiadomość startową
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "build-hierarchy", 0, 9,
                    $"⏳ Rozpoczynam budowanie struktury hierarchicznej...{Environment.NewLine}{Environment.NewLine}" +
                    $"ℹ️ Kody pocztowe NIE są ładowane w tym kroku.{Environment.NewLine}" +
                    $"   Użyj osobnej strony 'Kody pocztowe' aby je załadować.");

                // Raportowanie postępu przez SignalR
                var progress = new Progress<BuildProgressInfo>(async info =>
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                        "build-hierarchy",
                        info.CurrentStep,
                        info.TotalSteps,
                        info.CurrentOperation);

                    Console.WriteLine($"[{info.PercentageComplete:F1}%] {info.CurrentOperation}");
                });

                // Buduj strukturę hierarchiczną BEZ kodów pocztowych
                await database.BuildHierarchicalStructureAsync(progress);

                var context = database.GetContext();
                var wojCount = context.Wojewodztwa.Count(w => w.Id != -1);
                var powCount = context.Powiaty.Count(p => p.Id != -1);
                var gmCount = context.Gminy.Count(g => g.Id != -1);
                var mjsCount = context.Miasta.Count(m => m.Id != -1);
                var ulCount = context.Ulice.Count(u => u.Id != -1);

                var summary = $"✅ SUKCES! Utworzono strukturę hierarchiczną:{Environment.NewLine}{Environment.NewLine}" +
                             $"✓ Województw: {wojCount}{Environment.NewLine}" +
                             $"✓ Powiatów: {powCount}{Environment.NewLine}" +
                             $"✓ Gmin: {gmCount}{Environment.NewLine}" +
                             $"✓ Miejscowości: {mjsCount}{Environment.NewLine}" +
                             $"✓ Ulic: {ulCount}{Environment.NewLine}{Environment.NewLine}" +
                             $"⚠️ Aby załadować kody pocztowe, przejdź do strony 'Kody pocztowe'";

                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "build-hierarchy", 9, 9, summary);

                messageBuilder.AppendLine(summary);
                Message = messageBuilder.ToString();
            }
            catch (Exception ex)
            {
                var messageBuilder = new StringBuilder();
                messageBuilder.AppendLine("❌ BŁĄD:");
                messageBuilder.AppendLine(ex.Message);
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("Stack trace:");
                messageBuilder.AppendLine(ex.StackTrace);

                if (ex.InnerException != null)
                {
                    messageBuilder.AppendLine();
                    messageBuilder.AppendLine("=== INNER EXCEPTION ===");
                    messageBuilder.AppendLine(ex.InnerException.Message);
                    messageBuilder.AppendLine();
                    messageBuilder.AppendLine("Inner Stack trace:");
                    messageBuilder.AppendLine(ex.InnerException.StackTrace);

                    if (ex.InnerException.InnerException != null)
                    {
                        messageBuilder.AppendLine();
                        messageBuilder.AppendLine("=== INNER INNER EXCEPTION ===");
                        messageBuilder.AppendLine(ex.InnerException.InnerException.Message);
                    }
                }

                var errorMessage = messageBuilder.ToString();
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "build-hierarchy", 0, 9, $"❌ {errorMessage}");
                Message = errorMessage;
            }

            return Page();
        }
    }
}