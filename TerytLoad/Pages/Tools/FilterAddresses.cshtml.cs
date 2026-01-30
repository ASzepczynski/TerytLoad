using AddressLibrary.Services.Tools;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TerytLoad.Pages.Tools
{
    public class FilterAddressesModel : PageModel
    {
        [BindProperty]
        public string InputPath { get; set; } = @"AppData\Address\adresy.txt";
        [BindProperty]
        public string ErrorsPath { get; set; } = @"AppData\Address\adresy_bledy.txt";
        [BindProperty]
        public string OutputPath { get; set; } = @"AppData\Address\adresy_nowe.txt";
        public string? Message { get; set; }

        public void OnGet() { }

        public void OnPost()
        {
            try
            {
                AddressFileFilter.FilterByFirstColumn(InputPath, ErrorsPath, OutputPath);
                Message = $"Zakoñczono! Wynik zapisano do: {OutputPath}";
            }
            catch (Exception ex)
            {
                Message = $"B³¹d: {ex.Message}";
            }
        }
    }
}
