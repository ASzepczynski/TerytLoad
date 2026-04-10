// Copyright (c) 2025-2026 Andrzej Szepczyñski. All rights reserved.

using AddressLibrary.Cache;
using AddressLibrary.Data;
using AddressLibrary.Services.AddressSearch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace TerytLoad.Pages
{
    public class SzukajModel : PageModel
    {
        private readonly AddressDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public SzukajModel(AddressDbContext context, IWebHostEnvironment environment)
        {
            _context     = context;
            _environment = environment;
        }

        [BindProperty]
        public SearchParametersModel SearchParams { get; set; } = new();

        public AddressSearchResult? Wynik { get; set; }
        public string? ErrorMessage { get; set; }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
                return Page();

            try
            {
                var appDataPath = _environment.ContentRootPath;

                var appCache = new AppCache(_context);
                await appCache.InitializeAsync();

                var searchService = new AddressSearchService(_context, appDataPath);
                await searchService.InitializeAsync();

                var request = new AddressSearchRequest
                {
                    KodPocztowy     = SearchParams.Kod,
                    Miasto          = SearchParams.Miasto ?? string.Empty,
                    Ulica           = SearchParams.Ulica,
                    NumerDomu       = SearchParams.NumerDomu,
                    NumerMieszkania = SearchParams.NumerMieszkania
                };

                Wynik = await searchService.SearchAsync(request);

                if (Wynik.Status != AddressSearchStatus.Success)
                    ErrorMessage = Wynik.Message;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"B³¹d: {ex.Message}";
                Wynik = null;
            }

            return Page();
        }

        public IActionResult OnPostClear()
        {
            SearchParams = new SearchParametersModel();
            Wynik        = null;
            ErrorMessage = null;
            return Page();
        }
    }

    public class SearchParametersModel
    {
        [Display(Name = "Kod pocztowy")]
        [RegularExpression(@"^\d{2}-?\d{3}$", ErrorMessage = "Kod pocztowy musi byæ w formacie XX-XXX")]
        public string? Kod { get; set; }

        [Display(Name = "Miejscowoœæ")]
        public string? Miasto { get; set; }

        [Display(Name = "Ulica")]
        public string? Ulica { get; set; }

        [Display(Name = "Numer domu")]
        public string? NumerDomu { get; set; }

        [Display(Name = "Numer mieszkania")]
        public string? NumerMieszkania { get; set; }
    }
}
