using AddressLibrary;
using AddressLibrary.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TerytLoad.Pages
{
    public class LoadTerytUlicPoprawkiModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public bool IsProcessing { get; set; }
        public bool ShowResults { get; set; }
        public string CurrentOperation { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public int InsertedCount { get; set; }
        public string LogFilePath { get; set; } = string.Empty;
        public int ProgressPercentage => TotalCount > 0 ? (ProcessedCount * 100 / TotalCount) : 0;
        private int ProcessedCount { get; set; }

        public LoadTerytUlicPoprawkiModel(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public void OnGet()
        {
            // Strona startowa
        }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                IsProcessing = true;

                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? throw new InvalidOperationException("Connection string 'AddressDatabase' not found.");

                var appDataPath = _environment.ContentRootPath;

                var db = new AddressDatabase(connectionString, appDataPath);
                var context = db.GetContext();

                var loader = new LoadTerytUlicPoprawkiService(context, appDataPath);

                var progress = new Progress<LoadProgress>(p =>
                {
                    CurrentOperation = p.CurrentOperation;
                    TotalCount = p.TotalCount;
                    ProcessedCount = p.ProcessedCount;
                });

                var result = await loader.LoadAsync(progress);

                // Poka¿ wyniki
                IsProcessing = false;
                ShowResults = true;
                CurrentOperation = "Zakoñczono ³adowanie";
                TotalCount = result.TotalCount;
                InsertedCount = result.InsertedCount;
                LogFilePath = Path.Combine(appDataPath, "AppData", "Logs", "LoadTerytUlicPoprawki.txt");

                loader.Dispose();

                return Page();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"B³¹d: {ex.Message}");
                IsProcessing = false;
                return Page();
            }
        }
    }
}