using AddressLibrary;
using AddressLibrary.Models;
using AddressLibrary.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Collections.Concurrent;
using System.Text;
using TerytLoad.Configuration;

namespace TerytLoad.Pages
{
    public class LoadPnaCsvModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        [BindProperty]
        public string Message { get; set; } = string.Empty;

        public LoadPnaCsvModel(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostLoadCsvAsync()
        {
            var messageBuilder = new StringBuilder();

            try
            {
                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? DatabaseConfig.DefaultConnectionString;

                var projectPath = _environment.ContentRootPath;
                var database = new AddressDatabase(connectionString, projectPath);

                messageBuilder.AppendLine("🔍 Szukam pliku spispna-cz1.txt...");

                // Znajdź plik CSV w katalogu AppData/pna
                var pnaPath = Path.Combine(projectPath, "AppData", "pna");

                if (!Directory.Exists(pnaPath))
                {
                    messageBuilder.AppendLine($"✗ Nie znaleziono folderu: {pnaPath}");
                    Message = messageBuilder.ToString();
                    return Page();
                }

                var csvFile = Path.Combine(pnaPath, "spispna-cz1.txt");

                if (!System.IO.File.Exists(csvFile))
                {
                    messageBuilder.AppendLine($"✗ Nie znaleziono pliku: {csvFile}");
                    Message = messageBuilder.ToString();
                    return Page();
                }

                var fileName = Path.GetFileName(csvFile);
                var fileInfo = new FileInfo(csvFile);
                messageBuilder.AppendLine($"✓ Znaleziono plik: {fileName}");
                messageBuilder.AppendLine($"  Rozmiar: {fileInfo.Length / 1024} KB");
                messageBuilder.AppendLine($"  Data modyfikacji: {fileInfo.LastWriteTime}");
                messageBuilder.AppendLine();

                // Wyczyść tabelę Pna
                messageBuilder.AppendLine("🗑️ Czyszczenie tabeli Pna...");
                await database.ClearTableAsync<Pna>();
                messageBuilder.AppendLine("✓ Tabela wyczyszczona");
                messageBuilder.AppendLine();

                // Załaduj dane z CSV
                messageBuilder.AppendLine("⏳ Ładowanie danych z pliku CSV (kodowanie CP-1250)...");
                messageBuilder.AppendLine();

                var context = database.GetContext();
                var loader = new PnaCsvLoader(context, projectPath);

                // Użyj ConcurrentBag zamiast List dla thread-safety
                var progressMessages = new ConcurrentBag<string>();
                var progress = new Progress<PnaCsvLoader.LoadProgressInfo>(info =>
                {
                    progressMessages.Add(info.CurrentAction);
                });

                await loader.LoadFromCsvAsync(csvFile, progress);

                // Dodaj komunikaty postępu - bezpieczna iteracja
                foreach (var msg in progressMessages.ToList())
                {
                    messageBuilder.AppendLine(msg);
                    messageBuilder.AppendLine();
                }

                // Sprawdź ile rekordów zostało dodanych
                var count = context.Pna.Count();

                messageBuilder.AppendLine(new string('=', 50));
                if (count > 0)
                {
                    messageBuilder.AppendLine($"✅ SUKCES! Dodano {count} rekord(ów) do tabeli Pna");
                }
                else
                {
                    messageBuilder.AppendLine($"⚠️ OSTRZEŻENIE! Dodano {count} rekordów do tabeli Pna");
                    messageBuilder.AppendLine("Sprawdź format pliku CSV i logi powyżej.");
                }

                Message = messageBuilder.ToString();
            }
            catch (Exception ex)
            {
                messageBuilder.Clear();
                messageBuilder.AppendLine("❌ BŁĄD podczas ładowania pliku CSV:");
                messageBuilder.AppendLine(ex.Message);
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("Stack trace:");
                messageBuilder.AppendLine(ex.StackTrace);

                if (ex.InnerException != null)
                {
                    messageBuilder.AppendLine();
                    messageBuilder.AppendLine("Inner exception:");
                    messageBuilder.AppendLine(ex.InnerException.Message);
                }

                Message = messageBuilder.ToString();
            }

            return Page();
        }
    }
}