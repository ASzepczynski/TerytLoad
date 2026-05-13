using AddressLibrary.Helpers;
using AddressLibrary.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text;

namespace TerytLoad.Pages.Tools
{
    public class ImportSapAddressesModel : PageModel
    {
        private readonly IWebHostEnvironment _environment;

        public ImportSapAddressesModel(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        [BindProperty]
        public string Message { get; set; } = string.Empty;

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostConvertAsync()
        {
            try
            {
                var appData = _environment.ContentRootPath;
                var excelPath = Path.Combine(appData, "AppData", "Address", "address_sap.xlsx");

                if (!System.IO.File.Exists(excelPath))
                {
                    Message = $"❌ Plik nie istnieje: {excelPath}";
                    return Page();
                }

                var rows = ExcelTableReader.Read(excelPath);
                if (rows == null || rows.Count == 0)
                {
                    Message = "❌ Brak wierszy w pliku Excel";
                    return Page();
                }

                var listaAdresow = new List<Adres>();

                foreach (var row in rows)
                {
                    // Try several possible column names for each field
                    string Get(params string[] names)
                    {
                        foreach (var n in names)
                        {
                            if (row.Columns.Contains(n) && !string.IsNullOrWhiteSpace(row.GetString(n)))
                                return row.GetString(n).Trim();
                        }
                        return string.Empty;
                    }


                    var adres = new Adres();


                    adres.Id = Get("Nr osob.");
                    if (string.IsNullOrWhiteSpace(adres.Id))
                    {
                        // skip row without id
                        continue;
                    }

                    adres.Kraj = Get("Country", "Klucz kraju/regionu");
                    if (string.IsNullOrWhiteSpace(adres.Kraj)) adres.Kraj = "PL";

                    adres.Kod = Get("Kod pocztowy");
                    adres.Miasto = Get("Miejscowość");
                    adres.Ulica = Get("Ulica i numer domu");
                    adres.NrDomu = Get("NrDomu");
                    adres.NrLokalu = Get("NrLokalu");
                    adres.Wojewodztwo = Get("Województwo");
                    adres.Powiat = Get("Powiat");
                    adres.Gmina = "";
                    adres.Komentarz = "";
                    listaAdresow.Add(adres);
                }

                var outPath = @"AppData\Address\address_sap.txt";

                // Wyklucz kolumnę 'Komentarz' z pliku wynikowego
                AddressLibrary.Helpers.ExcelTableWriter.WriteToTextFile(listaAdresow, outPath, '|', Encoding.UTF8, includeHeader: true, excludeColumns: new[] { "Komentarz" });

                Message = $"✅ Zapisano {listaAdresow.Count} wierszy do: {outPath}";
            }
            catch (Exception ex)
            {
                Message = $"❌ Błąd: {ex.Message}";
            }

            return Page();
        }
    }
}
