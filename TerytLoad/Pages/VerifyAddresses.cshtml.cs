// Copyright (c) 2025-2026 Andrzej Szepczyński. All rights reserved.

using AddressLibrary.Data;
using AddressLibrary.Helpers;
using AddressLibrary.Models;
using AddressLibrary.Services.AddressSearch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.FileSystemGlobbing.Internal;
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
            try
            {
                Console.WriteLine("[ProcessVerification] ========== START ==========");
                Console.WriteLine($"[ProcessVerification] Tworzę scope...");

                using var scope = _scopeFactory.CreateScope();
                Console.WriteLine($"[ProcessVerification] ✓ Scope utworzony");

                var context = scope.ServiceProvider.GetRequiredService<AddressDbContext>();
                Console.WriteLine($"[ProcessVerification] ✓ DbContext pobrany");

                var totalStartTime = DateTime.Now;

                Console.WriteLine($"[ProcessVerification] Tworzę AddressSearchService...");
                using var searchService = new AddressSearchService(context, _env.ContentRootPath);
                Console.WriteLine($"[ProcessVerification] ✓ AddressSearchService utworzony");

                var outputDirectory = Path.GetDirectoryName(appDataPath)!;

                Console.WriteLine($"[ProcessVerification] Wysyłam komunikat SignalR...");
                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", 0, 100,
                    $"🔄 Rozpoczęto przetwarzanie pliku: {Path.GetFileName(appDataPath)}{Environment.NewLine}" +
                    $"⚠️ Limit: {MAX_RECORDS_TO_PROCESS:N0} rekordów{Environment.NewLine}");

                Console.WriteLine($"[ProcessVerification] ✓ Komunikat SignalR wysłany");

                // Inicjalizuj AddressSearchService
                Console.WriteLine($"[ProcessVerification] Wysyłam komunikat o inicjalizacji...");
                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", 0, 100,
                    "⏳ Inicjalizacja serwisu wyszukiwania...");

                Console.WriteLine($"[ProcessVerification] Wywołuję InitializeAsync()...");
                var initStartTime = DateTime.Now;

                await searchService.InitializeAsync();

                var initTime = (DateTime.Now - initStartTime).TotalSeconds;
                Console.WriteLine($"[ProcessVerification] ✓ InitializeAsync zakończone w {initTime:F1}s");

                await _hubContext.Clients.All.SendAsync(
                    "ReceiveProgress",
                    "verify-addresses",
                    0,
                    100,
                    $"✓ Serwis wyszukiwania zainicjaliony w {initTime:F1}s");

                // Wczytaj dane z pliku
                var readStartTime = DateTime.Now;
                var lines = await System.IO.File.ReadAllLinesAsync(appDataPath, Encoding.UTF8);
                var readTime = (DateTime.Now - readStartTime).TotalMilliseconds;

                Console.WriteLine($"[VerifyAddresses] ✓ Wczytano plik w {readTime:F0}ms");

                if (lines.Length == 0)
                {
                    await _hubContext.Clients.All.SendAsync(
                        "ReceiveProgress",
                        "verify-addresses",
                        0,
                        100,
                        "⚠️ Plik jest pusty!");
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

                await _hubContext.Clients.All.SendAsync(
                    "ReceiveProgress",
                    "verify-addresses",
                    0,
                    totalLines,
                    $"📊 Przygotowano {totalLines:N0} rekordów do weryfikacji{Environment.NewLine}{limitInfo}");

                int processedCount = 0;
                int successCount = 0;
                int successFuzzyCount = 0; // ✅ NOWE
                int warningCount = 0;
                int failureCount = 0;
                int emptyCount = 0;

                var results = new List<VerificationResult>();
                const int reportInterval = 100;
                const int consoleReportInterval = 1000;
                var processingStartTime = DateTime.Now;
                var lastConsoleReportTime = processingStartTime;

                Console.WriteLine($"[VerifyAddresses] ========== ROZPOCZĘCIE PĘTLI PRZETWARZANIA ({DateTime.Now:HH:mm:ss.fff}) ==========");

                foreach (var line in dataLines)
                {
                    processedCount++;

                    var result = await ProcessLineAsync(line, searchService);
                    results.Add(result);

                    if (result.Status == "SUKCES")
                    {
                        successCount++;
                        if (result.Method == "Fuzzy") // ✅ NOWE
                            successFuzzyCount++;
                    }
                    else if (result.Status == "OSTRZEŻENIE")
                        warningCount++;
                    else if (result.Status == "PUSTY")
                        emptyCount++;
                    else
                        failureCount++;

                    // Raportowanie do konsoli co 1000 rekordów
                    if (processedCount % consoleReportInterval == 0)
                    {
                        var now = DateTime.Now;
                        var elapsed = (now - processingStartTime).TotalSeconds;
                        var intervalTime = (now - lastConsoleReportTime).TotalSeconds;
                        var speed = elapsed > 0 ? (int)(processedCount / elapsed) : 0;
                        var intervalSpeed = intervalTime > 0 ? (int)(consoleReportInterval / intervalTime) : 0;

                        Console.WriteLine($"[Progress] {processedCount:N0}/{totalLines:N0} ({processedCount * 100.0 / totalLines:F1}%) " +
                                        $"| Avg: {speed:N0} rek/s | Last 1000: {intervalSpeed:N0} rek/s | Elapsed: {elapsed:F1}s " +
                                        $"| OK: {successCount:N0} (Fuzzy: {successFuzzyCount:N0}) | Warn: {warningCount:N0} | Empty: {emptyCount:N0} | Err: {failureCount:N0}");

                        lastConsoleReportTime = now;
                    }

                    // Raportowanie do SignalR co 100 rekordów
                    if (processedCount % reportInterval == 0 || processedCount == totalLines)
                    {
                        var elapsed = (DateTime.Now - processingStartTime).TotalSeconds;
                        var speed = elapsed > 0 ? (int)(processedCount / elapsed) : 0;
                        var progressMsg = $"Przetworzono: {processedCount:N0}/{totalLines:N0} ({speed:N0} rek/s){Environment.NewLine}" +
                                         $"Sukces: {successCount:N0} (Fuzzy: {successFuzzyCount:N0}) | Ostrzeżenia: {warningCount:N0} | Puste: {emptyCount:N0} | Błędy: {failureCount:N0}";

                        await _hubContext.Clients.All.SendAsync(
                            "ReceiveProgress",
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

                var summary = $"✅ Zakończono weryfikację!{Environment.NewLine}{Environment.NewLine}" +
                             $"📊 Statystyki:{Environment.NewLine}" +
                             $"   • Przetworzono: {processedCount:N0} rekordów{Environment.NewLine}" +
                             $"   • Sukces: {successCount:N0}{Environment.NewLine}" +
                             $"     - Strict: {successCount - successFuzzyCount:N0}{Environment.NewLine}" +
                             $"     - Fuzzy: {successFuzzyCount:N0}{Environment.NewLine}" +
                             $"   • Ostrzeżenia: {warningCount:N0}{Environment.NewLine}" +
                             $"   • Puste: {emptyCount:N0}{Environment.NewLine}" +
                             $"   • Błędy: {failureCount:N0}{Environment.NewLine}{Environment.NewLine}" +
                             $"⏱️ Czasy wykonania:{Environment.NewLine}" +
                             $"   • Inicjalizacja cache: {initTime:F2}s{Environment.NewLine}" +
                             $"   • Wczytanie pliku: {readTime:F0}ms{Environment.NewLine}" +
                             $"   • Przetwarzanie pętli: {processingTime:F2}s{Environment.NewLine}" +
                             $"   • Zapis wyników: {saveTime:F2}s{Environment.NewLine}" +
                             $"   • CZAS CAŁKOWITY: {totalTime:F2}s{Environment.NewLine}" +
                             $"   • Prędkość: {(processingTime > 0 ? processedCount / processingTime : 0):F0} rek/s{Environment.NewLine}{Environment.NewLine}" +
                             $"📄 Wyniki zapisano do:{Environment.NewLine}" +
                             $"   • adresy_ok.txt ({successCount - successFuzzyCount:N0} rekordów - strict){Environment.NewLine}" +
                             $"   • adresy_fuzzy.txt ({successFuzzyCount:N0} rekordów - fuzzy matching){Environment.NewLine}" +
                             $"   • adresy_bledy.txt ({failureCount + warningCount:N0} rekordów){Environment.NewLine}" +
                             $"   • adresy_puste.txt ({emptyCount:N0} rekordów)";

                await _hubContext.Clients.All.SendAsync(
                    "ReceiveProgress",
                    "verify-addresses",
                    totalLines,
                    totalLines,
                    summary);

                Console.WriteLine($"[VerifyAddresses] ========== ZAKOŃCZONO WERYFIKACJĘ ({DateTime.Now:HH:mm:ss.fff}) ==========");
                Console.WriteLine($"[VerifyAddresses] ⏱️ Czas całkowity: {totalTime:F2}s");
                Console.WriteLine($"[VerifyAddresses] 📊 Sukces: {successCount} (Strict: {successCount - successFuzzyCount}, Fuzzy: {successFuzzyCount}) | Ostrzeżenia: {warningCount} | Puste: {emptyCount} | Błędy: {failureCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProcessVerification] ✗✗✗ WYJĄTEK ✗✗✗");
                Console.WriteLine($"[ProcessVerification] Message: {ex.Message}");
                Console.WriteLine($"[ProcessVerification] Type: {ex.GetType().Name}");
                Console.WriteLine($"[ProcessVerification] StackTrace: {ex.StackTrace}");

                var innerEx = ex.InnerException;
                while (innerEx != null)
                {
                    Console.WriteLine($"[ProcessVerification] Inner Exception: {innerEx.Message}");
                    innerEx = innerEx.InnerException;
                }

                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", 0, 100,
                    $"❌ BŁĄD: {ex.Message}");

                throw;
            }
        }

        /// <summary>
        /// Przetwarza pojedynczą linię z pliku CSV
        /// Format: ID|Kraj|Kod|Miasto|Ulica|Budynek|Lokal|Wojewodztwo|Powiat|Gmina
        /// </summary>
        private async Task<VerificationResult> ProcessLineAsync(string line, AddressSearchService searchService)
        {
            try
            {
                var parts = line.Split('|');

                if (parts.Length < 10)
                {
                    return new VerificationResult
                    {
                        Status = "BŁĄD",
                        Message = $"Nieprawidłowa liczba kolumn: {parts.Length}",
                        SourceLine = line,
                        Method = "N/A"
                    };
                }

                var id = parts[0].Trim();
                var kraj = parts[1].Trim();
                var kodPocztowy = parts[2].Trim();
                var miasto = parts[3].Trim();
                var ulica = parts[4].Trim();
                var budynek = parts[5].Trim();
                var lokal = parts[6].Trim();
                var wojewodztwo = parts[7].Trim();
                var powiat = parts[8].Trim();
                var gmina = parts[9].Trim();

                bool hasKod = !string.IsNullOrWhiteSpace(kodPocztowy);
                bool hasUlica = !string.IsNullOrWhiteSpace(ulica);

                if (string.IsNullOrWhiteSpace(miasto))
                {
                    return new VerificationResult
                    {
                        Status = "PUSTY",
                        Message = "Brak nazwy miasta",
                        SourceId = id,
                        SourceKraj = kraj,
                        SourceLine = line,
                        SourceKodPocztowy = kodPocztowy,
                        SourceMiasto = miasto,
                        SourceUlica = ulica,
                        SourceBudynek = budynek,
                        SourceLokal = lokal,
                        SourceWojewodztwo = wojewodztwo,
                        SourcePowiat = powiat,
                        SourceGmina = gmina,
                        Method = "N/A"
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

                // Budowanie nowaUlica
                var nowaUlica = "";
                if (searchResult.Ulica != null)
                {
                    string sPrefix;
                    switch (searchResult.Ulica.Cecha) 
                    {
                        case "rynek": 
                            sPrefix = "";
                            break;
                        case "inne": 
                            sPrefix = "";
                            break;
                        default: 
                            sPrefix = searchResult.Ulica.Cecha ?? "";
                            break;
                    };
                    nowaUlica = $"{sPrefix} {searchResult.Ulica.Nazwa1}".Trim();
                }

                // ✅ POPRAWIONE: Używaj GetOverallMethod() zamiast sprawdzania Message
                string method = searchResult.GetOverallMethod() == MatchingMethod.Fuzzy ? "Fuzzy" : "Strict";

                return new VerificationResult
                {
                    SourceId = id,
                    SourceKraj = kraj,
                    Status = MapStatus(searchResult.Status),
                    Message = searchResult.Message ?? string.Empty,
                    SourceLine = line,
                    SourceKodPocztowy = kodPocztowy,
                    SourceMiasto = miasto,
                    SourceUlica = ulica,
                    SourceBudynek = budynek,
                    SourceLokal = lokal,
                    SourceWojewodztwo = wojewodztwo,
                    SourcePowiat = powiat,
                    SourceGmina = gmina,
                    // ✅ POPRAWIONE: Używaj Miejscowosc (tak jest w AddressSearchResult)
                    FoundKodPocztowy = searchResult.KodPocztowy?.Kod,
                    FoundMiasto = searchResult.Miasto?.Nazwa,
                    FoundUlica = nowaUlica,
                    FoundBudynek = searchResult.NormalizedBuildingNumber,
                    FoundLokal = searchResult.NormalizedApartmentNumber,
                    // ✅ POPRAWIONE: Dostęp przez hierarchię Miejscowosc
                    FoundGmina = searchResult.Miasto?.Gmina?.Nazwa,
                    FoundPowiat = searchResult.Miasto?.Gmina?.Powiat?.Nazwa,
                    FoundWojewodztwo = searchResult.Miasto?.Gmina?.Powiat?.Wojewodztwo?.Nazwa,
                    DiagnosticLog = searchResult.DiagnosticInfo,
                    Method = method
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProcessLine] ❌ WYJĄTEK ❌");
                Console.WriteLine($"[ProcessLine] Message: {ex.Message}");
                Console.WriteLine($"[ProcessLine] Type: {ex.GetType().Name}");
                Console.WriteLine($"[ProcessLine] StackTrace: {ex.StackTrace}");

                return new VerificationResult
                {
                    Status = "BŁĄD",
                    Message = $"Wyjątek: {ex.Message}",
                    SourceLine = line,
                    Method = "N/A"
                };
            }
        }

        private string MapStatus(AddressSearchStatus status)
        {
            return status switch
            {
                AddressSearchStatus.Success => "SUKCES",
                AddressSearchStatus.MultipleMatches => "OSTRZEŻENIE",
                AddressSearchStatus.MiastoNotFound => "BŁĄD",
                AddressSearchStatus.UlicaNotFound => "BŁĄD",
                AddressSearchStatus.InvalidStreetName => "BŁĄD",
                AddressSearchStatus.KodPocztowyNotFound => "BŁĄD",
                AddressSearchStatus.ValidationError => "BŁĄD",
                _ => "BŁĄD"
            };
        }

        /// <summary>
        /// Zapisuje wyniki do plików: adresy_ok.txt, adresy_fuzzy.txt, adresy_bledy.txt i adresy_puste.txt
        /// </summary>
        private async Task SaveResultsAsync(string outputDirectory, List<VerificationResult> results)
        {
            var okPath = Path.Combine(outputDirectory, "adresy_ok.txt");
            var fuzzyPath = Path.Combine(outputDirectory, "adresy_fuzzy.txt"); // ✅ NOWE
            var errorPath = Path.Combine(outputDirectory, "adresy_bledy.txt");
            var emptyPath = Path.Combine(outputDirectory, "adresy_puste.txt");

            var everyLine = "ID|Kod|Miejscowość|Ulica|Nr domu|Nr mieszkania|Województwo|Powiat|Gmina";

            var okLines = new List<string> { everyLine };
            var fuzzyLines = new List<string> { everyLine }; // ✅ NOWE
            var errorLines = new List<string> { $"Komunikat|{everyLine}" };
            var emptyLines = new List<string> { everyLine };

            foreach (var result in results.OrderBy(r => r.Message).ToList())
            {
                var kod = result.SourceKodPocztowy;
                var miasto = result.SourceMiasto;
                var ulica = result.SourceUlica;
                var budynek = result.SourceBudynek;
                var lokal = result.SourceLokal;
                var gmina = result.SourceGmina;
                var powiat = result.SourcePowiat;
                var wojewodztwo = result.SourceWojewodztwo;

                switch (result.Status)
                {
                    case "SUKCES":
                        kod = FormatWithChange(result.FoundKodPocztowy, result.SourceKodPocztowy);
                        miasto = FormatWithChange(result.FoundMiasto, result.SourceMiasto);
                        ulica = FormatWithChange(result.FoundUlica, result.SourceUlica);
                        budynek = FormatWithChange(result.FoundBudynek, result.SourceBudynek);
                        lokal = FormatWithChange(result.FoundLokal, result.SourceLokal);
                        gmina = FormatWithChange(result.FoundGmina, result.SourceGmina);
                        powiat = FormatWithChange(result.FoundPowiat, result.SourcePowiat);
                        wojewodztwo = FormatWithChange(result.FoundWojewodztwo, result.SourceWojewodztwo);

                        var line = $"{result.SourceId}|{kod}|{miasto}|{ulica}|{budynek}|{lokal}|{wojewodztwo}|{powiat}|{gmina}";

                        // ✅ NOWE: Rozdziel strict i fuzzy
                        if (result.Method == "Fuzzy")
                        {
                            fuzzyLines.Add(line);
                        }
                        else
                        {
                            okLines.Add(line);
                        }
                        break;

                    case "PUSTY":
                        emptyLines.Add($"{result.SourceId}|{kod}|{miasto}|{ulica}|{budynek}|{lokal}|{wojewodztwo}|{powiat}|{gmina}");
                        break;

                    case "BŁĄD":
                    case "OSTRZEŻENIE":
                        var sDiag = result.DiagnosticLog?.Replace("\n", ",").Replace("\r", "");
                        errorLines.Add($"{result.Message}/{sDiag}|{result.SourceId}|{kod}|{miasto}|{ulica}|{budynek}|{lokal}|{wojewodztwo}|{powiat}|{gmina}");
                        break;
                }
            }

            await System.IO.File.WriteAllLinesAsync(okPath, okLines, Encoding.UTF8);
            await System.IO.File.WriteAllLinesAsync(fuzzyPath, fuzzyLines, Encoding.UTF8); // ✅ NOWE
            await System.IO.File.WriteAllLinesAsync(errorPath, errorLines, Encoding.UTF8);
            await System.IO.File.WriteAllLinesAsync(emptyPath, emptyLines, Encoding.UTF8);

            Console.WriteLine($"[VerifyAddresses] ✓ Zapisano wyniki:");
            Console.WriteLine($"   • {okPath} ({okLines.Count - 1} rekordów - strict)");
            Console.WriteLine($"   • {fuzzyPath} ({fuzzyLines.Count - 1} rekordów - fuzzy)"); // ✅ NOWE
            Console.WriteLine($"   • {errorPath} ({errorLines.Count - 1} rekordów)");
            Console.WriteLine($"   • {emptyPath} ({emptyLines.Count - 1} rekordów)");
        }

        /// <summary>
        /// Formatuje wartość z oryginalną w nawiasach kwadratowych jeśli się różnią
        /// Przykład: "Kraków[Krakow]" lub "Kraków" (jeśli identyczne)
        /// </summary>
        private string FormatWithChange(string? found, string? source)
        {
            if (found == null) found = "";
            if (source == null) source = "";

            if (string.Equals(found.Trim(), source.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return found;
            }

            return $"{found}[{source}]";
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
        public string Method { get; set; } = "Strict"; // ✅ NOWE: "Strict" lub "Fuzzy"

        // Dane źródłowe
        public string? SourceKraj { get; set; } // ✅ DODANE
        public string? SourceKodPocztowy { get; set; }
        public string? SourceMiasto { get; set; }
        public string? SourceUlica { get; set; }
        public string? SourceBudynek { get; set; }
        public string? SourceLokal { get; set; }
        public string? SourceWojewodztwo { get; set; }
        public string? SourcePowiat { get; set; }
        public string? SourceGmina { get; set; }

        // Dane znalezione
        public string? FoundKodPocztowy { get; set; }
        public string? FoundMiasto { get; set; }
        public string? FoundUlica { get; set; }
        public string? FoundBudynek { get; set; }
        public string? FoundLokal { get; set; }
        public string? FoundWojewodztwo { get; set; }
        public string? FoundPowiat { get; set; }
        public string? FoundGmina { get; set; }

        public string? DiagnosticLog { get; set; }
    }
}