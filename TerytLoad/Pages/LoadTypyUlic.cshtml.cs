using AddressLibrary.Data;
using AddressLibrary;
using AddressLibrary.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using TerytLoad.Hubs;

namespace TerytLoad.Pages
{
    public class LoadTypyUlicModel : PageModel
    {
        private readonly IHubContext<ProgressHub> _hubContext;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public string? Message { get; set; }
        public bool ShowResults { get; set; }

        public LoadTypyUlicModel(
            IHubContext<ProgressHub> hubContext,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            _hubContext = hubContext;
            _configuration = configuration;
            _environment = environment;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? throw new InvalidOperationException("Connection string 'AddressDatabase' not found.");

                var appDataPath = _environment.ContentRootPath;

                var db = new AddressDatabase(connectionString, appDataPath);
                var context = db.GetContext();

                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "load-typy-ulic", 0, 100,
                    "Rozpoczynam ³adowanie TypyUlic...");

                using var loader = new LoadTypyUlicService(context, appDataPath);

                var progress = new Progress<ValidatorProgress>(async p =>
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                        "load-typy-ulic",
                        p.ProcessedCount,
                        p.TotalCount > 0 ? p.TotalCount : 100,
                        p.CurrentOperation);
                });

                var result = await loader.LoadAsync(progress);

                var logFilePath = Path.Combine(appDataPath, "AppData", "Logs", "LoadTypyUlic.txt");
                var coverage = result.TotalCount > 0 ? (result.FoundCount * 100.0 / result.TotalCount) : 0;
                var summary = $"? Zakoñczono ³adowanie TypyUlic{Environment.NewLine}{Environment.NewLine}" +
                              $"Przetworzonych: {result.TotalCount}{Environment.NewLine}" +
                              $"Znaleziono w s³owniku: {result.FoundCount}{Environment.NewLine}" +
                              $"Brak w s³owniku: {result.NotFoundCount}{Environment.NewLine}" +
                              $"Pokrycie: {coverage:F2}%{Environment.NewLine}{Environment.NewLine}" +
                              $"?? Log: {logFilePath}";

                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "load-typy-ulic",
                    result.TotalCount,
                    result.TotalCount > 0 ? result.TotalCount : 100,
                    summary);

                ShowResults = true;
                Message = summary;

                return Page();
            }
            catch (Exception ex)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "load-typy-ulic", 0, 100,
                    $"? B³¹d: {ex.Message}");

                ModelState.AddModelError(string.Empty, $"B³¹d: {ex.Message}");
                return Page();
            }
        }
    }
}