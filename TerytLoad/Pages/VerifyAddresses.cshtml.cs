// Copyright (c) 2025-2026 Andrzej Szepczyński. All rights reserved.

using AddressLibrary.Data;
using AddressLibrary.Models;
using AddressLibrary.Services.AddressSearch;
using AddressLibrary.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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

        public static string DataDirectory { get; set; } = "Appdata/Address";

        [BindProperty]
        [Display(Name = "Ścieżka do pliku źródłowego")]
        public string InputFilePath { get; set; } = "adresy.txt";
        public string ErrorFilePath { get; set; } = "adresy_inp.txt";

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

            var outputFolder = System.IO.Path.Combine(_env.ContentRootPath, DataDirectory);

            var appDataPath = System.IO.Path.Combine(outputFolder, InputFilePath);
            Console.WriteLine($"[VerifyAddresses] Pełna ścieżka: {appDataPath}");

            if (!System.IO.File.Exists(appDataPath))
            {
                Console.WriteLine($"[VerifyAddresses] ✗ Plik nie istnieje!");
                Message = $"❌ Nie znaleziono pliku: {appDataPath}";
                return Page();
            }

            Console.WriteLine($"[VerifyAddresses] ✓ Plik znaleziony");

            // Uruchom przetwarzanie w tle
            _ = Task.Run(async () => await ProcessVerificationAsync(outputFolder));

            Message = $"🔄 Weryfikacja rozpoczęta w tle. Limit przetwarzania: {MAX_RECORDS_TO_PROCESS:N0} rekordów.";

            return Page();
        }

        private async Task ProcessVerificationAsync(string appDataFolder)
        {
            string adresyFilePath = System.IO.Path.Combine(appDataFolder, InputFilePath);
            string bladFilePath = System.IO.Path.Combine(appDataFolder, ErrorFilePath);

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

                Console.WriteLine($"[ProcessVerification] Wysyłam komunikat SignalR...");
                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-addresses", 0, 100,
                    $"🔄 Rozpoczęto przetwarzanie pliku: {InputFilePath}{Environment.NewLine}" +
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
                //var dataLines0 = await GenericFileLoader.LoadFromFileAsync<Adres>(adresyFilePath);
                var dataLines0 = await GenericFileLoader.LoadFromFileWithHeaderMappingAsync<Adres>(adresyFilePath);
                var errorLines = System.IO.File.Exists(bladFilePath)
                    ? await GenericFileLoader.LoadFromFileAsync<Adres>(bladFilePath)
                    : new List<Adres>();

                // Poniższe można zamieniać z DataLines
                //                var errorIds = new HashSet<string>(errorLines.Select(x => x.Id));
                //                var dataLines = dataLines0.Where(x => errorIds.Contains(x.Id)).ToList();
                var dataLines = dataLines0;

                // Wyodrębnij adresy z obcymi krajami i zapisz je do adresy_obce.txt
                var foreignList = dataLines.Where(a => !string.IsNullOrWhiteSpace(a.Kraj)
                                                      && !string.Equals(a.Kraj, "PL", StringComparison.OrdinalIgnoreCase)
                                                      && !string.Equals(a.Kraj, "Polska", StringComparison.OrdinalIgnoreCase))
                                           .ToList();

                if (foreignList.Count > 0)
                {
                    try
                    {
                        var foreignPath = System.IO.Path.Combine(appDataFolder, "adresy_obce.txt");
                        // Zapisz w tym samym formacie co adresy.txt
                        await AddressLibrary.Helpers.ExcelTableWriter.WriteToTextFileAsync(foreignList, foreignPath, '|', Encoding.UTF8, includeHeader: true);
                        Console.WriteLine($"[VerifyAddresses] Zapisano {foreignList.Count} adresów obcych do: {foreignPath}");

                        await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                            "verify-addresses",
                            0,
                            dataLines.Count(),
                            $"ℹ️ Zapisano {foreignList.Count} adresów z obcymi krajami do: adresy_obce.txt");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[VerifyAddresses] Błąd zapisu adresy_obce.txt: {ex.Message}");
                    }

                    // Usuń obce adresy z listy do przetworzenia
                    var foreignIds = new HashSet<string>(foreignList.Select(x => x.Id));
                    dataLines = dataLines.Where(a => !foreignIds.Contains(a.Id)).ToList();
                }

                var readTime = (DateTime.Now - readStartTime).TotalMilliseconds;

                Console.WriteLine($"[VerifyAddresses] ✓ Wczytano plik w {readTime:F0}ms");

                if (dataLines.Count() == 0)
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

                var totalLines = dataLines.Count();
                var totalLinesInFile = dataLines.Count();

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

                // foreach (var item in dataLines.Where(x=>x.Id== "A1346398"))

                foreach (var item in dataLines)
                {
                    var lineStartTime = DateTime.Now;

                    processedCount++;


                    //
                    // Tutaj procesuje się pojedyncza linia
                    //

                    var result = await ProcessLineAsync(item, searchService);
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

                    var lineTime = (DateTime.Now - lineStartTime).TotalMilliseconds;
                    if (lineTime > 100)
                    {
                        var addressInfo = $"{item.Miasto}, {item.Ulica} {item.NrDomu}".Trim();
                        Console.WriteLine($"⚠️ SLOW [{lineTime:F0}ms] ID: {item.Id} | {addressInfo} | Status: {result.Status}");
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
                await SaveResultsAsync(appDataFolder, results);
                var saveTime = (DateTime.Now - saveStartTime).TotalSeconds;

                var totalTime = (DateTime.Now - totalStartTime).TotalSeconds;
                var totalEndTime = DateTime.Now;

                var summary = $"✅ Zakończono weryfikację!{Environment.NewLine}{Environment.NewLine}" +
                             $"🕐 Czas:{Environment.NewLine}" +
                             $"   • Start:      {totalStartTime:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                             $"   • Koniec:     {totalEndTime:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                             $"   • Czas trwania: {TimeSpan.FromSeconds(totalTime):hh\\:mm\\:ss}{Environment.NewLine}{Environment.NewLine}" +
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
        private async Task<VerificationResult> ProcessLineAsync(Adres item, AddressSearchService searchService)
        {
            try
            {

                bool hasKod = !string.IsNullOrWhiteSpace(item.Kod);
                bool hasUlica = !string.IsNullOrWhiteSpace(item.Ulica);

                if (string.IsNullOrWhiteSpace(item.Miasto))
                {
                    return new VerificationResult
                    {
                        Status = "PUSTY",
                        Message = "Brak nazwy miasta",
                        SourceId = item.Id,
                        SourceKraj = item.Kraj,
                        SourceItem = item,
                        SourceKodPocztowy = item.Kod,
                        SourceMiasto = item.Miasto,
                        SourceUlica = item.Ulica,
                        SourceBudynek = item.NrDomu,
                        SourceLokal = item.NrLokalu,
                        SourceWojewodztwo = item.Wojewodztwo,
                        SourcePowiat = item.Powiat,
                        SourceGmina = item.Gmina,
                        Method = "N/A"
                    };
                }

                // Prepare AddressSearchRequest
                string inputMiasto = item.Miasto ?? string.Empty;
                string inputDzielnica = string.Empty;

                // Jeśli miasto ma postać: "Kraków (Krowodrza)" - rozdzielimy je na miasto i dzielnicę
                var m = Regex.Match(inputMiasto, "^\\s*(.*?)\\s*\\((.*?)\\)\\s*$");
                if (m.Success)
                {
                    inputMiasto = m.Groups[1].Value.Trim();
                    inputDzielnica = m.Groups[2].Value.Trim();
                }

                var request = new AddressSearchRequest
                {
                    KodPocztowy = hasKod ? item.Kod : null,
                    Miasto = inputMiasto,
                    Dzielnica = inputDzielnica,
                    Ulica = hasUlica ? item.Ulica : null,
                    NumerDomu = string.IsNullOrWhiteSpace(item.NrDomu) ? null : item.NrDomu,
                    NumerMieszkania = string.IsNullOrWhiteSpace(item.NrLokalu) ? null : item.NrLokalu
                };

                var plik = System.IO.Path.Combine(_env.ContentRootPath, DataDirectory, "Przetworzono.txt");
                var values = item.GetType()
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Select(p => p.GetValue(item));

                var razem = string.Join("|", values);
                System.IO.File.WriteAllText(plik, razem + Environment.NewLine);

                var searchResult = await searchService.SearchAsync(request);

                // Budowanie nowaUlica
                var nowaUlica = "";

                var ul = searchResult.Ulica;
                if (ul != null)
                {
                    nowaUlica = ul.NazwaTeryt;

                    if (ul.Dzielnica!="")
                    {
                        nowaUlica = $"{nowaUlica} {ul.Dzielnica}".Trim();
                    }
                }
                nowaUlica = Regex.Replace(nowaUlica, @"\s+", " ").Trim();
                // ✅ POPRAWIONE: Używaj GetOverallMethod() zamiast sprawdzania Message
                string method = searchResult.GetOverallMethod() == MatchingMethod.Fuzzy ? "Fuzzy" : "Strict";

                return new VerificationResult
                {
                    SourceId = item.Id,
                    SourceKraj = item.Kraj,
                    Status = MapStatus(searchResult.Status),
                    Message = searchResult.Message ?? string.Empty,
                    SourceItem = item,
                    SourceKodPocztowy = item.Kod,
                    SourceMiasto = item.Miasto,
                    SourceUlica = item.Ulica,
                    SourceBudynek = item.NrDomu,
                    SourceLokal = item.NrLokalu,
                    SourceWojewodztwo = item.Wojewodztwo,
                    SourcePowiat = item.Powiat,
                    SourceGmina = item.Gmina,
                    FoundKodPocztowy = searchResult.KodPocztowy?.Kod,
                    FoundMiasto = searchResult.Miasto?.Nazwa,
                    FoundUlica = nowaUlica,
                    FoundBudynek = searchResult.NormalizedBuildingNumber,
                    FoundLokal = searchResult.NormalizedApartmentNumber,
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
                    SourceItem = item,
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




        /// Zapisuje wyniki do plików: adresy_ok.txt, adresy_fuzzy.txt, adresy_bledy.txt, adresy_brak_numeru.txt, adresy_brakkodu.txt i adresy_puste.txt
        /// </summary>
        private async Task SaveResultsAsync(string outputDirectory, List<VerificationResult> results)
        {
            var okPath = System.IO.Path.Combine(outputDirectory, "adresy_ok.txt");
            var fuzzyPath = System.IO.Path.Combine(outputDirectory, "adresy_fuzzy.txt");
            var errorPath = System.IO.Path.Combine(outputDirectory, "adresy_bledy.txt");
            var brakNumeruPath = System.IO.Path.Combine(outputDirectory, "adresy_brak_numeru.txt");
            var brakKoduPath = System.IO.Path.Combine(outputDirectory, "adresy_brakkodu.txt");
            var emptyPath = System.IO.Path.Combine(outputDirectory, "adresy_puste.txt");

            var everyLine = "ID|Kraj|Kod|Miasto|Ulica|NrDomu|NrLokalu|Wojewodztwo|Powiat|Gmina";

            var okLines = new List<string> { everyLine };
            var fuzzyLines = new List<string> { everyLine };
            var errorLines = new List<string> { $"Komunikat|{everyLine}" };
            var brakNumeruLines = new List<string> { $"Komunikat|{everyLine}" };
            var brakKoduLines = new List<string> { $"Komunikat|{everyLine}" };
            var emptyLines = new List<string> { everyLine };

            foreach (var result in results.OrderBy(r => r.Message).ToList())
            {
                var adres = new Adres();
                adres.Kraj = result.SourceKraj;
                adres.Kod = result.SourceKodPocztowy;
                adres.Miasto = result.SourceMiasto;
                adres.Ulica = result.SourceUlica;
                adres.NrDomu = result.SourceBudynek;
                adres.NrLokalu = result.SourceLokal;
                adres.Gmina = result.SourceGmina;
                adres.Powiat = result.SourcePowiat;
                adres.Wojewodztwo = result.SourceWojewodztwo;

                switch (result.Status)
                {
                    case "SUKCES":
                        adres.Kod = FormatWithChange(result.FoundKodPocztowy, result.SourceKodPocztowy);
                        adres.Miasto = FormatWithChange(result.FoundMiasto, result.SourceMiasto);
                        adres.Ulica = FormatWithChange(result.FoundUlica, result.SourceUlica);
                        adres.NrDomu = FormatWithChange(result.FoundBudynek, result.SourceBudynek);
                        adres.NrLokalu = FormatWithChange(result.FoundLokal, result.SourceLokal);
                        adres.Gmina = FormatWithChange(result.FoundGmina, result.SourceGmina);
                        adres.Powiat = FormatWithChange(result.FoundPowiat, result.SourcePowiat);
                        adres.Wojewodztwo = FormatWithChange(result.FoundWojewodztwo, result.SourceWojewodztwo);

                        // Rozdziel strict i fuzzy
                        if (result.Method == "Fuzzy")
                        {
                            fuzzyLines.Add($"{result.SourceId}|{ConcatenateAddress(adres)}");
                        }
                        else
                        {
                            okLines.Add($"{result.SourceId}|{ConcatenateAddress(adres)}");
                        }
                        break;

                    case "PUSTY":
                        emptyLines.Add($"{result.SourceId}|{ConcatenateAddress(adres)}");
                        break;

                    case "BŁĄD":
                    case "OSTRZEŻENIE":
                        var sDiag = result.DiagnosticLog?.Replace("\n", ",").Replace("\r", "");

                        // ✅ Klasyfikacja błędów według typu
                        if (result.Message != null && result.Message.Contains("Numer domu jest wymagany"))
                        {
                            brakNumeruLines.Add($"{result.Message}/{sDiag}|{result.SourceId}|{ConcatenateAddress(adres)}");
                        }
                        else
                        if (result.Message != null && result.Message.Contains("Nie znaleziono kodu pocztowego dla podanych parametrów"))
                        {
                            brakKoduLines.Add($"{result.Message}/{sDiag}|{result.SourceId}|{ConcatenateAddress(adres)}");
                        }
                        else
                        {
                            errorLines.Add($"{result.Message}/{sDiag}|{result.SourceId}|{ConcatenateAddress(adres)}");
                        }
                        break;
                }
            }

            await System.IO.File.WriteAllLinesAsync(okPath, okLines, Encoding.UTF8);
            await System.IO.File.WriteAllLinesAsync(fuzzyPath, fuzzyLines, Encoding.UTF8);
            await System.IO.File.WriteAllLinesAsync(errorPath, errorLines, Encoding.UTF8);
            await System.IO.File.WriteAllLinesAsync(brakNumeruPath, brakNumeruLines, Encoding.UTF8); // ✅ NOWE
            await System.IO.File.WriteAllLinesAsync(brakKoduPath, brakKoduLines, Encoding.UTF8); // ✅ NOWE
            await System.IO.File.WriteAllLinesAsync(emptyPath, emptyLines, Encoding.UTF8);

            Console.WriteLine($"[VerifyAddresses] ✓ Zapisano wyniki:");
            Console.WriteLine($"   • {okPath} ({okLines.Count - 1} rekordów - strict)");
            Console.WriteLine($"   • {fuzzyPath} ({fuzzyLines.Count - 1} rekordów - fuzzy)");
            Console.WriteLine($"   • {errorPath} ({errorLines.Count - 1} rekordów)");
            Console.WriteLine($"   • {brakNumeruPath} ({brakNumeruLines.Count - 1} rekordów)"); // ✅ NOWE
            Console.WriteLine($"   • {brakKoduPath} ({brakKoduLines.Count - 1} rekordów)"); // ✅ NOWE
            Console.WriteLine($"   • {emptyPath} ({emptyLines.Count - 1} rekordów)");
        }

        private string ConcatenateAddress(Adres r)
        {
            return $"{r.Kraj}|{r.Kod}|{r.Miasto}|{r.Ulica}|{r.NrDomu}|{r.NrLokalu}|{r.Wojewodztwo}|{r.Powiat}|{r.Gmina}";
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
        public Adres? SourceItem { get; set; }
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