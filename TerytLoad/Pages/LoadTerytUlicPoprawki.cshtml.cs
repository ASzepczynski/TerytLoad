using AddressLibrary.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;
using TerytLoad.Services;

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
        private static S³ownikUlicLoaderResult? _result = null;
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
            if (_isCompleted && _result != null)
            {
                InsertedCount = _result.InsertedCountPoprawki;
                FoundCount = _result.FoundTypyUlic;
                NotFoundCount = _result.NotFoundTypyUlic;

                var appDataPath = _environment.ContentRootPath;
                LogFilePath = Path.Combine(appDataPath, "AppData", "Logs", "LoadTerytUlicPoprawki.txt");
                LogFilePathTypyUlic = Path.Combine(appDataPath, "AppData", "Logs", "LoadTypyUlic.txt");
            }
        }

        public IActionResult OnPostReset()
        {
            _isRunning = false;
            _isCompleted = false;
            _totalCount = 0;
            _processedCount = 0;
            _currentOperation = string.Empty;
            _errorMessage = null;
            _stackTrace = null;
            _result = null;

            return RedirectToPage();
        }

        public IActionResult OnPost()
        {
            if (_isRunning)
            {
                ModelState.AddModelError(string.Empty, "Proces ju¿ siê wykonuje. Proszê czekaæ.");
                return Page();
            }

            _isRunning = true;
            _isCompleted = false;
            _totalCount = 0;
            _processedCount = 0;
            _currentOperation = "Uruchamianie...";
            _errorMessage = null;
            _stackTrace = null;
            _result = null;

            _ = Task.Run(async () => await RunProcessAsync());

            return RedirectToPage();
        }

        private async Task RunProcessAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _currentOperation = "Inicjalizacja...";

                var connectionString = _configuration.GetConnectionString("AddressDatabase");
                if (string.IsNullOrEmpty(connectionString))
                    throw new InvalidOperationException("Connection string 'AddressDatabase' not found.");

                var appDataPath = _environment.ContentRootPath;

                var service = new S³ownikUlicLoaderService();
                _result = await service.LoadAsync(
                    connectionString,
                    appDataPath,
                    new Progress<(string op, int current, int total)>(p =>
                    {
                        _currentOperation = p.op;
                        _totalCount = p.total;
                        _processedCount = p.current;
                    })
                );

                stopwatch.Stop();
                _currentOperation = $"? Zakoñczono pomyœlnie w {stopwatch.Elapsed.TotalMinutes:F1} min";
                _isCompleted = true;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _errorMessage = $"B³¹d: {ex.Message}";
                _stackTrace = ex.StackTrace;

                var innerException = ex.InnerException;
                var depth = 1;
                while (innerException != null && depth < 5)
                {
                    _errorMessage += $"\n\n[Inner Exception {depth}]:\n{innerException.Message}";
                    innerException = innerException.InnerException;
                    depth++;
                }
                _currentOperation = $"? B³¹d: {ex.Message}";
            }
            finally
            {
                _isRunning = false;
            }
        }
    }
}
