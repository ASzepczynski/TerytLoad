using AddressLibrary;
using AddressLibrary;
using AddressLibrary.Models;
using AddressLibrary.Services;
using AddressLibrary.Services.KodyPocztoweLoader;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TerytLoad.Hubs;
using AddressLibrary.Helpers;

namespace TerytLoad.Pages
{
    public class LoadPostalCodesModel : PageModel
    {
        private readonly IHubContext<ProgressHub> _hubContext;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public LoadPostalCodesModel(
            IHubContext<ProgressHub> hubContext,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            _hubContext = hubContext;
            _configuration = configuration;
            _environment = environment;
        }

        public string? Message { get; set; }
        public bool IsProcessing { get; set; }
        public bool ShowResults { get; set; }

        public void OnGet()
        {
            ShowResults = false;
        }

        public async Task<IActionResult> OnPostLoadAsync()
        {
            try
            {
                IsProcessing = true;
                ShowResults = false;

                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? throw new InvalidOperationException("Connection string 'AddressDatabase' not found.");

                var appDataPath = _environment.ContentRootPath;
                var db = new AddressDatabase(connectionString, appDataPath);
                var context = db.GetContext();

                // ── KROK 1: Załaduj spispna-cz1.txt do tabeli Pna ─────────────────────
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "postal-codes", 0, 100,
                    "⏳ KROK 1/2: Ładowanie pliku spispna-cz1.txt do tabeli Pna...");

                var csvFile = Path.Combine(appDataPath, "AppData", "pna", "spispna-cz1.txt");

                if (!System.IO.File.Exists(csvFile))
                {
                    Message = $"❌ Nie znaleziono pliku: {csvFile}";
                    IsProcessing = false;
                    ShowResults = true;
                    return Page();
                }

                await context.Database.ExecuteSqlRawAsync("DELETE FROM Pna");

                var csvLoader = new PnaCsvLoader(context, appDataPath);
                var csvProgress = new Progress<PnaCsvLoader.LoadProgressInfo>(info =>
                {
                    _ = _hubContext.Clients.All.SendAsync("ReceiveProgress", "postal-codes", 0, 100,
                        $"  {info.CurrentAction}");
                });
                await csvLoader.LoadFromCsvAsync(csvFile, csvProgress);

                var totalCsvRows = context.Pna.Count();
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "postal-codes", 0, 100,
                    $"✓ KROK 1/2 zakończony — załadowano {totalCsvRows} rekordów do tabeli Pna");

                // ── KROK 2: Przetwórz Pna → KodyPocztowe ──────────────────────────────
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "postal-codes", 0, 100,
                    $"⏳ KROK 2/2: Przetwarzanie PNA → KodyPocztowe...");

                var logFilePath = string.Empty;

                using (var loader = new KodyPocztoweLoaderService(context, appDataPath))
                {
                    logFilePath = loader.LogFilePath;

                    var progress = new Progress<LoadProgressInfo>(async info =>
                    {
                        await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                            "postal-codes",
                            info.ProcessedCount,
                            info.TotalCount,
                            info.CurrentOperation);
                    });

                    var pnaData = context.Pna.ToList();
                    await loader.LoadAsync(pnaData, progress);
                }

                await Task.Delay(500);

                var logContent = System.IO.File.Exists(logFilePath)
                    ? await System.IO.File.ReadAllTextAsync(logFilePath)
                    : string.Empty;

                var summaryStart = logContent.IndexOf("=== Podsumowanie ===");
                var summary = summaryStart >= 0 ? logContent.Substring(summaryStart) : string.Empty;

                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "postal-codes", 100, 100,
                    $"✅ Zakończono{Environment.NewLine}{Environment.NewLine}" +
                    $"Krok 1: {totalCsvRows} rekordów z pliku spispna-cz1.txt{Environment.NewLine}" +
                    $"Krok 2: przetwarzanie PNA → KodyPocztowe{Environment.NewLine}{Environment.NewLine}" +
                    (summary.Length > 0 ? summary : $"📄 Log: {logFilePath}"));

                Message = $"✅ Zakończono{Environment.NewLine}" +
                          $"CSV: {totalCsvRows} rekordów, Log: {logFilePath}{Environment.NewLine}" +
                          $"{new string('=', 60)}{Environment.NewLine}{logContent}";

                IsProcessing = false;
                ShowResults = true;
                return Page();
            }
            catch (Exception ex)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "postal-codes", 0, 100,
                    $"❌ Błąd: {ex.Message}");
                Message = $"❌ Błąd: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
                IsProcessing = false;
                ShowResults = true;
                return Page();
            }
        }
    }
}