using AddressLibrary;
using AddressLibrary.Services.HierarchyBuilders.KodyPocztoweLoader;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using TerytLoad.Hubs;

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
                Console.WriteLine($"[LoadPostalCodes] ContentRootPath: {appDataPath}");

                var db = new AddressDatabase(connectionString, appDataPath);
                var context = db.GetContext();

                var loader = new KodyPocztoweLoaderService(context, appDataPath); // ZMIENIONO
                
                var logFilePath = loader.LogFilePath;
                Console.WriteLine($"[LoadPostalCodes] Ścieżka logu: {logFilePath}");

                // Wyślij ścieżkę logu do przeglądarki
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "postal-codes", 0, 100, 
                    $"Rozpoczynam ładowanie kodów pocztowych...\n\n📄 Log zapisywany do:\n{logFilePath}");

                // ZMIENIONO: Używamy LoadProgressInfo bezpośrednio (jest w namespace)
                var progress = new Progress<LoadProgressInfo>(async info =>
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveProgress", 
                        "postal-codes", 
                        info.ProcessedCount, 
                        info.TotalCount, 
                        info.CurrentOperation);
                    
                    Console.WriteLine($"[{info.PercentageComplete:F1}%] {info.CurrentOperation}");
                });

                var pnaData = context.Pna.ToList();
                
                if (!pnaData.Any())
                {
                    Message = "Brak danych PNA w bazie. Najpierw załaduj dane z pliku PDF na stronie 'Ładowanie PDF'.";
                    IsProcessing = false;
                    ShowResults = true;
                    return Page();
                }

                Console.WriteLine($"Ładowanie {pnaData.Count} rekordów PNA...");

                await loader.LoadAsync(pnaData, progress);

                Console.WriteLine("Ładowanie zakończone, sprawdzam log...");

                await Task.Delay(1000);

                if (System.IO.File.Exists(logFilePath))
                {
                    Console.WriteLine($"✓ Log istnieje: {logFilePath}");
                    var logContent = await System.IO.File.ReadAllTextAsync(logFilePath);
                    
                    Console.WriteLine($"Długość logu: {logContent.Length} znaków");

                    var summaryStart = logContent.IndexOf("=== Podsumowanie ===");
                    if (summaryStart >= 0)
                    {
                        var summary = logContent.Substring(summaryStart);
                        await _hubContext.Clients.All.SendAsync("ReceiveProgress", "postal-codes", 100, 100, 
                            $"✓ Zakończono ładowanie kodów pocztowych\n\n📄 Pełny log zapisany w:\n{logFilePath}\n\n{summary}");
                        
                        Console.WriteLine("Podsumowanie wysłane przez SignalR");
                    }
                    else
                    {
                        await _hubContext.Clients.All.SendAsync("ReceiveProgress", "postal-codes", 100, 100, 
                            $"✓ Zakończono ładowanie kodów pocztowych\n\n📄 Log zapisany w:\n{logFilePath}");
                        Console.WriteLine("Brak sekcji podsumowania w logu");
                    }

                    Message = $"📄 Log zapisany w: {logFilePath}\n\n{new string('=', 80)}\n\n{logContent}";
                }
                else
                {
                    Console.WriteLine($"✗ Log NIE istnieje: {logFilePath}");
                    
                    var logsDir = Path.GetDirectoryName(logFilePath);
                    Console.WriteLine($"Katalog logów: {logsDir}");
                    Console.WriteLine($"Katalog istnieje: {Directory.Exists(logsDir)}");
                    
                    if (Directory.Exists(logsDir))
                    {
                        Console.WriteLine("Zawartość katalogu Logs:");
                        foreach (var file in Directory.GetFiles(logsDir))
                        {
                            Console.WriteLine($"  - {Path.GetFileName(file)}");
                        }
                    }

                    await _hubContext.Clients.All.SendAsync("ReceiveProgress", "postal-codes", 100, 100, 
                        $"✓ Zakończono ładowanie kodów pocztowych\n\n⚠️ Log nie został zapisany");
                    Message = $"✓ Proces zakończony pomyślnie!\n\nOczekiwana ścieżka logu: {logFilePath}\n(Log nie został znaleziony - sprawdź logi konsoli)";
                }

                IsProcessing = false;
                ShowResults = true;
                
                return Page();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"BŁĄD: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "postal-codes", 0, 100, $"❌ Błąd: {ex.Message}");
                Message = $"❌ Błąd: {ex.Message}\n\nStack trace:\n{ex.StackTrace}";
                IsProcessing = false;
                ShowResults = true;
                return Page();
            }
        }
    }
}