using AddressLibrary;
using AddressLibrary.Helpers;
using AddressLibrary.Models;
using AddressLibrary.Services.AddressSearch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text;
using TerytLoad.Configuration;

namespace TerytLoad.Pages
{
    public class ProcessTaxOfficesModel : PageModel
    {
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;

        [BindProperty]
        public string Message { get; set; } = string.Empty;

        public ProcessStats? Stats { get; set; }

        public ProcessTaxOfficesModel(IWebHostEnvironment environment, IConfiguration configuration)
        {
            _environment = environment;
            _configuration = configuration;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostProcessAsync()
        {
            var excelFilePath = Directories.GetExcelFilePath("UrzedySkarbowe.xlsx");

            try
            {
                var messageBuilder = new StringBuilder();
                messageBuilder.AppendLine($"?? Plik Ÿród³owy: {excelFilePath}");
                messageBuilder.AppendLine($"?? Rozpoczynam przetwarzanie...\n");

                // Wczytaj Excel przez ExcelTableReader
                var rows = ExcelTableReader.Read(excelFilePath);
                messageBuilder.AppendLine($"?? Wczytano {rows.Count} wierszy danych");

                // Po³¹czenie z baz¹ danych
                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? DatabaseConfig.DefaultConnectionString;

                var appDataPath = _environment.ContentRootPath;
                var database = new AddressDatabase(connectionString, appDataPath);
                var context = database.GetContext();

                await context.Database.EnsureCreatedAsync();

                messageBuilder.AppendLine($"?? Inicjalizujê AddressSearchService...");
                var searchService = new AddressSearchService(context, appDataPath);
                await searchService.ReinitializeAsync();
                messageBuilder.AppendLine($"? AddressSearchService zainicjalizowany\n");

                // Wyczyœæ istniej¹ce dane
                var existingCount = await context.UrzedySkarbowe.CountAsync();
                if (existingCount > 0)
                {
                    messageBuilder.AppendLine($"?? Usuwam {existingCount} istniej¹cych rekordów...");
                    context.UrzedySkarbowe.RemoveRange(context.UrzedySkarbowe);
                    await context.SaveChangesAsync();
                    messageBuilder.AppendLine($"   ? Usuniêto\n");
                }

                int savedCount = 0;
                int matchedStreets = 0;
                const int batchSize = 100;

                for (int i = 0; i < rows.Count; i += batchSize)
                {
                    var batch = rows.Skip(i).Take(batchSize);

                    foreach (var row in batch)
                    {
                        var nazwa  = row.GetString("Nazwa");
                        var kod    = row.GetString("Kod");
                        var miasto = row.GetString("Miasto");
                        var ulica  = row.GetString("Ulica");
                        var nrDomu = row.GetString("NrDomu");
                        var email  = row["Email"];
                        var www    = row["WWW"];
                        var zasieg = row["Zasiêg"];

                        // Spróbuj dopasowaæ UlicaId
                        int ulicaId = -1;
                        if (!string.IsNullOrWhiteSpace(miasto) && !string.IsNullOrWhiteSpace(ulica))
                        {
                            if (miasto == "Augustów")
                            {
                                int z = 1;
                            }
                            var searchResult = await searchService.SearchAsync(new AddressSearchRequest
                            {
                                KodPocztowy = kod,
                                Miasto      = miasto,
                                Ulica       = ulica,
                                NumerDomu   = nrDomu
                            });

                            if (searchResult.Status == AddressSearchStatus.Success && searchResult.Ulica != null)
                            {
                                ulicaId = searchResult.Ulica.Id;
                                matchedStreets++;
                            }
                        }

                        context.UrzedySkarbowe.Add(new UrzadSkarbowy
                        {
                            Nazwa   = nazwa,
                            Kod     = kod,
                            Miasto  = miasto,
                            Ulica   = ulica,
                            NrDomu  = nrDomu,
                            UlicaId = ulicaId,
                            Email   = email ?? string.Empty,
                            Www     = www ?? string.Empty,
                            Zasieg  = zasieg ?? string.Empty
                        });
                    }

                    await context.SaveChangesAsync();
                    savedCount += batch.Count();
                    messageBuilder.AppendLine($"   ? Zapisano {savedCount}/{rows.Count} rekordów (dopasowano ulic: {matchedStreets})");
                }

                messageBuilder.AppendLine($"\n? Import zakoñczony pomyœlnie!");
                messageBuilder.AppendLine($"   • Wczytanych wierszy: {rows.Count}");
                messageBuilder.AppendLine($"   • Zapisanych do bazy: {savedCount}");
                messageBuilder.AppendLine($"   • Dopasowanych ulic (UlicaId): {matchedStreets} ({(rows.Count > 0 ? (double)matchedStreets / rows.Count * 100 : 0):F1}%)");

                // Próbka danych
                messageBuilder.AppendLine($"\n?? Przyk³adowe dane (pierwsze 5 urzêdów):");
                var sample = await context.UrzedySkarbowe.Take(5).ToListAsync();
                foreach (var office in sample)
                {
                    messageBuilder.AppendLine($"\n?? {office.Nazwa}");
                    messageBuilder.AppendLine($"   ?? {office.Kod} {office.Miasto}, {office.Ulica} {office.NrDomu}");
                    messageBuilder.AppendLine($"   ?? UlicaId: {office.UlicaId}");
                    messageBuilder.AppendLine($"   ??  {office.Email}  ?? {office.Www}");
                    messageBuilder.AppendLine($"   ?? Zasiêg: {office.Zasieg}");
                }

                Message = messageBuilder.ToString();
                Stats = new ProcessStats
                {
                    TotalRows        = rows.Count,
                    ProcessedRecords = rows.Count,
                    SavedRecords     = savedCount,
                    MatchedStreets   = matchedStreets
                };
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null
                    ? $"\n\nInner Exception:\n{ex.InnerException.Message}"
                    : string.Empty;

                Message = $"? B£¥D podczas przetwarzania pliku:{Environment.NewLine}{ex.Message}{innerMsg}{Environment.NewLine}{Environment.NewLine}Stack trace:{Environment.NewLine}{ex.StackTrace}";
            }

            return Page();
        }

        public class ProcessStats
        {
            public int TotalRows { get; set; }
            public int ProcessedRecords { get; set; }
            public int SavedRecords { get; set; }
            public int MatchedStreets { get; set; }
        }
    }
}
