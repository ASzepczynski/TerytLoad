// Copyright (c) 2025-2026 Andrzej Szepczyński. All rights reserved.

using AddressLibrary.Data;
using AddressLibrary.Services.AddressSearch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using System.ComponentModel.DataAnnotations;
using System.Text;
using TerytLoad.Hubs;

namespace TerytLoad.Pages
{
    public class VerifyAddressesModel : PageModel
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<ProgressHub> _hubContext;
        private const int MAX_RECORDS_TO_PROCESS = 1000000;

        public VerifyAddressesModel(
            IServiceScopeFactory scopeFactory,
            IWebHostEnvironment env,
            IHubContext<ProgressHub> hubContext)
        {
            _scopeFactory = scopeFactory;
            _env = env;
            _hubContext = hubContext;
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

            var totalStartTime = DateTime.Now;

            try
            {
                var outputDirectory = Path.GetDirectoryName(appDataPath)!;

                Console.WriteLine($"[SignalR] Wysyłam początkowy komunikat...");
                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", 0, 100,
                    $"🔄 Rozpoczęto przetwarzanie pliku: {Path.GetFileName(appDataPath)}\n" +
                    $"⚠️ Limit: {MAX_RECORDS_TO_PROCESS:N0} rekordów\n");

                // Inicjalizuj AddressSearchService
                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", 0, 100,
                    "⏳ Inicjalizacja serwisu wyszukiwania...");

                var initStartTime = DateTime.Now;
                var searchService = new AddressSearchService(context, _env.ContentRootPath);
                await searchService.InitializeAsync();
                var initTime = (DateTime.Now - initStartTime).TotalSeconds;

