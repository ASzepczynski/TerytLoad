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
                var projectPath = _environment.ContentRootPath;
                var database = new AddressDatabase(connectionString, projectPath);

                Message = "🔍 Szukam pliku pna.pdf...\n";

                // Znajdź plik PDF w katalogu Teryt
                var terytPath = Path.Combine(projectPath, "AppData", "Teryt");

                if (!Directory.Exists(terytPath))
                {
                    Message += $"✗ Nie znaleziono folderu: {terytPath}\n";
                    return Page();
                }

                var pdfFiles = Directory.GetFiles(terytPath, "pna.pdf", SearchOption.TopDirectoryOnly);

                if (!pdfFiles.Any())
                {
                    Message += $"✗ Nie znaleziono pliku pna.pdf w folderze: {terytPath}\n";
                    return Page();
                }

                var pdfFile = pdfFiles.First();
                var fileName = Path.GetFileName(pdfFile);

                Message += $"✓ Znaleziono plik: {fileName}\n\n";

                // Wyczyść tabelę Pna
                Message += $"🗑️ Czyszczenie tabeli Pna...\n";
                await database.ClearTableAsync<Pna>();

                // Załaduj dane z PDF
                Message += $"⏳ Przetwarzanie pliku PDF...\n";
                await database.LoadDataFromPdfAsync(pdfFile);

                // Sprawdź ile rekordów zostało dodanych
                var context = database.GetContext();
                var count = context.Pna.Count();

                Message += $"\n{'=',-50}\n";
                Message += $"✅ SUKCES! Dodano {count} rekord(ów) do tabeli Pna\n";
            }
            catch (Exception ex)
            {
                Message = $"❌ BŁĄD podczas ładowania pliku PDF:\n{ex.Message}\n\nStack trace:\n{ex.StackTrace}";
            }

            return Page();
        }
    }
}