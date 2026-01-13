// Copyright (c) 2025-2026 Andrzej Szepczyński. All rights reserved.

using AddressLibrary.Data;
using AddressLibrary.Services.AddressSearch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using System.ComponentModel.DataAnnotations;
using System.Text;
using TerytLoad.Hubs;
using TerytLoad.Pages.VerifyAddresses.Models;
using TerytLoad.Pages.VerifyAddresses.Services;

namespace TerytLoad.Pages
{
    public class VerifyAddressesModel : PageModel
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<ProgressHub> _hubContext;
        private readonly ResultsWriter _resultsWriter;
        private const int MAX_RECORDS_TO_PROCESS = 1000; // LIMIT 1000 rekordów

        public VerifyAddressesModel(
            IServiceScopeFactory scopeFactory,
            IWebHostEnvironment env,
            IHubContext<ProgressHub> hubContext)
        {
            _scopeFactory = scopeFactory;
            _env = env;
            _hubContext = hubContext;
            _resultsWriter = new ResultsWriter();
        }

        [BindProperty]
        [Display(Name = "Ścieżka do pliku źródłowego")]
        public string InputFilePath { get; set; } = "AppData/Address/adresy.txt";

        public string? Message { get; set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostVerifyAsync()
        {
            Console.WriteLine("[VerifyAddresses] ========== ROZPOCZĘCIE WERYFIKACJI ==========");
            Console.WriteLine($"[VerifyAddresses] InputFilePath: {InputFilePath}");

            if (!ModelState.IsValid)
            {
                Console.WriteLine("[VerifyAddresses] ModelState nieprawidłowy!");
                return Page();
            }

            var appDataPath = Path.Combine(_env.ContentRootPath, InputFilePath);
            Console.WriteLine($"[VerifyAddresses] Pełna ścieżka: {appDataPath}");

            if (!System.IO.File.Exists(appDataPath))
            {
                Console.WriteLine($"[VerifyAddresses] ✗ Plik nie istnieje!");
                Message = $"❌ Nie znaleziono pliku: {appDataPath}";
                return Page();
            }

            Console.WriteLine($"[VerifyAddresses] ✓ Plik znaleziony");

            // Uruchom przetwarzanie w tle
            _ = Task.Run(async () => await ProcessVerificationAsync(appDataPath));

            Message = $"🔄 Weryfikacja rozpoczęta w tle. Limit przetwarzania: {MAX_RECORDS_TO_PROCESS:N0} rekordów.";

            return Page();
        }

        private async Task ProcessVerificationAsync(string appDataPath)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AddressDbContext>();

            var totalStartTime = DateTime.Now; // CZAS STARTOWY CAŁEJ OPERACJI

            try
            {
                // ✅ ZMIANA: Przekazuj KATALOG, a nie pełną ścieżkę do pliku
                var outputDirectory = Path.GetDirectoryName(appDataPath)!;

                // Ścieżka do logu diagnostycznego
                var logFilePath = Path.Combine(
                    _env.ContentRootPath,
                    "AppData", "Logs", "SearchLog.txt"
                );

                Console.WriteLine($"[VerifyAddresses] Ścieżka logu: {logFilePath}");
                Console.WriteLine($"[VerifyAddresses] Katalog wynikowy: {outputDirectory}");

                Console.WriteLine($"[SignalR] Wysyłam początkowy komunikat...");
                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", 0, 100,
                    $"🔄 Rozpoczęto przetwarzanie pliku: {Path.GetFileName(appDataPath)}\n" +
                    $"⚠️ Limit: {MAX_RECORDS_TO_PROCESS:N0} rekordów\n\n" +
                    $"📄 Log diagnostyczny będzie zapisany w:\n{logFilePath}");

                var startTime = DateTime.Now;

                // Inicjalizuj AddressSearchService
                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", 0, 100,
                    "⏳ Inicjalizacja serwisu wyszukiwania...");

                var initStartTime = DateTime.Now;
                var searchService = new AddressSearchService(context);
                await searchService.InitializeAsync();
                var initTime = (DateTime.Now - initStartTime).TotalSeconds;

