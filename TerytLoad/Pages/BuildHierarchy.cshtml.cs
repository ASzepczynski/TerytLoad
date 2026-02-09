using AddressLibrary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text;
using TerytLoad.Configuration;

namespace TerytLoad.Pages
{
    public class BuildHierarchyModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        [BindProperty]
        public string Message { get; set; } = string.Empty;

        public BuildHierarchyModel(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostBuildAsync()
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? DatabaseConfig.DefaultConnectionString;

                var appDataPath = _environment.ContentRootPath;
                var database = new AddressDatabase(connectionString, appDataPath);

                var messageBuilder = new StringBuilder();
                messageBuilder.AppendLine($"⏳ Rozpoczynam budowanie struktury hierarchicznej...{Environment.NewLine}");
                messageBuilder.AppendLine($"ℹ️ Kody pocztowe NIE są ładowane w tym kroku.");
                messageBuilder.AppendLine($"   Użyj osobnej strony 'Kody pocztowe' aby je załadować.{Environment.NewLine}");

                // Buduj strukturę hierarchiczną BEZ kodów pocztowych
                await database.BuildHierarchicalStructureAsync();

                var context = database.GetContext();
                var wojCount = context.Wojewodztwa.Count(w => w.Id != -1);
                var powCount = context.Powiaty.Count(p => p.Id != -1);
                var gmCount = context.Gminy.Count(g => g.Id != -1);
                var mjsCount = context.Miasta.Count(m => m.Id != -1);
                var ulCount = context.Ulice.Count(u => u.Id != -1);

                messageBuilder.AppendLine($"✅ SUKCES! Utworzono strukturę hierarchiczną:{Environment.NewLine}");
                messageBuilder.AppendLine($"✓ Województw: {wojCount}");
                messageBuilder.AppendLine($"✓ Powiatów: {powCount}");
                messageBuilder.AppendLine($"✓ Gmin: {gmCount}");
                messageBuilder.AppendLine($"✓ Miejscowości: {mjsCount}");
                messageBuilder.AppendLine($"✓ Ulic: {ulCount}{Environment.NewLine}");
                
                messageBuilder.AppendLine($"📄 Log kontrolny zapisany{Environment.NewLine}");
                messageBuilder.AppendLine($"⚠️ Aby załadować kody pocztowe, przejdź do strony 'Kody pocztowe'");

                Message = messageBuilder.ToString();
            }
            catch (Exception ex)
            {
                var messageBuilder = new StringBuilder();
                messageBuilder.AppendLine("❌ BŁĄD:");
                messageBuilder.AppendLine(ex.Message);
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("Stack trace:");
                messageBuilder.AppendLine(ex.StackTrace);
                
                // DODANO: Obsługa inner exception
                if (ex.InnerException != null)
                {
                    messageBuilder.AppendLine();
                    messageBuilder.AppendLine("=== INNER EXCEPTION ===");
                    messageBuilder.AppendLine(ex.InnerException.Message);
                    messageBuilder.AppendLine();
                    messageBuilder.AppendLine("Inner Stack trace:");
                    messageBuilder.AppendLine(ex.InnerException.StackTrace);
                    
                    // Jeśli jest jeszcze głębsza inner exception
                    if (ex.InnerException.InnerException != null)
                    {
                        messageBuilder.AppendLine();
                        messageBuilder.AppendLine("=== INNER INNER EXCEPTION ===");
                        messageBuilder.AppendLine(ex.InnerException.InnerException.Message);
                    }
                }
                
                Message = messageBuilder.ToString();
            }

            return Page();
        }
    }
}