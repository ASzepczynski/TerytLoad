using AddressLibrary;
using AddressLibrary.Models;
using AddressLibrary.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace TerytLoad.Pages
{
    public class ExportToExcelModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        [BindProperty]
        public string SelectedTable { get; set; } = string.Empty;

        public List<SelectListItem> AvailableTables { get; set; } = new();
        public bool IsProcessing { get; set; }
        public bool ShowResults { get; set; }
        public string CurrentOperation { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public int ProcessedCount { get; set; }
        public string OutputFileName { get; set; } = string.Empty;
        public int ProgressPercentage => TotalCount > 0 ? (ProcessedCount * 100 / TotalCount) : 0;

        public ExportToExcelModel(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public void OnGet()
        {
            LoadAvailableTables();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(SelectedTable))
            {
                ModelState.AddModelError(string.Empty, "Wybierz tabelê do eksportu");
                LoadAvailableTables();
                return Page();
            }

            try
            {
                IsProcessing = true;

                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? throw new InvalidOperationException("Connection string 'AddressDatabase' not found.");

                var appDataPath = _environment.ContentRootPath;
                var outputPath = Path.Combine(appDataPath, "AppData", "Export");

                // Utwórz folder jeœli nie istnieje
                Directory.CreateDirectory(outputPath);

                var db = new AddressDatabase(connectionString, appDataPath);
                var context = db.GetContext();

                var exporter = new ExcelExportService();

                var progress = new Progress<ExportProgress>(p =>
                {
                    CurrentOperation = p.CurrentOperation;
                    TotalCount = p.TotalCount;
                    ProcessedCount = p.ProcessedCount;
                    if (p.IsCompleted)
                    {
                        OutputFileName = p.OutputFileName;
                    }
                });

                string filePath = string.Empty;

                // Wybierz odpowiedni¹ tabelê i eksportuj
                switch (SelectedTable)
                {
                    case "TerytSimc":
                        filePath = await exporter.ExportToExcelAsync(context.TerytSimc, outputPath, "TerytSimc", progress);
                        break;
                    case "TerytTerc":
                        filePath = await exporter.ExportToExcelAsync(context.TerytTerc, outputPath, "TerytTerc", progress);
                        break;
                    case "TerytUlic":
                        filePath = await exporter.ExportToExcelAsync(context.TerytUlic, outputPath, "TerytUlic", progress);
                        break;
                    case "TerytWmRodz":
                        filePath = await exporter.ExportToExcelAsync(context.TerytWmRodz, outputPath, "TerytWmRodz", progress);
                        break;
                    case "Pna":
                        filePath = await exporter.ExportToExcelAsync(context.Pna, outputPath, "Pna", progress);
                        break;
                    case "Wojewodztwa":
                        filePath = await exporter.ExportToExcelAsync(context.Wojewodztwa, outputPath, "Wojewodztwa", progress);
                        break;
                    case "Powiaty":
                        filePath = await exporter.ExportToExcelAsync(context.Powiaty, outputPath, "Powiaty", progress);
                        break;
                    case "Gminy":
                        filePath = await exporter.ExportToExcelAsync(context.Gminy, outputPath, "Gminy", progress);
                        break;
                    case "Miasta":
                        filePath = await exporter.ExportToExcelAsync(context.Miasta, outputPath, "Miasta", progress);
                        break;
                    case "Ulice":
                        filePath = await exporter.ExportToExcelAsync(context.Ulice, outputPath, "Ulice", progress);
                        break;
                    case "KodyPocztowe":
                        filePath = await exporter.ExportToExcelAsync(context.KodyPocztowe, outputPath, "KodyPocztowe", progress);
                        break;
                    case "Adresy":
                        filePath = await exporter.ExportToExcelAsync(context.Adresy, outputPath, "Adresy", progress);
                        break;
                    case "UrzedySkarbowe":
                        filePath = await exporter.ExportToExcelAsync(context.UrzedySkarbowe, outputPath, "UrzedySkarbowe", progress);
                        break;
                    case "TerytUlicPoprawki":
                        filePath = await exporter.ExportToExcelAsync(context.TerytUlicPoprawki, outputPath, "TerytUlicPoprawki", progress);
                        break;
                    case "TypyUlic":
                        filePath = await exporter.ExportToExcelAsync(context.TypyUlic, outputPath, "TypyUlic", progress);
                        break;
                    default:
                        throw new InvalidOperationException($"Nieznana tabela: {SelectedTable}");
                }

                // Poka¿ wyniki
                IsProcessing = false;
                ShowResults = true;
                CurrentOperation = "Zakoñczono eksport";

                return Page();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"B³¹d: {ex.Message}");
                IsProcessing = false;
                LoadAvailableTables();
                return Page();
            }
        }

        private void LoadAvailableTables()
        {
            AvailableTables = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "-- Wybierz tabelê --", Disabled = true, Selected = true },
                new SelectListItem { Value = "TerytSimc", Text = "TerytSimc - Miejscowoœci TERYT" },
                new SelectListItem { Value = "TerytTerc", Text = "TerytTerc - Podzia³ terytorialny" },
                new SelectListItem { Value = "TerytUlic", Text = "TerytUlic - Ulice TERYT" },
                new SelectListItem { Value = "TerytWmRodz", Text = "TerytWmRodz - Rodzaje miejscowoœci" },
                new SelectListItem { Value = "Pna", Text = "Pna - Punkty adresowe" },
                new SelectListItem { Value = "Wojewodztwa", Text = "Wojewodztwa - Hierarchia" },
                new SelectListItem { Value = "Powiaty", Text = "Powiaty - Hierarchia" },
                new SelectListItem { Value = "Gminy", Text = "Gminy - Hierarchia" },
                new SelectListItem { Value = "Miasta", Text = "Miasta - Hierarchia" },
                new SelectListItem { Value = "Ulice", Text = "Ulice - Hierarchia" },
                new SelectListItem { Value = "KodyPocztowe", Text = "KodyPocztowe - Kody pocztowe" },
                new SelectListItem { Value = "Adresy", Text = "Adresy - Adresy z ASIMS" },
                new SelectListItem { Value = "UrzedySkarbowe", Text = "UrzedySkarbowe - Urzêdy skarbowe" },
                new SelectListItem { Value = "TerytUlicPoprawki", Text = "TerytUlicPoprawki - S³ownik poprawek ulic" },
                new SelectListItem { Value = "TypyUlic", Text = "TypyUlic - Typy ulic osobowych" }
            };
        }
    }
}