using AddressLibrary;
using AddressLibrary.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TerytLoad.Configuration;

namespace TerytLoad.Pages
{
    public class BrowseTerytModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public List<TerytTerc> Wojewodztwa { get; set; } = new();
        public List<TerytTerc> Powiaty { get; set; } = new();
        public List<TerytTerc> Gminy { get; set; } = new();
        public List<TerytSimc> Miejscowosci { get; set; } = new();
        public List<TerytUlic> Ulice { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? WojKod { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? PowKod { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? GmiKod { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? MiejSymbol { get; set; }

        public string CurrentPath { get; set; } = string.Empty;

        public BrowseTerytModel(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public async Task OnGetAsync()
        {
            var connectionString = _configuration.GetConnectionString("AddressDatabase")
                ?? DatabaseConfig.DefaultConnectionString;

            var appDataPath = _environment.ContentRootPath;
            var database = new AddressDatabase(connectionString, appDataPath);
            var context = database.GetContext();

            // Zawsze ³aduj województwa (tabela TERYT_TERC)
            Wojewodztwa = await context.TerytTerc
                .Where(t => t.Powiat == "" && t.Gmina == "")
                .OrderBy(t => t.Nazwa)
                .ToListAsync();

            // Jeœli wybrano województwo, za³aduj powiaty
            if (!string.IsNullOrEmpty(WojKod))
            {
                var wojewodztwo = Wojewodztwa.FirstOrDefault(w => w.Wojewodztwo == WojKod);
                if (wojewodztwo != null)
                {
                    CurrentPath = wojewodztwo.Nazwa;

                    Powiaty = await context.TerytTerc
                        .Where(t => t.Wojewodztwo == WojKod && t.Powiat != "" && t.Gmina == "")
                        .OrderBy(t => t.Nazwa)
                        .ToListAsync();
                }
            }

            // Jeœli wybrano powiat, za³aduj gminy
            if (!string.IsNullOrEmpty(PowKod))
            {
                var powiat = await context.TerytTerc
                    .FirstOrDefaultAsync(t => t.Wojewodztwo == WojKod && 
                                            t.Wojewodztwo + t.Powiat == PowKod && 
                                            t.Gmina == "");

                if (powiat != null)
                {
                    var woj = Wojewodztwa.FirstOrDefault(w => w.Wojewodztwo == WojKod);
                    CurrentPath = $"{woj?.Nazwa} > {powiat.Nazwa}";

                    Gminy = await context.TerytTerc
                        .Where(t => t.Wojewodztwo + t.Powiat == PowKod && t.Gmina != "")
                        .OrderBy(t => t.Nazwa)
                        .ToListAsync();
                }
            }

            // Jeœli wybrano gminê, za³aduj miejscowoœci (tabela TERYT_SIMC)
            if (!string.IsNullOrEmpty(GmiKod))
            {
                var gmina = await context.TerytTerc
                    .FirstOrDefaultAsync(t => t.Wojewodztwo + t.Powiat + t.Gmina + t.RodzajGminy == GmiKod);

                if (gmina != null)
                {
                    var woj = Wojewodztwa.FirstOrDefault(w => w.Wojewodztwo == WojKod);
                    var pow = Powiaty.FirstOrDefault(p => p.Wojewodztwo + p.Powiat == PowKod);
                    CurrentPath = $"{woj?.Nazwa} > {pow?.Nazwa} > {gmina.Nazwa}";

                    Miejscowosci = await context.TerytSimc
                        .Where(s => s.Wojewodztwo + s.Powiat + s.Gmina + s.RodzajGminy == GmiKod)
                        .OrderBy(s => s.Nazwa)
                        .ToListAsync();
                }
            }

            // Jeœli wybrano miejscowoœæ, za³aduj ulice (tabela TERYT_ULIC)
            if (!string.IsNullOrEmpty(MiejSymbol))
            {
                var miejscowosc = await context.TerytSimc
                    .FirstOrDefaultAsync(s => s.Symbol == MiejSymbol);

                if (miejscowosc != null)
                {
                    var woj = Wojewodztwa.FirstOrDefault(w => w.Wojewodztwo == WojKod);
                    var pow = Powiaty.FirstOrDefault(p => p.Wojewodztwo + p.Powiat == PowKod);
                    var gmi = Gminy.FirstOrDefault(g => g.Wojewodztwo + g.Powiat + g.Gmina + g.RodzajGminy == GmiKod);
                    CurrentPath = $"{woj?.Nazwa} > {pow?.Nazwa} > {gmi?.Nazwa} > {miejscowosc.Nazwa}";

                    Ulice = await context.TerytUlic
                        .Where(u => u.Symbol == MiejSymbol)
                        .OrderBy(u => u.Cecha)
                        .ThenBy(u => u.Nazwa1)
                        .ToListAsync();
                }
            }
        }
    }
}