using AddressLibrary;
using AddressLibrary.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.IO.Compression;
using TerytLoad.Configuration;

namespace TerytLoad.Pages
{
    public class DatabaseModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        
        [BindProperty]
        public string Message { get; set; } = string.Empty;

        public DatabaseModel(IConfiguration configuration, IWebHostEnvironment environment)
        {
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
                var database = new AddressDatabase(connectionString,appDataPath);

                Message = "🚀 Rozpoczęto ładowanie danych TERYT...{Environment.NewLine}";

                // Znajdź folder z plikami
                var projectPath = _environment.ContentRootPath;
                var terytPath = Path.Combine(projectPath, "AppData", "Teryt");

                if (!Directory.Exists(terytPath))
                {
                    Message += $"✗ Nie znaleziono folderu: {terytPath}{Environment.NewLine}";
                    return Page();
                }

                Message += $"✓ Znaleziono folder: {terytPath}{Environment.NewLine}";

                // Znajdź wszystkie pliki ZIP
                var allZipFiles = Directory.GetFiles(terytPath, "*.zip").ToList();
                
                if (!allZipFiles.Any())
                {
                    Message += $"✗ Nie znaleziono plików ZIP w folderze";
                    return Page();
                }

                Message += $"✓ Znaleziono {allZipFiles.Count} plik(ów) ZIP{Environment.NewLine}{Environment.NewLine}";

                var loadedFiles = new List<string>();
                var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                try
                {
                    foreach (var zipFile in allZipFiles)
                    {
                        var zipFileName = Path.GetFileName(zipFile);
                        var hasUrzedowy = zipFileName.Contains("Urzedowy", StringComparison.OrdinalIgnoreCase);

                        Message += $"📦 Rozpakowywanie: {zipFileName}{Environment.NewLine}";

                        // Rozpakuj ZIP
                        var extractPath = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(zipFile));
                        ZipFile.ExtractToDirectory(zipFile, extractPath);

                        // Znajdź pliki CSV
                        var csvFiles = Directory.GetFiles(extractPath, "*.csv", SearchOption.AllDirectories);
                        Message += $"   Znaleziono {csvFiles.Length} plik(ów) CSV{Environment.NewLine}";

                        foreach (var csvFile in csvFiles)
                        {
                            var fileName = Path.GetFileName(csvFile).ToUpper();

                            if (fileName.Contains("SIMC") && hasUrzedowy)
                            {
                                Message += $"   🗑️ Czyszczenie tabeli TerytSimc...{Environment.NewLine}";
                                await database.ClearTableAsync<TerytSimc>();
                                
                                Message += $"   ⏳ Ładowanie SIMC: {Path.GetFileName(csvFile)}...{Environment.NewLine}";
                                await database.LoadDataFromCsvAsync<TerytSimc>(csvFile);
                                loadedFiles.Add($"✓ SIMC: {Path.GetFileName(csvFile)}");
                            }
                            else if (fileName.Contains("TERC") && hasUrzedowy)
                            {
                                Message += $"   🗑️ Czyszczenie tabeli TerytTerc...{Environment.NewLine}";
                                await database.ClearTableAsync<TerytTerc>();
                                
                                Message += $"   ⏳ Ładowanie TERC: {Path.GetFileName(csvFile)}...{Environment.NewLine}";
                                await database.LoadDataFromCsvAsync<TerytTerc>(csvFile);
                                loadedFiles.Add($"✓ TERC: {Path.GetFileName(csvFile)}");
                            }
                            else if (fileName.Contains("ULIC") && hasUrzedowy)
                            {
                                Message += $"   🗑️ Czyszczenie tabeli TerytUlic...{Environment.NewLine}";
                                await database.ClearTableAsync<TerytUlic>();
                                
                                Message += $"   ⏳ Ładowanie ULIC: {Path.GetFileName(csvFile)}...{Environment.NewLine}";
                                await database.LoadDataFromCsvAsync<TerytUlic>(csvFile);
                                loadedFiles.Add($"✓ ULIC: {Path.GetFileName(csvFile)}");
                            }
                            else if (fileName.Contains("WMRODZ"))
                            {
                                Message += $"   🗑️ Czyszczenie tabeli TerytWmRodz...{Environment.NewLine}";
                                await database.ClearTableAsync<TerytWmRodz>();
                                
                                Message += $"   ⏳ Ładowanie WMRODZ: {Path.GetFileName(csvFile)}...{Environment.NewLine}";
                                await database.LoadDataFromCsvAsync<TerytWmRodz>(csvFile);
                                loadedFiles.Add($"✓ WMRODZ: {Path.GetFileName(csvFile)}");
                            }
                        }
                    }

                    Message += $"{Environment.NewLine}{'=',-50}{Environment.NewLine}";

                    if (loadedFiles.Any())
                    {
                        Message += $"✅ SUKCES! Załadowano {loadedFiles.Count} plików:{Environment.NewLine}{Environment.NewLine}";
                        Message += string.Join("{Environment.NewLine}", loadedFiles);
                    }
                    else
                    {
                        Message += "⚠️ Nie znaleziono rozpoznawalnych plików CSV (SIMC, TERC, ULIC, WMRODZ)";
                    }
                }
                finally
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                Message = $"❌ BŁĄD podczas ładowania plików:{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}Stack trace:{Environment.NewLine}{ex.StackTrace}";
            }

            return Page();
        }
    }
}