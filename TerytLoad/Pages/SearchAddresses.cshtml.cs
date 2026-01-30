using AddressLibrary.Data;
using AddressLibrary.Services.AddressSearch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TerytLoad.Pages
{
    public class SearchAddressesModel : PageModel
    {
        private readonly AddressDbContext _context;
        private readonly IWebHostEnvironment _env;

        public SearchAddressesModel(AddressDbContext context, IWebHostEnvironment env)
        {
            _env = env;
            _context = context;
        }

        [BindProperty]
        public List<AddressInputRow> Addresses { get; set; } = new();

        public List<AddressResultRow> Results { get; set; } = new();

        public class AddressInputRow
        {
            public string? KodPocztowy { get; set; }
            public string Miasto { get; set; } = string.Empty;
            public string? Ulica { get; set; }
            public string? NumerDomu { get; set; }
            public string? NumerMieszkania { get; set; }
        }

        public class AddressResultRow
        {
            public string Status { get; set; } = string.Empty;
            public string? ZnalezionyKodPocztowy { get; set; }
            public string? Miasto { get; set; }
            public string? Ulica { get; set; }
            public string? Message { get; set; }
        }

        public void OnGet()
        {
            // Inicjalizuj kilka przyk³adowych wierszy
            Addresses = new List<AddressInputRow>
            {
                new AddressInputRow(),
                new AddressInputRow(),
                new AddressInputRow()
            };
        }

        public async Task<IActionResult> OnPostSearchAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var searchService = new AddressSearchService(_context, _env.ContentRootPath);
            await searchService.InitializeAsync();

            var requests = Addresses
                .Where(a => !string.IsNullOrWhiteSpace(a.Miasto))
                .Select(a => new AddressSearchRequest
                {
                    KodPocztowy = a.KodPocztowy,
                    Miasto = a.Miasto,
                    Ulica = a.Ulica,
                    NumerDomu = a.NumerDomu,
                    NumerMieszkania = a.NumerMieszkania
                })
                .ToList();

            var results = await searchService.SearchBatchAsync(requests);

            Results = results.Select(r => new AddressResultRow
            {
                Status = r.Status.ToString(),
                ZnalezionyKodPocztowy = r.KodPocztowy?.Kod,
                Miasto = r.Miasto?.Nazwa,
                Ulica = r.Ulica != null ? $"{r.Ulica.Cecha} {r.Ulica.Nazwa1}" : null,
                Message = r.Message
            }).ToList();

            return Page();
        }
    }
}