                Console.WriteLine($"[VerifyAddresses] ✓ Zainicjalizowano AddressSearchService w {initTime:F1}s");

                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", 0, 100,
                    $"✓ Serwis wyszukiwania zainicjaliony w {initTime:F1}s");

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
                    .Take(MAX_RECORDS_TO_PROCESS)
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
                int emptyCount = 0;

                var results = new List<VerificationResult>();
                const int reportInterval = 100;
                const int consoleReportInterval = 1000; // ✅ DODANE: Co 1000 rekordów do konsoli
                var processingStartTime = DateTime.Now;
                var lastConsoleReportTime = processingStartTime; // ✅ DODANE: Czas ostatniego raportu

                Console.WriteLine($"[VerifyAddresses] ========== ROZPOCZĘCIE PĘTLI PRZETWARZANIA ({DateTime.Now:HH:mm:ss.fff}) ==========");

                foreach (var line in dataLines)
                {
                    processedCount++;

                    var result = await ProcessLineAsync(line, searchService);
                    results.Add(result);

                    if (result.Status == "SUKCES")
                        successCount++;
                    else if (result.Status == "OSTRZEŻENIE")
                        warningCount++;
                    else if (result.Status == "PUSTY")
                        emptyCount++;
                    else
                        failureCount++;

                    // ✅ DODANE: Raportowanie do konsoli co 1000 rekordów
                    if (processedCount % consoleReportInterval == 0)
                    {
                        var now = DateTime.Now;
                        var elapsed = (now - processingStartTime).TotalSeconds;
                        var intervalTime = (now - lastConsoleReportTime).TotalSeconds;
                        var speed = elapsed > 0 ? (int)(processedCount / elapsed) : 0;
                        var intervalSpeed = intervalTime > 0 ? (int)(consoleReportInterval / intervalTime) : 0;

                        Console.WriteLine($"[Progress] {processedCount:N0}/{totalLines:N0} ({processedCount * 100.0 / totalLines:F1}%) " +
                                        $"| Avg: {speed:N0} rek/s | Last 1000: {intervalSpeed:N0} rek/s | Elapsed: {elapsed:F1}s " +
                                        $"| OK: {successCount:N0} | Warn: {warningCount:N0} | Empty: {emptyCount:N0} | Err: {failureCount:N0}");

                        lastConsoleReportTime = now;
                    }

                    // Raportowanie do SignalR co 100 rekordów (pozostaje bez zmian)
                    if (processedCount % reportInterval == 0 || processedCount == totalLines)
                    {
                        var elapsed = (DateTime.Now - processingStartTime).TotalSeconds;
                        var speed = elapsed > 0 ? (int)(processedCount / elapsed) : 0;
                        var progressMsg = $"Przetworzono: {processedCount:N0}/{totalLines:N0} ({speed:N0} rek/s)\n" +
                                         $"Sukces: {successCount:N0} | Ostrzeżenia: {warningCount:N0} | Puste: {emptyCount:N0} | Błędy: {failureCount:N0}";

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

                await _hubContext.Clients.All.SendAsync(
                    "ReceiveProgress",
                    "verify-addresses",
                    totalLines,
                    totalLines,
                    $"💾 Zapisywanie {results.Count:N0} wyników do pliku...");

                var saveStartTime = DateTime.Now;
                await SaveResultsAsync(outputDirectory, results);
                var saveTime = (DateTime.Now - saveStartTime).TotalSeconds;

                var totalTime = (DateTime.Now - totalStartTime).TotalSeconds;

                var summary = $"✅ Zakończono weryfikację!\n\n" +
                             $"📊 Statystyki:\n" +
                             $"   • Przetworzono: {processedCount:N0} rekordów\n" +
                             $"   • Sukces: {successCount:N0}\n" +
                             $"   • Ostrzeżenia: {warningCount:N0}\n" +
                             $"   • Puste: {emptyCount:N0}\n" +
                             $"   • Błędy: {failureCount:N0}\n\n" +
                             $"⏱️ Czasy wykonania:\n" +
                             $"   • Inicjalizacja cache: {initTime:F2}s\n" +
                             $"   • Wczytanie pliku: {readTime:F0}ms\n" +
                             $"   • Przetwarzanie pętli: {processingTime:F2}s\n" +
                             $"   • Zapis wyników: {saveTime:F2}s\n" +
                             $"   • CZAS CAŁKOWITY: {totalTime:F2}s\n" +
                             $"   • Prędkość: {(processingTime > 0 ? processedCount / processingTime : 0):F0} rek/s\n\n" +
                             $"📄 Wyniki zapisano do:\n" +
                             $"   • adresy_ok.txt ({successCount:N0} rekordów)\n" +
                             $"   • adresy_bledy.txt ({failureCount + warningCount:N0} rekordów)\n" +
                             $"   • adresy_puste.txt ({emptyCount:N0} rekordów)";

                await _hubContext.Clients.All.SendAsync(
                    "ReceiveProgress",
                    "verify-addresses",
                    totalLines,
                    totalLines,
                    summary);

                Console.WriteLine($"[VerifyAddresses] ========== ZAKOŃCZONO WERYFIKACJĘ ({DateTime.Now:HH:mm:ss.fff}) ==========");
                Console.WriteLine($"[VerifyAddresses] ⏱️ Czas całkowity: {totalTime:F2}s");
                Console.WriteLine($"[VerifyAddresses] 📊 Sukces: {successCount} | Ostrzeżenia: {warningCount} | Puste: {emptyCount} | Błędy: {failureCount}");
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

        /// <summary>
        /// Przetwarza pojedynczą linię z pliku CSV
        /// Format: ID|Kod|Miasto|Ulica|Budynek|Lokal|Wojewodztwo|Powiat|Gmina
        /// </summary>
        private async Task<VerificationResult> ProcessLineAsync(string line, AddressSearchService searchService)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var parts = line.Split('|');

            if (parts.Length < 6)
            {
                return new VerificationResult
                {
                    SourceId = parts.Length > 0 ? parts[0] : "UNKNOWN",
                    Status = "BŁĄD",
                    Message = "Nieprawidłowy format linii (za mało kolumn)",
                    SourceLine = line
                };
            }

            var id = parts[0];
            var kodPocztowy = parts.Length > 1 ? parts[1] : null;
            var miasto = parts.Length > 2 ? parts[2] : null;
            var ulica = parts.Length > 3 ? parts[3] : null;
            var budynek = parts.Length > 4 ? parts[4] : null;
            var lokal = parts.Length > 5 ? parts[5] : null;

            // ✅ SPRAWDZENIE: Czy adres ma wystarczające dane
            var hasMiasto = !string.IsNullOrWhiteSpace(miasto);
            var hasKod = !string.IsNullOrWhiteSpace(kodPocztowy);
            var hasUlica = !string.IsNullOrWhiteSpace(ulica);

            // Jeśli brak miasta ALBO (jest miasto ale brak kodu i ulicy)
            if (!hasMiasto || (hasMiasto && !hasKod && !hasUlica))
            {
                return new VerificationResult
                {
                    SourceId = id,
                    Status = "PUSTY",
                    Message = "Za mało danych",
                    SourceLine = line,
                    SourceKodPocztowy = kodPocztowy,
                    SourceMiasto = miasto,
                    SourceUlica = ulica,
                    SourceBudynek = budynek,
                    SourceLokal = lokal
                };
            }

            var request = new AddressSearchRequest
            {
                KodPocztowy = hasKod ? kodPocztowy : null,
                Miasto = miasto,
                Ulica = hasUlica ? ulica : null,
                NumerDomu = string.IsNullOrWhiteSpace(budynek) ? null : budynek,
                NumerMieszkania = string.IsNullOrWhiteSpace(lokal) ? null : lokal
            };

            var searchResult = await searchService.SearchAsync(request);

            sw.Stop();

            if (sw.ElapsedMilliseconds > 100) // Jeśli > 100ms
            {
                Console.WriteLine($"[SLOW] ID:{id} zajęło {sw.ElapsedMilliseconds}ms - {miasto}/{ulica}");
            }

            return new VerificationResult
            {
                SourceId = id,
                Status = MapStatus(searchResult.Status),
                Message = searchResult.Message ?? string.Empty,
                SourceLine = line,
                SourceKodPocztowy = kodPocztowy,
                SourceMiasto = miasto,
                SourceUlica = ulica,
                SourceBudynek = budynek,
                SourceLokal = lokal,
                FoundKodPocztowy = searchResult.KodPocztowy?.Kod,
                FoundMiasto = searchResult.Miasto?.Nazwa,
                FoundUlica = searchResult.Ulica != null ? $"{searchResult.Ulica.Cecha} {searchResult.Ulica.Nazwa1}".Trim() : null,
                FoundBudynek = searchResult.NormalizedBuildingNumber,
                FoundLokal = searchResult.NormalizedApartmentNumber,
                DiagnosticLog = searchResult.DiagnosticInfo
            };
        }

        private string MapStatus(AddressSearchStatus status)
        {
            return status switch
            {
                AddressSearchStatus.Success => "SUKCES",
                AddressSearchStatus.MultipleMatches => "OSTRZEŻENIE",
                AddressSearchStatus.MiastoNotFound => "BŁĄD",
                AddressSearchStatus.UlicaNotFound => "BŁĄD",
                AddressSearchStatus.KodPocztowyNotFound => "BŁĄD",
                AddressSearchStatus.ValidationError => "BŁĄD",
                _ => "BŁĄD"
            };
        }

        /// <summary>
        /// Zapisuje wyniki do plików: adresy_ok.txt, adresy_bledy.txt i adresy_puste.txt
        /// </summary>
        private async Task SaveResultsAsync(string outputDirectory, List<VerificationResult> results)
        {
            var okPath = Path.Combine(outputDirectory, "adresy_ok.txt");
            var errorPath = Path.Combine(outputDirectory, "adresy_bledy.txt");
            var emptyPath = Path.Combine(outputDirectory, "adresy_puste.txt");

            var okLines = new List<string> { "ID|Kod|Miasto|Ulica|Budynek|Lokal|Komunikat" };
            var errorLines = new List<string> { "ID|Status|Adres źródłowy|Komunikat" };
            var emptyLines = new List<string> { "ID|Adres źródłowy|Komunikat" };

            foreach (var result in results)
            {
                if (result.Status == "SUKCES")
                {
                    var kod = FormatWithChange(result.FoundKodPocztowy, result.SourceKodPocztowy);
                    var miasto = FormatWithChange(result.FoundMiasto, result.SourceMiasto);
                    var ulica = FormatWithChange(result.FoundUlica, result.SourceUlica);
                    var budynek = FormatWithChange(result.FoundBudynek, result.SourceBudynek);
                    var lokal = FormatWithChange(result.FoundLokal, result.SourceLokal);

                    okLines.Add($"{result.SourceId}|{kod}|{miasto}|{ulica}|{budynek}|{lokal}|{result.Message}");
                }
                else if (result.Status == "PUSTY")
                {
                    emptyLines.Add($"{result.SourceId}|{result.SourceLine}|{result.Message}");
                }
                else
                {
                    errorLines.Add($"{result.SourceId}|{result.Status}|{result.SourceLine}|{result.Message}");
                }
            }

            await System.IO.File.WriteAllLinesAsync(okPath, okLines, Encoding.UTF8);
            await System.IO.File.WriteAllLinesAsync(errorPath, errorLines, Encoding.UTF8);
            await System.IO.File.WriteAllLinesAsync(emptyPath, emptyLines, Encoding.UTF8);

            Console.WriteLine($"[VerifyAddresses] ✓ Zapisano wyniki:");
            Console.WriteLine($"   • {okPath} ({okLines.Count - 1} rekordów)");
            Console.WriteLine($"   • {errorPath} ({errorLines.Count - 1} rekordów)");
            Console.WriteLine($"   • {emptyPath} ({emptyLines.Count - 1} rekordów)");
        }

        /// <summary>
        /// Formatuje wartość z oryginalną w nawiasach kwadratowych jeśli się różnią
        /// Przykład: "Kraków[Krakow]" lub "Kraków" (jeśli identyczne)
        /// </summary>
        private string FormatWithChange(string? found, string? source)
        {
            // Jeśli oba są puste - zwróć pusty string
            if (string.IsNullOrWhiteSpace(found) && string.IsNullOrWhiteSpace(source))
                return string.Empty;

            // Jeśli znaleziono wartość
            if (!string.IsNullOrWhiteSpace(found))
            {
                // Jeśli źródło jest puste lub identyczne - zwróć tylko znalezioną
                if (string.IsNullOrWhiteSpace(source) ||
                    string.Equals(found.Trim(), source.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return found;
                }

                // Różne - zwróć z oryginalną w nawiasach
                return $"{found}[{source}]";
            }

            // Jeśli nie znaleziono, ale było źródło - zwróć źródło
            return source ?? string.Empty;
        }
    }

    /// <summary>
    /// Model wyniku weryfikacji pojedynczego adresu
    /// </summary>
    public class VerificationResult
    {
        public string SourceId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string SourceLine { get; set; } = string.Empty;

        // Dane źródłowe
        public string? SourceKodPocztowy { get; set; }
        public string? SourceMiasto { get; set; }
        public string? SourceUlica { get; set; }
        public string? SourceBudynek { get; set; }
        public string? SourceLokal { get; set; }

        // Dane znalezione
        public string? FoundKodPocztowy { get; set; }
        public string? FoundMiasto { get; set; }
        public string? FoundUlica { get; set; }
        public string? FoundBudynek { get; set; }
        public string? FoundLokal { get; set; }

        public string? DiagnosticLog { get; set; }
    }
}