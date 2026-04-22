using AddressLibrary;
using AddressLibrary.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TerytLoad.Pages
{
    public class VerifyTerytUlicPoprawkiExcelModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public bool ShowResults { get; set; }
        public int TotalCount { get; set; }
        public int ProcessedCount { get; set; }
        public int ErrorCount { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }

        public VerifyTerytUlicPoprawkiExcelModel(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? throw new InvalidOperationException("Connection string 'AddressDatabase' not found.");

                var appDataPath = _environment.ContentRootPath;

                var db = new AddressDatabase(connectionString, appDataPath);
                var context = db.GetContext();

                var progress = new Progress<string>(msg =>
                    System.Diagnostics.Debug.WriteLine($"[VerifyTerytUlicPoprawkiExcel] {msg}"));

                using var service = new VerifyTerytUlicPoprawkiExcelService(context, appDataPath);
                var result = await service.VerifyAsync(progress);

                ShowResults = true;
                TotalCount = result.TotalCount;
                ProcessedCount = result.ProcessedCount;
                ErrorCount = result.ErrorCount;
                OutputPath = result.OutputPath;

                return Page();
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                ShowResults = false;
                return Page();
            }
        }
    }
}
