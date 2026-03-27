using AddressLibrary;
using AddressLibrary.Models;
using AddressLibrary.Extensions;
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

            // Zawsze ładuj województwa
            Wojewodztwa = await context.Wojewodztwa
                .Where(w => w.Id != -1)
                .OrderBy(w => w.Nazwa)
                .ToListAsync();

            // Jeśli wybrano województwo, załaduj powiaty
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

            // Jeśli wybrano powiat, załaduj gminy
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

            // Jeśli wybrano gminę, załaduj miejscowości
            if (GminaId.HasValue)
            {
                var gmina = await context.Gminy
                    .Include(g => g.Powiat)
                    .ThenInclude(p => p.Wojewodztwo)
                    .FirstOrDefaultAsync(g => g.Id == GminaId.Value);

                if (gmina != null)
                {
                    CurrentPath = $"{gmina.Powiat.Wojewodztwo.Nazwa} > {gmina.Powiat.Nazwa} > {gmina.Nazwa}";

                    var miasta = await context.Miasta
                        .Include(m => m.RodzajMiasta)
                        .Where(m => m.GminaId == GminaId.Value && m.Id != -1)
                        .OrderBy(m => m.Nazwa)
                        .ToListAsync();

                    // Pobierz kody pocztowe dla każdego miasta
                    var miastaIds = miasta.Select(m => m.Id).ToList();
                    var kody = await context.KodyPocztowe
                        .Where(k => miastaIds.Contains(k.MiastoId))
                        .GroupBy(k => k.MiastoId)
                        .Select(g => new
                        {
                            MiastoId = g.Key,
                            MinKod = g.Min(x => x.Kod),
                            MaxKod = g.Max(x => x.Kod)
                        })
                        .ToListAsync();

                    MiastaWithPostalCodes = miasta
                        .Select(m =>
                        {
                            var kod = kody.FirstOrDefault(k => k.MiastoId == m.Id);
                            return new MiastoWithPostalCodes
                            {
                                Miasto = m,
                                MinKod = kod?.MinKod,
                                MaxKod = kod?.MaxKod
                            };
                        })
                        .ToList();
                }
            }

            // Jeśli wybrano miejscowość, załaduj ulice
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

                    // ✅ POPRAWIONE: Dodano Include dla CechaUlicy
                    var ulice = await context.Ulice
                        .Include(u => u.CechaUlicy)  // ✅ DODANE
                        .IncludeTypUlicy()
                        .Where(u => u.MiastoId == MiastoId.Value && u.Id != -1)
                        .ToListAsync();

                    ulice = ulice.SortByNazwa();

                    List<int> ulicaIds = ulice
                        .Where(u => u.Id != -1)
                        .Select(u => u.Id)
                        .ToList();

                    var kodyBezDzielnic = await context.KodyPocztowe
                        .Where(k => ulicaIds.Contains(k.UlicaId))
                        .Select(k => new { k.UlicaId, k.Kod })
                        .ToListAsync();

                    var kodyZNumerami = await context.KodyPocztowe
                        .Where(k => ulicaIds.Contains(k.UlicaId))
                        .Select(k => new { k.UlicaId, k.Kod, k.Numery })
                        .ToListAsync();

                    UliceWithPostalCodes = ulice
                        .Select(u =>
                        {
                            var kodyPocztoweZNumerami = kodyZNumerami
                                .Where(k => k.UlicaId == u.Id)
                                .GroupBy(k => k.Kod)
                                .Select(g => new KodPocztowyZNumerami
                                {
                                    Kod = g.Key,
                                    Numery = g.SelectMany(x => x.Numery?.Split(',') ?? Array.Empty<string>())
                                            .Where(n => !string.IsNullOrWhiteSpace(n))
                                            .Select(n => n.Trim())
                                            .OrderBy(n => n, new NumeryComparer())
                                            .ToList()
                                })
                                .ToList();

                            var kodySimplee = kodyBezDzielnic
                                .Where(k => k.UlicaId == u.Id)
                                .Select(k => k.Kod)
                                .Distinct()
                                .OrderBy(k => k)
                                .ToList();

                            return new UlicaWithPostalCodes
                            {
                                Ulica = u,
                                KodyPocztoweZNumerami = kodyPocztoweZNumerami,
                                KodyPocztowe = kodySimplee
                            };
                        })
                        .ToList();
                }
            }
        }

        public List<MiastoWithPostalCodes> MiastaWithPostalCodes { get; set; } = new();
        public List<UlicaWithPostalCodes> UliceWithPostalCodes { get; set; } = new();
    }

    public class MiastoWithPostalCodes
    {
        public Miasto Miasto { get; set; } = null!;
        public string? MinKod { get; set; }
        public string? MaxKod { get; set; }
    }

    public class UlicaWithPostalCodes
    {
        public Ulica Ulica { get; set; } = null!;
        public List<string> KodyPocztowe { get; set; } = new();
        public List<KodPocztowyZNumerami> KodyPocztoweZNumerami { get; set; } = new();
    }

    public class KodPocztowyZNumerami
    {
        public string Kod { get; set; } = string.Empty;
        public List<string> Numery { get; set; } = new();
    }

    public class NumeryComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var xParts = x.Split('/');
            var yParts = y.Split('/');

            if (int.TryParse(xParts[0], out int xNum) && int.TryParse(yParts[0], out int yNum))
            {
                return xNum.CompareTo(yNum);
            }

            return string.Compare(x, y, StringComparison.Ordinal);
        }
    }
}