                Console.WriteLine($"[VerifyAddresses] ✓ Zainicjalizowano AddressSearchService w {initTime:F1}s");

                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", 0, 100,
                    $"✓ Serwis wyszukiwania zainicjalowany w {initTime:F1}s");

                // Utwórz serwis weryfikacji Z LOGOWANIEM
                var verificationService = new AddressVerificationService(searchService, logFilePath);

                var results = new List<VerificationResult>();

                // Wczytaj dane z pliku
                var readStartTime = DateTime.Now;
                var lines = await System.IO.File.ReadAllLinesAsync(appDataPath, Encoding.UTF8);
                var readTime = (DateTime.Now - readStartTime).TotalMilliseconds;

                Console.WriteLine($"[VerifyAddresses] ✓ Wczytano plik w {readTime:F0}ms");

                if (lines.Length == 0)
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                        "verify-addresses", 0, 100, "⚠️ Plik jest pusty!");
                    return;
                }

                // Pomiń nagłówek i zastosuj LIMIT
                var dataLines = lines.Skip(1)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Take(MAX_RECORDS_TO_PROCESS) // LIMIT 1000 rekordów
                    .ToList();

                var totalLines = dataLines.Count;
                var totalLinesInFile = lines.Skip(1).Count(l => !string.IsNullOrWhiteSpace(l));

                Console.WriteLine($"[VerifyAddresses] Do przetworzenia: {totalLines:N0} rekordów (z {totalLinesInFile:N0} dostępnych w pliku)");

                var limitInfo = totalLinesInFile > MAX_RECORDS_TO_PROCESS
                    ? $"⚠️ ZASTOSOWANO LIMIT: przetwarzanie {totalLines:N0} z {totalLinesInFile:N0} rekordów"
                    : "";

                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", 0, totalLines,
                    $"📊 Przygotowano {totalLines:N0} rekordów do weryfikacji\n{limitInfo}");

                int processedCount = 0;
                int successCount = 0;
                int warningCount = 0;
                int failureCount = 0;

                const int reportInterval = 100;
                var processingStartTime = DateTime.Now; // CZAS STARTOWY PĘTLI PRZETWARZANIA

                Console.WriteLine($"[VerifyAddresses] ========== ROZPOCZĘCIE PĘTLI PRZETWARZANIA ({DateTime.Now:HH:mm:ss.fff}) ==========");

                foreach (var line in dataLines)
                {
                    processedCount++;

                    // Użyj asynchronicznej metody z nowego serwisu
                    var result = await verificationService.ProcessLineAsync(line);
                    results.Add(result);

                    if (result.Status == "SUKCES")
                        successCount++;
                    else if (result.Status == "OSTRZEŻENIE")
                        warningCount++;
                    else
                        failureCount++;

                    if (processedCount % reportInterval == 0 || processedCount == totalLines)
                    {
                        var elapsed = (DateTime.Now - processingStartTime).TotalSeconds;
                        var speed = elapsed > 0 ? (int)(processedCount / elapsed) : 0;
                        var progressMsg = $"Przetworzono: {processedCount:N0}/{totalLines:N0} ({speed:N0} rek/s)\n" +
                                         $"Sukces: {successCount:N0} | Ostrzeżenia: {warningCount:N0} | Brak: {failureCount:N0}";

                        await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                            "verify-addresses",
                            processedCount,
                            totalLines,
                            progressMsg);
                    }
                }

                var processingTime = (DateTime.Now - processingStartTime).TotalSeconds;
                Console.WriteLine($"[VerifyAddresses] ========== ZAKOŃCZENIE PĘTLI PRZETWARZANIA ({DateTime.Now:HH:mm:ss.fff}) ==========");
                Console.WriteLine($"[VerifyAddresses] ⏱️ Czas przetwarzania pętli: {processingTime:F2}s ({processedCount / processingTime:F0} rek/s)");

                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", totalLines, totalLines,
                    $"💾 Zapisywanie {results.Count:N0} wyników do pliku...");

                var saveStartTime = DateTime.Now;
                // ✅ ZMIANA: Przekazuj katalog zamiast pełnej ścieżki do pliku
                await _resultsWriter.SaveResultsAsync(outputDirectory, results);
                var saveTime = (DateTime.Now - saveStartTime).TotalSeconds;

                Console.WriteLine($"[VerifyAddresses] ✓ Zapisano wyniki w {saveTime:F2}s");

                // ZAPISZ LOG DIAGNOSTYCZNY
                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", totalLines, totalLines,
                    "📝 Zapisywanie logu diagnostycznego...");

                var logStartTime = DateTime.Now;
                await verificationService.SaveDiagnosticLogAsync();
                var logTime = (DateTime.Now - logStartTime).TotalSeconds;

                Console.WriteLine($"[VerifyAddresses] ✓ Zapisano log w {logTime:F2}s");

                var totalTime = (DateTime.Now - totalStartTime).TotalSeconds;

                var summary = $"✅ Zakończono weryfikację!\n\n" +
                             $"📊 Statystyki:\n" +
                             $"   • Przetworzono: {processedCount:N0} rekordów\n" +
                             $"   • Sukces: {successCount:N0}\n" +
                             $"   • Ostrzeżenia: {warningCount:N0}\n" +
                             $"   • Brak: {failureCount:N0}\n\n" +
                             $"⏱️ Czasy wykonania:\n" +
                             $"   • Inicjalizacja: {initTime:F2}s\n" +
                             $"   • Wczytanie pliku: {readTime:F0}ms\n" +
                             $"   • Przetwarzanie pętli: {processingTime:F2}s\n" +
                             $"   • Zapis wyników: {saveTime:F2}s\n" +
                             $"   • Zapis logu: {logTime:F2}s\n" +
                             $"   • CZAS CAŁKOWITY: {totalTime:F2}s\n" +
                             $"   • Prędkość: {processedCount / processingTime:F0} rek/s\n\n" +
                             $"📄 Wyniki zapisano do:\n" +
                             $"   • adresy_ok.txt ({successCount:N0} rekordów)\n" +
                             $"   • adresy_bledy.txt ({failureCount + warningCount:N0} rekordów)\n\n" +
                             $"📝 Log diagnostyczny zapisano do:\n{logFilePath}";

                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", totalLines, totalLines, summary);

                Console.WriteLine($"[VerifyAddresses] ========== ZAKOŃCZONO WERYFIKACJĘ ({DateTime.Now:HH:mm:ss.fff}) ==========");
                Console.WriteLine($"[VerifyAddresses] ⏱️ Czas całkowity: {totalTime:F2}s");
            }
            catch (Exception ex)
            {
                var totalTime = (DateTime.Now - totalStartTime).TotalSeconds;
                Console.WriteLine($"[VerifyAddresses] ✗ BŁĄD po {totalTime:F2}s: {ex.Message}");
                Console.WriteLine($"[VerifyAddresses] StackTrace: {ex.StackTrace}");

                var errorMsg = $"❌ Błąd podczas przetwarzania: {ex.Message}\n⏱️ Czas do błędu: {totalTime:F2}s";
                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", 0, 100, errorMsg);
            }
        }
    }
}