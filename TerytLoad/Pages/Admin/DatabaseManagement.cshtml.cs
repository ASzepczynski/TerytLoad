using AddressLibrary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TerytLoad.Pages.Admin
{
    public class DatabaseManagementModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public DatabaseManagementModel(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public bool CanConnect { get; set; }
        public string? Message { get; set; }

        public async Task OnGetAsync()
        {
            var connectionString = _configuration.GetConnectionString("AddressDatabase");
            var database = new AddressDatabase(connectionString!, _environment.ContentRootPath);
            CanConnect = await database.CanConnectToDatabaseAsync();
        }

        public async Task<IActionResult> OnPostRecreateAsync()
        {
            var connectionString = _configuration.GetConnectionString("AddressDatabase");
            var database = new AddressDatabase(connectionString!, _environment.ContentRootPath);

            try
            {
                await database.ManualRecreateDatabaseAsync();
                Message = "✓ Baza danych została odtworzona pomyślnie.";
            }
            catch (Exception ex)
            {
                Message = $"❌ Błąd: {ex.Message}";
            }

            return RedirectToPage();
        }
    }
}