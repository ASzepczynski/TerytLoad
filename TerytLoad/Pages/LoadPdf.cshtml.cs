using AddressLibrary;
using AddressLibrary.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TerytLoad.Configuration;

namespace TerytLoad.Pages
{
    public class LoadPdfModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        [BindProperty]
        public string Message { get; set; } = string.Empty;

        public LoadPdfModel(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostLoadPdfAsync()
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? DatabaseConfig.DefaultConnectionString;

                // ZMIANA: Przekaż ContentRootPath jako appDataPath
                var appDataPath = _environment.ContentRootPath;
                var database = new AddressDatabase(connectionString, appDataPath);

                Message = "🔍 Szukam pliku pna.pdf...{Environment.NewLine}";

                // Znajdź plik PDF w katalogu Teryt
                var terytPath = Path.Combine(appDataPath, "AppData", "Teryt");

                if (!Directory.Exists(terytPath))
                {
                    Message += $"✗ Nie znaleziono folderu: {terytPath}{Environment.NewLine}";
                    return Page();
                }

                var pdfFiles = Directory.GetFiles(terytPath, "pna.pdf", SearchOption.TopDirectoryOnly);

                if (!pdfFiles.Any())
                {
                    Message += $"✗ Nie znaleziono pliku pna.pdf w folderze: {terytPath}{Environment.NewLine}";
                    return Page();
                }

                var pdfFile = pdfFiles.First();
                var fileName = Path.GetFileName(pdfFile);

                Message += $"✓ Znaleziono plik: {fileName}{Environment.NewLine}{Environment.NewLine}";

                // Wyczyść tabelę Pna
                Message += $"🗑️ Czyszczenie tabeli Pna...{Environment.NewLine}";
                await database.ClearTableAsync<Pna>();

                // Załaduj dane z PDF
                Message += $"⏳ Przetwarzanie pliku PDF...{Environment.NewLine}";
                await database.LoadDataFromPdfAsync(pdfFile);

                // Sprawdź ile rekordów zostało dodanych
                var context = database.GetContext();
                var count = context.Pna.Count();

                Message += $"{Environment.NewLine}{'=',-50}{Environment.NewLine}";
                Message += $"✅ SUKCES! Dodano {count} rekord(ów) do tabeli Pna{Environment.NewLine}";
            }
            catch (Exception ex)
            {
                Message = $"❌ BŁĄD podczas ładowania pliku PDF:{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}Stack trace:{Environment.NewLine}{ex.StackTrace}";
            }

            return Page();
        }
    }
}