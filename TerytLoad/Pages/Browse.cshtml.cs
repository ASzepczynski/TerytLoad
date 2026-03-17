using AddressLibrary;
using AddressLibrary.Models;
using AddressLibrary.Extensions; // ✅ DODAJ TĘ LINIĘ
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

                    // ✅ POPRAWIONE: Usuń OrderBy/ThenBy z zapytania SQL
                    var ulice = await context.Ulice
                        .IncludeTypUlicy() // Załaduj TypUlicy dla computed properties
                        .Where(u => u.MiastoId == MiastoId.Value && u.Id != -1)
                        .ToListAsync(); // Najpierw pobierz do pamięci

                    // ✅ Sortowanie w pamięci (computed properties działają)
                    ulice = ulice.SortByNazwa();

                    List<int> ulicaIds = ulice
                        .Where(u => u.Id != -1)
                        .Select(u => u.Id)
                        .ToList();

                    // Pobierz kody pocztowe i dzielnice dla ulic
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
                                .Select(g =>
                                {
                                    var numery = g
                                        .Select(x => x.Numery)
                                        .Where(n => !string.IsNullOrWhiteSpace(n))
                                        .Distinct()
                                        .OrderBy(n =>
                                        {
                                            // Wyciągnij pierwszą liczbę z numeru, np. "12A" -> 12
                                            var match = System.Text.RegularExpressions.Regex.Match(n, @"\d+");
                                            return match.Success ? int.Parse(match.Value) : int.MaxValue;
                                        })
                                        .ToList();

                                    var numeryStr = numery.Count > 0 ? string.Join(",", numery) : "-";
                                    // Dla sortowania po pierwszej liczbie z numeryStr
                                    var pierwszaLiczba = numery.Count > 0
                                        ? int.TryParse(System.Text.RegularExpressions.Regex.Match(numery[0], @"\d+").Value, out var val) ? val : int.MaxValue
                                        : int.MaxValue;

                                    return new { Kod = g.Key, NumeryStr = numeryStr, PierwszaLiczba = pierwszaLiczba };
                                })
                                .OrderBy(x => x.PierwszaLiczba)
                                .Select(x => $"{x.NumeryStr}({x.Kod})")
                                .ToList();

                            return new UlicaWithPostalCodes
                            {
                                Ulica = u,
                                KodyPocztoweZNumerami = string.Join(Environment.NewLine, kodyPocztoweZNumerami)
                            };
                        })
                        .ToList();
                }
            }
        }

        public class MiastoWithPostalCodes
        {
            public Miasto Miasto { get; set; } = default!;
            public string? MinKod { get; set; }
            public string? MaxKod { get; set; }
        }
        public List<MiastoWithPostalCodes> MiastaWithPostalCodes { get; set; } = new();

        public class UlicaWithPostalCodes
        {
            public Ulica Ulica { get; set; } = default!;
            public List<string> KodyPocztoweZDzielnicami { get; set; } = new();
            public string KodyPocztoweZNumerami { get; set; } = string.Empty;
        }
        public List<UlicaWithPostalCodes> UliceWithPostalCodes { get; set; } = new();
    }
}