// Copyright (c) 2025-2026 Andrzej Szepczyński. All rights reserved.

using AddressLibrary.Data;
using AddressLibrary.Models;
using AddressLibrary.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace TerytLoad.Pages
{
    public class SzukajModel : PageModel
    {
        private readonly AddressDbContext _context;

        public SzukajModel(AddressDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public SearchParametersModel SearchParams { get; set; } = new();

        public List<KodPocztowyZAdresem>? Wyniki { get; set; }

        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            // Pusta strona przy pierwszym wejściu
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            try
            {
                var searchService = new KodPocztowySearchService(_context);

                var parameters = new KodPocztowySearchService.SearchParameters
                {
                    Kod = SearchParams.Kod,
                    Miasto = SearchParams.Miasto,
                    Ulica = SearchParams.Ulica,
                    NumerDomu = SearchParams.NumerDomu,
                    NumerMieszkania = SearchParams.NumerMieszkania
                };

                Wyniki = await searchService.SzukajAsync(parameters);

                if (Wyniki.Count == 0)
                {
                    ErrorMessage = null; // Brak błędu, po prostu brak wyników
                }
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = ex.Message;
                Wyniki = null;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Wystąpił błąd podczas wyszukiwania: {ex.Message}";
                Wyniki = null;
            }

            return Page();
        }

        public IActionResult OnPostClear()
        {
            SearchParams = new SearchParametersModel();
            Wyniki = null;
            ErrorMessage = null;
            return Page();
        }
    }

    /// <summary>
    /// Model dla formularza wyszukiwania
    /// </summary>
    public class SearchParametersModel
    {
        [Display(Name = "Kod pocztowy")]
        [RegularExpression(@"^\d{2}-?\d{3}$", ErrorMessage = "Kod pocztowy musi być w formacie XX-XXX")]
        public string? Kod { get; set; }

        [Display(Name = "Miejscowość")]
        public string? Miasto { get; set; }

        [Display(Name = "Ulica")]
        public string? Ulica { get; set; }

        [Display(Name = "Numer domu")]
        public string? NumerDomu { get; set; }

        [Display(Name = "Numer mieszkania")]
        public string? NumerMieszkania { get; set; }
    }
}