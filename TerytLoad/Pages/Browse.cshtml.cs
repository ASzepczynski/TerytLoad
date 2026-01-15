using AddressLibrary;
using AddressLibrary.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TerytLoad.Configuration;

namespace TerytLoad.Pages
{
    public class BrowseModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public List<Wojewodztwo> Wojewodztwa { get; set; } = new();
        public List<Powiat> Powiaty { get; set; } = new();
        public List<Gmina> Gminy { get; set; } = new();
        public List<Miasto> Miasta { get; set; } = new();
        public List<Ulica> Ulice { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public int? WojewodztwoId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? PowiatId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? GminaId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? MiastoId { get; set; }

        public string CurrentPath { get; set; } = string.Empty;

        public BrowseModel(IConfiguration configuration, IWebHostEnvironment environment)
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

            // Zawsze ³aduj województwa
            Wojewodztwa = await context.Wojewodztwa
                .Where(w => w.Id != -1)
                .OrderBy(w => w.Nazwa)
                .ToListAsync();

            // Jeœli wybrano województwo, za³aduj powiaty
            if (WojewodztwoId.HasValue)
            {
                var wojewodztwo = await context.Wojewodztwa.FindAsync(WojewodztwoId.Value);
                if (wojewodztwo != null)
                {
                    CurrentPath = wojewodztwo.Nazwa;

                    Powiaty = await context.Powiaty
                        .Where(p => p.WojewodztwoId == WojewodztwoId.Value && p.Id != -1)
                        .OrderBy(p => p.Nazwa)
                        .ToListAsync();
                }
            }

            // Jeœli wybrano powiat, za³aduj gminy
            if (PowiatId.HasValue)
            {
                var powiat = await context.Powiaty
                    .Include(p => p.Wojewodztwo)
                    .FirstOrDefaultAsync(p => p.Id == PowiatId.Value);

                if (powiat != null)
                {
                    CurrentPath = $"{powiat.Wojewodztwo.Nazwa} > {powiat.Nazwa}";

                    Gminy = await context.Gminy
                        .Include(g => g.RodzajGminy)
                        .Where(g => g.PowiatId == PowiatId.Value && g.Id != -1)
                        .OrderBy(g => g.Nazwa)
                        .ToListAsync();
                }
            }

            // Jeœli wybrano gminê, za³aduj miejscowoœci
            if (GminaId.HasValue)
            {
                var gmina = await context.Gminy
                    .Include(g => g.Powiat)
                    .ThenInclude(p => p.Wojewodztwo)
                    .FirstOrDefaultAsync(g => g.Id == GminaId.Value);

                if (gmina != null)
                {
                    CurrentPath = $"{gmina.Powiat.Wojewodztwo.Nazwa} > {gmina.Powiat.Nazwa} > {gmina.Nazwa}";

                    Miasta = await context.Miasta
                        .Include(m => m.RodzajMiasta)
                        .Where(m => m.GminaId == GminaId.Value && m.Id != -1)
                        .OrderBy(m => m.Nazwa)
                        .ToListAsync();
                }
            }

            // Jeœli wybrano miejscowoœæ, za³aduj ulice
            if (MiastoId.HasValue)
            {
                var miasto = await context.Miasta
                    .Include(m => m.Gmina)
                    .ThenInclude(g => g.Powiat)
                    .ThenInclude(p => p.Wojewodztwo)
                    .FirstOrDefaultAsync(m => m.Id == MiastoId.Value);

                if (miasto != null)
                {
                    CurrentPath = $"{miasto.Gmina.Powiat.Wojewodztwo.Nazwa} > {miasto.Gmina.Powiat.Nazwa} > {miasto.Gmina.Nazwa} > {miasto.Nazwa}";

                    Ulice = await context.Ulice
                        .Where(u => u.MiastoId == MiastoId.Value && u.Id != -1)
                        .OrderBy(u => u.Cecha)
                        .ThenBy(u => u.Nazwa1)
                        .ToListAsync();
                }
            }
        }
    }
}