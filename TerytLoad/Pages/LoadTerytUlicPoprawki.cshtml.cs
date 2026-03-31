using AddressLibrary.Logging;
using AddressLibrary.Data;
using AddressLibrary;
using AddressLibrary.Helpers;
using AddressLibrary.Models;
using AddressLibrary.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace TerytLoad.Pages
{
    public class LoadTerytUlicPoprawkiModel : PageModel
    {
        private static bool _isRunning = false;
        private static int _totalCount = 0;
        private static int _processedCount = 0;
        private static string _currentOperation = string.Empty;
        private static string? _errorMessage = null;
        private static string? _stackTrace = null;
        private static LoadResult? _resultPoprawki = null;
        private static ValidatorResult? _resultTypyUlic = null;
        private static bool _isCompleted = false;

        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<LoadTerytUlicPoprawkiModel> _logger;

        public bool IsProcessing => _isRunning;
        public bool ShowResults => _isCompleted && !_isRunning;
        public string CurrentOperation => _currentOperation;
        public int TotalCount => _totalCount;
        public int ProcessedCount => _processedCount;
        public int InsertedCount { get; set; }
        public int FoundCount { get; set; }
        public int NotFoundCount { get; set; }
        public string LogFilePath { get; set; } = string.Empty;
        public string LogFilePathTypyUlic { get; set; } = string.Empty;
        public int ProgressPercentage => TotalCount > 0 ? (ProcessedCount * 100 / TotalCount) : 0;
        public string? ErrorMessage => _errorMessage;
        public string? StackTrace => _stackTrace;

        public LoadTerytUlicPoprawkiModel(
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<LoadTerytUlicPoprawkiModel> logger)
        {
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        public void OnGet()
        {
            if (_isCompleted && _resultPoprawki != null && _resultTypyUlic != null)
            {
                InsertedCount = _resultPoprawki.InsertedCount;
                FoundCount = _resultTypyUlic.FoundCount;
                NotFoundCount = _resultTypyUlic.NotFoundCount;

                var appDataPath = _environment.ContentRootPath;
                LogFilePath = Path.Combine(appDataPath, "AppData", "Logs", "LoadTerytUlicPoprawki.txt");
                LogFilePathTypyUlic = Path.Combine(appDataPath, "AppData", "Logs", "LoadTypyUlic.txt");
            }
        }

        public IActionResult OnPostReset()
        {
            // Reset stanu - przygotowanie do ponownego uruchomienia
            _isRunning = false;
            _isCompleted = false;
            _totalCount = 0;
            _processedCount = 0;
            _currentOperation = string.Empty;
            _errorMessage = null;
            _stackTrace = null;
            _resultPoprawki = null;
            _resultTypyUlic = null;

            return RedirectToPage();
        }

        public IActionResult OnPost()
        {
            if (_isRunning)
            {
                ModelState.AddModelError(string.Empty, "Proces już się wykonuje. Proszę czekać.");
                return Page();
            }

            // Zresetuj stan
            _isRunning = true;
            _isCompleted = false;
            _totalCount = 0;
            _processedCount = 0;
            _currentOperation = "Uruchamianie...";
            _errorMessage = null;
            _stackTrace = null;
            _resultPoprawki = null;
            _resultTypyUlic = null;

            // Uruchom w tle
            _ = Task.Run(async () => await RunProcessAsync());

            return RedirectToPage();
        }

        private async Task RunProcessAsync()
        {
            LoadTerytUlicPoprawkiService? poprawkiLoader = null;
            var stopwatch = Stopwatch.StartNew();
            Dictionary<string, TerytUlicPoprawka>? sharedDictionary = null;

            try
            {
                _logger.LogInformation("=== Rozpoczęcie procesu ładowania słowników ulic w tle ===");
                _currentOperation = "Inicjalizacja...";

                var connectionString = _configuration.GetConnectionString("AddressDatabase");
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new InvalidOperationException("Connection string 'AddressDatabase' not found.");
                }

                var appDataPath = _environment.ContentRootPath;
                _logger.LogInformation($"AppDataPath: {appDataPath}");

                var db = new AddressDatabase(connectionString, appDataPath);
                var context = db.GetContext();

                // ========================================
                // KROK 1: Załaduj TerytUlicPoprawki
                // ========================================
                _logger.LogInformation("KROK 1: Ładowanie TerytUlicPoprawki...");
                _currentOperation = "[TerytUlicPoprawki] Inicjalizacja...";

                poprawkiLoader = new LoadTerytUlicPoprawkiService(context, appDataPath);

                var progressPoprawki = new Progress<LoadProgress>(p =>
                {
                    _currentOperation = $"[1/2] TerytUlicPoprawki: {p.CurrentOperation}";
                    _totalCount = p.TotalCount;
                    _processedCount = p.ProcessedCount;
                });

                _resultPoprawki = await poprawkiLoader.LoadAsync(progressPoprawki);
                _logger.LogInformation($"✓ KROK 1 zakończony. Wstawiono: {_resultPoprawki.InsertedCount}");

                if (!string.IsNullOrEmpty(_resultPoprawki.ErrorMessage))
                {
                    throw new Exception($"Błąd ładowania TerytUlicPoprawki: {_resultPoprawki.ErrorMessage}");
                }

                poprawkiLoader.Dispose();
                poprawkiLoader = null;

                // Wymuś garbage collection przed kolejnym krokiem
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                _logger.LogInformation("Czekanie 3 sekundy przed następnym krokiem...");
                await Task.Delay(3000);

                // ========================================
                // KROK 2: Załaduj słownik RAZ z pliku Excel
                // ========================================
                _logger.LogInformation("KROK 2: Ładowanie słownika TerytUlicPoprawki do pamięci...");
                _currentOperation = "[2/2] TypyUlic: Ładowanie słownika z Excel...";

                // Użyj tymczasowego loggera tylko do tego kroku
                var tempLogger = new PostalCodesLogger(appDataPath, "LoadTypyUlic.txt");
                await tempLogger.InitializeAsync();

                sharedDictionary = TerytUlicPoprawkiDictionary.Load(appDataPath, tempLogger);
                
                tempLogger.Dispose();

                if (sharedDictionary == null || sharedDictionary.Count == 0)
                {
                    throw new Exception("Nie udało się załadować słownika TerytUlicPoprawki z Excel");
                }

                _logger.LogInformation($"✓ Załadowano {sharedDictionary.Count} wpisów ze słownika do pamięci");

                // ========================================
                // KROK 3: Generuj TypyUlic używając załadowanego słownika
                // ========================================
                _logger.LogInformation("KROK 3: Generowanie TypyUlic...");
                _currentOperation = "[2/2] TypyUlic: Przetwarzanie...";

                _resultTypyUlic = await GenerateTypyUlicAsync(
                    context, 
                    appDataPath, 
                    sharedDictionary,
                    new Progress<ValidatorProgress>(p =>
                    {
                        _currentOperation = $"[2/2] TypyUlic: {p.CurrentOperation}";
                        _totalCount = p.TotalCount;
                        _processedCount = p.ProcessedCount;
                    })
                );

                _logger.LogInformation($"✓ KROK 3 zakończony. Found: {_resultTypyUlic.FoundCount}, NotFound: {_resultTypyUlic.NotFoundCount}");

                stopwatch.Stop();

                _currentOperation = $"✅ Zakończono pomyślnie w {stopwatch.Elapsed.TotalMinutes:F1} min";
                _isCompleted = true;

                _logger.LogInformation($"=== Proces zakończony pomyślnie w {stopwatch.Elapsed.TotalMinutes:F1} min ===");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                _logger.LogError(ex, "❌ Błąd podczas ładowania słowników ulic");

                _errorMessage = $"Błąd: {ex.Message}";
                _stackTrace = ex.StackTrace;

                // Loguj szczegółowy błąd z hierarchią InnerException
                var innerException = ex.InnerException;
                var depth = 1;
                while (innerException != null && depth < 5)
                {
                    _errorMessage += $"\n\n[Inner Exception {depth}]:\n{innerException.Message}";
                    _logger.LogError($"InnerException {depth}: {innerException.Message}");
                    innerException = innerException.InnerException;
                    depth++;
                }

                _currentOperation = $"❌ Błąd: {ex.Message}";
            }
            finally
            {
                try
                {
                    poprawkiLoader?.Dispose();
                    sharedDictionary?.Clear();
                    sharedDictionary = null;

                    // Wymuś garbage collection
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    _logger.LogInformation("Zasoby zwolnione");
                }
                catch (Exception disposeEx)
                {
                    _logger.LogError(disposeEx, "Błąd podczas zwalniania zasobów");
                }

                _isRunning = false;
            }
        }

        /// <summary>
        /// Generuje TypyUlic używając już załadowanego słownika (bez ponownego ładowania z Excel)
        /// </summary>
        private async Task<ValidatorResult> GenerateTypyUlicAsync(
            AddressDbContext context,
            string appDataPath,
            Dictionary<string, TerytUlicPoprawka> dictionary,
            IProgress<ValidatorProgress>? progress)
        {
            var logger = new PostalCodesLogger(appDataPath, "LoadTypyUlic.txt");
            await logger.InitializeAsync();

            var result = new ValidatorResult();

            try
            {
                logger.LogInfo("=== Rozpoczęcie generowania TypyUlic ===");

                // Pobierz dane z TerytUlic
                logger.LogInfo("Pobieranie danych z TerytUlic...");
                progress?.Report(new ValidatorProgress
                {
                    CurrentOperation = "Pobieranie danych z TerytUlic..."
                });

                var terytUlice = await context.TerytUlic
                    .Where(u => !string.IsNullOrEmpty(u.Nazwa1))
                    .AsNoTracking()
                    .ToListAsync();

                result.TotalCount = terytUlice.Count;
                logger.LogInfo($"Pobrano {result.TotalCount} wpisów z TerytUlic");

                progress?.Report(new ValidatorProgress
                {
                    CurrentOperation = $"Przetwarzanie {result.TotalCount} wpisów...",
                    TotalCount = result.TotalCount
                });

                var uliceList = new List<TerytUlicPoprawka>();

                // Przetwórz każdy wpis
                logger.LogInfo("Przetwarzanie wpisów...");
                foreach (var terytUlica in terytUlice)
                {
                    result.ProcessedCount++;

                    // Zbuduj klucz: Cecha + Nazwa2 + Nazwa1
                    var originalParts = new List<string>();

                    if (!string.IsNullOrWhiteSpace(terytUlica.Cecha))
                        originalParts.Add(terytUlica.Cecha.Trim());

                    if (!string.IsNullOrWhiteSpace(terytUlica.Nazwa2))
                        originalParts.Add(terytUlica.Nazwa2.Trim());

                    if (!string.IsNullOrWhiteSpace(terytUlica.Nazwa1))
                        originalParts.Add(terytUlica.Nazwa1.Trim());

                    var original = string.Join(" ", originalParts);

                    // Sprawdź czy wpis istnieje w słowniku
                    if (dictionary.TryGetValue(original, out var terytUlicPoprawka))
                    {
                        result.FoundCount++;
                        uliceList.Add(terytUlicPoprawka);
                    }
                    else
                    {
                        result.NotFoundCount++;
                        if (result.NotFoundCount <= 100) // Loguj tylko pierwsze 100
                        {
                            logger.LogWarning($"BRAK w słowniku: '{original}'");
                        }
                    }

                    // Raportuj postęp co 1000 wpisów
                    if (result.ProcessedCount % 1000 == 0 || result.ProcessedCount == result.TotalCount)
                    {
                        progress?.Report(new ValidatorProgress
                        {
                            CurrentOperation = $"Przetw: {result.ProcessedCount}/{result.TotalCount} | Znaleziono: {result.FoundCount} | Brak: {result.NotFoundCount}",
                            TotalCount = result.TotalCount,
                            ProcessedCount = result.ProcessedCount
                        });
                    }
                }

                logger.LogInfo($"Przetwarzanie zakończone. Found: {result.FoundCount}, NotFound: {result.NotFoundCount}");

                // Wstaw unikalne wartości do tabeli TypyUlic
                logger.LogInfo("Wstawianie unikalnych wartości do tabeli TypyUlic...");
                await InsertUniqueTypyUlicAsync(context, uliceList, logger, progress);

                logger.LogInfo("=== Podsumowanie ===");
                logger.LogInfo($"Przetworzono: {result.ProcessedCount}");
                logger.LogInfo($"Znaleziono: {result.FoundCount}");
                logger.LogInfo($"Brak: {result.NotFoundCount}");
                logger.LogInfo($"Procent pokrycia: {(result.FoundCount * 100.0 / result.TotalCount):F2}%");

                return result;
            }
            finally
            {
                logger.Dispose();
            }
        }

        private async Task InsertUniqueTypyUlicAsync(
            AddressDbContext context,
            List<TerytUlicPoprawka> uliceList,
            GeneralLogger logger,
            IProgress<ValidatorProgress>? progress)
        {
            // Usuń referencje z tabeli Ulice
            logger.LogInfo("Usuwanie referencji z tabeli Ulice...");
            await context.Database.ExecuteSqlRawAsync("UPDATE Ulice SET TypUlicyId = NULL WHERE TypUlicyId IS NOT NULL");

            // Wyczyść tabelę TypyUlic (zachowaj rekord -1)
            logger.LogInfo("Czyszczenie tabeli TypyUlic...");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM TypyUlic WHERE Id != -1");
            await context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('TypyUlic', RESEED, 0)");

            // Deduplikacja
            logger.LogInfo($"Deduplikacja {uliceList.Count} wpisów...");
            var uniqueUliceSet = new HashSet<TypUlicy>(new LocalTypUlicyEqualityComparer());

            var tytulyDict = new AddressLibrary.Dictionaries.TytulyStopnie.TytulyStopnieDictionary(context);
            await tytulyDict.GetDopelniaczToIdMappingAsync();

            foreach (var item in uliceList)
            {
                int tytulStopienId = tytulyDict.MapDopelniaczToId(item.Tytul);

                if (tytulStopienId == -2)
                {
                    _logger.LogError($"Brak tytułu [{item.Tytul}]");
                    tytulStopienId = -1;
                }

                var typUlicy = new TypUlicy
                {
                    Prefiks = item.Prefiks?.Length > 200 ? item.Prefiks.Substring(0, 200) : item.Prefiks,
                    TytulStopienId = tytulStopienId,
                    Imie = item.Imie?.Length > 200 ? item.Imie.Substring(0, 200) : item.Imie,
                    Imie2 = item.Imie2?.Length > 200 ? item.Imie2.Substring(0, 200) : item.Imie2,
                    Nazwisko = item.Nazwisko?.Length > 200 ? item.Nazwisko.Substring(0, 200) : item.Nazwisko,
                    Nazwisko2 = item.Nazwisko2?.Length > 200 ? item.Nazwisko2.Substring(0, 200) : item.Nazwisko2,
                    Pseudonim = item.Pseudonim?.Length > 200 ? item.Pseudonim.Substring(0, 200) : item.Pseudonim,
                    Postfiks = item.Postfiks?.Length > 200 ? item.Postfiks.Substring(0, 200) : item.Postfiks
                };

                uniqueUliceSet.Add(typUlicy);
            }

            var uniqueUlice = uniqueUliceSet.ToList();
            logger.LogInfo($"Znaleziono {uniqueUlice.Count} unikalnych wpisów");

            // Wstaw do bazy danych partiami
            const int batchSize = 500;
            int insertedCount = 0;

            for (int i = 0; i < uniqueUlice.Count; i += batchSize)
            {
                var batch = uniqueUlice.Skip(i).Take(batchSize).ToList();

                await context.TypyUlic.AddRangeAsync(batch);
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();

                insertedCount += batch.Count;

                logger.LogInfo($"✓ Wstawiono partię {i / batchSize + 1}: {insertedCount}/{uniqueUlice.Count}");

                progress?.Report(new ValidatorProgress
                {
                    CurrentOperation = $"Wstawiono {insertedCount}/{uniqueUlice.Count} unikalnych wpisów...",
                    TotalCount = uniqueUlice.Count,
                    ProcessedCount = insertedCount
                });
            }

            logger.LogInfo($"✓ Zakończono wstawianie: {insertedCount} unikalnych wpisów");
        }
    }

    /// <summary>
    /// Lokalny comparer do porównywania TypUlicy pod kątem unikalności (bez Id)
    /// </summary>
    internal class LocalTypUlicyEqualityComparer : IEqualityComparer<TypUlicy>
    {
        public bool Equals(TypUlicy? x, TypUlicy? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            return string.Equals(x.Prefiks, y.Prefiks, StringComparison.Ordinal) &&
                   x.TytulStopienId == y.TytulStopienId &&
                   string.Equals(x.Imie, y.Imie, StringComparison.Ordinal) &&
                   string.Equals(x.Imie2, y.Imie2, StringComparison.Ordinal) &&
                   string.Equals(x.Nazwisko, y.Nazwisko, StringComparison.Ordinal) &&
                   string.Equals(x.Nazwisko2, y.Nazwisko2, StringComparison.Ordinal) &&
                   string.Equals(x.Pseudonim, y.Pseudonim, StringComparison.Ordinal) &&
                   string.Equals(x.Postfiks, y.Postfiks, StringComparison.Ordinal);
        }

        public int GetHashCode(TypUlicy obj)
        {
            if (obj is null) return 0;

            return HashCode.Combine(
                obj.Prefiks ?? "",
                obj.TytulStopienId,
                obj.Imie ?? "",
                obj.Imie2 ?? "",
                obj.Nazwisko ?? "",
                obj.Nazwisko2 ?? "",
                HashCode.Combine(obj.Pseudonim ?? "", obj.Postfiks ?? "")
            );
        }
    }
}