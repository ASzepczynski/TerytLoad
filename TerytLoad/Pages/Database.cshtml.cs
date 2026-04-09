using AddressLibrary;
using AddressLibrary.Models;
using AddressLibrary.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using System.IO.Compression;
using TerytLoad.Configuration;
using TerytLoad.Hubs;

namespace TerytLoad.Pages
{
    public class DatabaseModel : PageModel
    {
        private readonly IHubContext<ProgressHub> _hubContext;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        [BindProperty]
        public string Message { get; set; } = string.Empty;

        public DatabaseModel(
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

        public async Task<IActionResult> OnPostDeleteDatabaseAsync()
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? DatabaseConfig.DefaultConnectionString;

                var appDataPath = _environment.ContentRootPath;
                var database = new AddressDatabase(connectionString, appDataPath);
                await database.DeleteDatabaseAsync();

                Message = "✓ Baza danych została usunięta pomyślnie!";
            }
            catch (Exception ex)
            {
                Message = $"❌ BŁĄD podczas usuwania bazy danych:{Environment.NewLine}{ex.Message}";
            }

            return Page();
        }

        public async Task<IActionResult> OnPostCreateAndLoadAsync()
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? DatabaseConfig.DefaultConnectionString;
                var appDataPath = _environment.ContentRootPath;
                var database = new AddressDatabase(connectionString, appDataPath);

                var terytPath = Path.Combine(appDataPath, "AppData", "Teryt");

                if (!Directory.Exists(terytPath))
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveProgress", "load-teryt", 0, 1,
                        $"❌ Nie znaleziono folderu: {terytPath}");
                    Message = $"Nie znaleziono folderu: {terytPath}";
                    return Page();
                }

                var allZipFiles = Directory.GetFiles(terytPath, "*.zip").ToList();

                if (!allZipFiles.Any())
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveProgress", "load-teryt", 0, 1,
                        "❌ Nie znaleziono plików ZIP w folderze.");
                    Message = "Nie znaleziono plików ZIP w folderze.";
                    return Page();
                }

                // Policz łączną liczbę plików CSV do załadowania
                var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                // Krok 1 — informacja startowa
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "load-teryt", 0, 100,
                    $"Znaleziono {allZipFiles.Count} plik(ów) ZIP. Rozpakowywanie...");

                var loadedFiles = new List<string>();
                int totalFiles = 0;
                int doneFiles = 0;

                // Policz najpierw ile plików CSV będziemy ładować
                foreach (var zipFile in allZipFiles)
                {
                    var countPath = Path.Combine(tempDir, "count_" + Path.GetFileNameWithoutExtension(zipFile));
                    Directory.CreateDirectory(countPath);
                    ZipFile.ExtractToDirectory(zipFile, countPath);
                    var csvFiles = Directory.GetFiles(countPath, "*.csv", SearchOption.AllDirectories);
                    var hasUrzedowy = Path.GetFileName(zipFile).Contains("Urzedowy", StringComparison.OrdinalIgnoreCase);
                    foreach (var csv in csvFiles)
                    {
                        var fn = Path.GetFileName(csv).ToUpper();
                        if ((fn.Contains("SIMC") || fn.Contains("TERC") || fn.Contains("ULIC")) && hasUrzedowy)
                            totalFiles++;
                        else if (fn.Contains("WMRODZ"))
                            totalFiles++;
                    }
                }

                if (totalFiles == 0)
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveProgress", "load-teryt", 0, 1,
                        "❌ Nie znaleziono rozpoznawalnych plików CSV (SIMC, TERC, ULIC, WMRODZ).");
                    Directory.Delete(tempDir, true);
                    Message = "Nie znaleziono rozpoznawalnych plików CSV.";
                    return Page();
                }

                try
                {
                    foreach (var zipFile in allZipFiles)
                    {
                        var zipFileName = Path.GetFileName(zipFile);
                        var hasUrzedowy = zipFileName.Contains("Urzedowy", StringComparison.OrdinalIgnoreCase);
                        var extractPath = Path.Combine(tempDir, "count_" + Path.GetFileNameWithoutExtension(zipFile));

                        var csvFiles = Directory.GetFiles(extractPath, "*.csv", SearchOption.AllDirectories);

                        foreach (var csvFile in csvFiles)
                        {
                            var fileName = Path.GetFileName(csvFile).ToUpper();
                            string? tableLabel = null;

                            if (fileName.Contains("SIMC") && hasUrzedowy)
                            {
                                tableLabel = "SIMC";
                                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "load-teryt",
                                    doneFiles, totalFiles, $"🗑️ Czyszczenie tabeli TerytSimc...");
                                await database.ClearTableAsync<TerytSimc>();
                            }
                            else if (fileName.Contains("TERC") && hasUrzedowy)
                            {
                                tableLabel = "TERC";
                                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "load-teryt",
                                    doneFiles, totalFiles, $"🗑️ Czyszczenie tabeli TerytTerc...");
                                await database.ClearTableAsync<TerytTerc>();
                            }
                            else if (fileName.Contains("ULIC") && hasUrzedowy)
                            {
                                tableLabel = "ULIC";
                                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "load-teryt",
                                    doneFiles, totalFiles, $"🗑️ Czyszczenie tabeli TerytUlic...");
                                await database.ClearTableAsync<TerytUlic>();
                            }
                            else if (fileName.Contains("WMRODZ"))
                            {
                                tableLabel = "WMRODZ";
                                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "load-teryt",
                                    doneFiles, totalFiles, $"🗑️ Czyszczenie tabeli TerytWmRodz...");
                                await database.ClearTableAsync<TerytWmRodz>();
                            }

                            if (tableLabel == null)
                                continue;

                            var progress = new Progress<LoadProgress>(async p =>
                            {
                                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "load-teryt",
                                    doneFiles, totalFiles,
                                    $"⏳ {tableLabel}: {p.CurrentOperation}");
                            });

                            if (tableLabel == "SIMC")
                                await database.LoadDataFromCsvAsync<TerytSimc>(csvFile, progress);
                            else if (tableLabel == "TERC")
                                await database.LoadDataFromCsvAsync<TerytTerc>(csvFile, progress);
                            else if (tableLabel == "ULIC")
                                await database.LoadDataFromCsvAsync<TerytUlic>(csvFile, progress);
                            else if (tableLabel == "WMRODZ")
                                await database.LoadDataFromCsvAsync<TerytWmRodz>(csvFile, progress);

                            doneFiles++;
                            loadedFiles.Add($"✓ {tableLabel}: {Path.GetFileName(csvFile)}");

                            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "load-teryt",
                                doneFiles, totalFiles,
                                $"✓ Załadowano {tableLabel}: {Path.GetFileName(csvFile)}");
                        }
                    }

                    var summary = $"✅ SUKCES! Załadowano {loadedFiles.Count} plików:{Environment.NewLine}{Environment.NewLine}" +
                                  string.Join(Environment.NewLine, loadedFiles);

                    await _hubContext.Clients.All.SendAsync("ReceiveProgress", "load-teryt",
                        totalFiles, totalFiles, summary);

                    Message = summary;
                }
                finally
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"❌ BŁĄD podczas ładowania plików:{Environment.NewLine}{ex.Message}";
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "load-teryt", 0, 1, errorMsg);
                Message = errorMsg;
            }

            return Page();
        }
    }
}