using AddressLibrary;
using AddressLibrary.Models;
using AddressLibrary.Services.AddressSearch;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;
using TerytLoad.Configuration;

namespace TerytLoad.Pages
{
    public class ProcessTaxOfficesModel : PageModel
    {
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;

        [BindProperty]
        public string Message { get; set; } = string.Empty;

        public ProcessStats? Stats { get; set; }

        public ProcessTaxOfficesModel(IWebHostEnvironment environment, IConfiguration configuration)
        {
            _environment = environment;
            _configuration = configuration;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostProcessAsync()
        {
            const string excelFilePath = @"c:\dane\us\UrzedySkarbowe.xlsx";

            try
            {
                if (!System.IO.File.Exists(excelFilePath))
                {
                    Message = $"❌ BŁĄD: Plik Excel nie istnieje: {excelFilePath}";
                    return Page();
                }

                var messageBuilder = new StringBuilder();
                messageBuilder.AppendLine($"📁 Plik źródłowy: {excelFilePath}");
                messageBuilder.AppendLine($"🔄 Rozpoczynam przetwarzanie...\n");

                var taxOffices = new List<TaxOfficeData>();
                int rowCount = 0;

                // Otwórz dokument Excel
                using (SpreadsheetDocument spreadsheet = SpreadsheetDocument.Open(excelFilePath, false))
                {
                    WorkbookPart? workbookPart = spreadsheet.WorkbookPart;
                    if (workbookPart == null)
                    {
                        Message = "❌ BŁĄD: Nie można otworzyć arkusza Excel";
                        return Page();
                    }

                    // Pobierz pierwszy arkusz
                    WorksheetPart worksheetPart = workbookPart.WorksheetParts.First();
                    SheetData sheetData = worksheetPart.Worksheet.Elements<SheetData>().First();
                    var rows = sheetData.Elements<Row>().ToList();

                    rowCount = rows.Count;
                    messageBuilder.AppendLine($"📊 Znaleziono {rowCount} wierszy w arkuszu");

                    TaxOfficeData? currentOffice = null;
                    int officeCount = 0;
                    int emptyCount = 0;

                    for (int i = 0; i < rows.Count; i++)
                    {
                        var row = rows[i];
                        var cells = row.Elements<Cell>().ToList();

                        // Pobierz wartość z kolumny A
                        string cellValue = string.Empty;
                        string hyperlinkUrl = string.Empty;

                        if (cells.Count > 0)
                        {
                            var cell = cells[0];
                            cellValue = GetCellValue(workbookPart, cell);

                            // Pobierz URL z hiperłącza jeśli istnieje
                            hyperlinkUrl = GetHyperlinkUrl(worksheetPart, cell);
                        }

                        // Określ pozycję w grupie 5-wierszowej (0-4)
                        int positionInGroup = i % 5;

                        switch (positionInGroup)
                        {
                            case 0: // A1, A6, A11... - Nazwa urzędu
                                // Zapisz poprzedni urząd jeśli istnieje
                                if (currentOffice != null && !string.IsNullOrWhiteSpace(currentOffice.Name))
                                {
                                    taxOffices.Add(currentOffice);
                                }

                                currentOffice = new TaxOfficeData { Name = CleanValue(cellValue, "Nazwa urzędu:") };
                                if (!string.IsNullOrWhiteSpace(cellValue))
                                {
                                    officeCount++;
                                }
                                else
                                {
                                    emptyCount++;
                                }
                                break;

                            case 1: // A2, A7, A12... - Adres
                                if (currentOffice != null)
                                    currentOffice.Address = CleanValue(cellValue, "Adres:");
                                break;

                            case 2: // A3, A8, A13... - Telefon/Faks
                                if (currentOffice != null)
                                    currentOffice.PhoneAndFax = CleanValue(cellValue, "Nr Telefonu/Fax:", "Telefon/Fax:", "Tel/Fax:");
                                break;

                            case 3: // A4, A9, A14... - Email
                                if (currentOffice != null)
                                {
                                    // Jeśli jest hiperłącze mailto:, wyciągnij adres email
                                    if (!string.IsNullOrWhiteSpace(hyperlinkUrl) && hyperlinkUrl.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                                    {
                                        currentOffice.Email = CleanValue(hyperlinkUrl.Substring(7), "Email:", "E-mail:");
                                    }
                                    else
                                    {
                                        currentOffice.Email = CleanValue(cellValue, "Email:", "E-mail:");
                                    }
                                }
                                break;

                            case 4: // A5, A10, A15... - WWW
                                if (currentOffice != null)
                                {
                                    // Jeśli jest hiperłącze, użyj URL-a zamiast tekstu wyświetlanego
                                    if (!string.IsNullOrWhiteSpace(hyperlinkUrl))
                                    {
                                        currentOffice.Website = CleanValue(hyperlinkUrl, "WWW:", "Strona WWW:", "www:");
                                    }
                                    else
                                    {
                                        currentOffice.Website = CleanValue(cellValue, "WWW:", "Strona WWW:", "www:");
                                    }
                                }
                                break;
                        }
                    }

                    // Dodaj ostatni urząd
                    if (currentOffice != null && !string.IsNullOrWhiteSpace(currentOffice.Name))
                    {
                        taxOffices.Add(currentOffice);
                    }

                    messageBuilder.AppendLine($"✅ Wykryto {officeCount} urzędów (pustych rekordów: {emptyCount})");
                    messageBuilder.AppendLine($"📋 Przetworzone urzędy: {taxOffices.Count}\n");
                }

                // Zapis do bazy danych
                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? DatabaseConfig.DefaultConnectionString;

                var appDataPath = _environment.ContentRootPath;
                var database = new AddressDatabase(connectionString, appDataPath);
                var context = database.GetContext();

                // Upewnij się że tabela istnieje
                await context.Database.EnsureCreatedAsync();

                messageBuilder.AppendLine($"💾 Rozpoczynam zapis do bazy danych...\n");

                // ✅ POPRAWKA: Inicjalizuj AddressSearchService z appDataPath
                messageBuilder.AppendLine($"🔍 Inicjalizuję AddressSearchService...");
                var searchService = new AddressSearchService(context, appDataPath);
                await searchService.InitializeAsync();
                messageBuilder.AppendLine($"✓ AddressSearchService zainicjalizowany\n");

                // Wyczyść istniejące dane (opcjonalnie)
                var existingCount = await context.UrzedySkarbowe.CountAsync();
                if (existingCount > 0)
                {
                    messageBuilder.AppendLine($"⚠️ W bazie znajduje się {existingCount} istniejących urzędów");
                    messageBuilder.AppendLine($"   Usuwam istniejące dane...");
                    context.UrzedySkarbowe.RemoveRange(context.UrzedySkarbowe);
                    await context.SaveChangesAsync();
                    messageBuilder.AppendLine($"   ✓ Usunięto {existingCount} rekordów\n");
                }

                // Dodaj nowe dane
                int savedCount = 0;
                int parsedCount = 0;
                int matchedStreets = 0;
                const int batchSize = 100;

                for (int i = 0; i < taxOffices.Count; i += batchSize)
                {
                    var batch = taxOffices.Skip(i).Take(batchSize).ToList();

                    foreach (var office in batch)
                    {
                        // Parsuj adres
                        var (kod, miasto, ulica, nrDomu) = ParseAddress(office.Address);

                        if (!string.IsNullOrWhiteSpace(kod) && !string.IsNullOrWhiteSpace(miasto))
                        {
                            parsedCount++;
                        }

                        // Spróbuj dopasować UlicaId używając AddressSearchService
                        int? ulicaId = null;

                        if (!string.IsNullOrWhiteSpace(miasto) && !string.IsNullOrWhiteSpace(ulica))
                        {
                            var searchRequest = new AddressSearchRequest
                            {
                                KodPocztowy = kod,
                                Miasto = miasto,
                                Ulica = ulica,
                                NumerDomu = nrDomu
                            };

                            if (ulica.Contains("Lindleya"))
                            {
                                int x = 1;
                            }

                            var searchResult = await searchService.SearchAsync(searchRequest);

                            if (searchResult.Status == AddressSearchStatus.Success && searchResult.Ulica != null)
                            {
                                ulicaId = searchResult.Ulica.Id;
                                matchedStreets++;
                            }
                        }

                        var urzad = new UrzadSkarbowy
                        {
                            Nazwa = office.Name ?? string.Empty,
                            Kod = kod,
                            Miasto = miasto,
                            Ulica = ulica,
                            NrDomu = nrDomu,
                            UlicaId = ulicaId, // ✅ POPRAWKA: Używaj dopasowanego ulicaId
                            Email = office.Email,
                            Www = office.Website
                        };

                        context.UrzedySkarbowe.Add(urzad);
                    }

                    await context.SaveChangesAsync();
                    savedCount += batch.Count;

                    messageBuilder.AppendLine($"   ✓ Zapisano {savedCount}/{taxOffices.Count} rekordów (dopasowano ulic: {matchedStreets})");
                }

                messageBuilder.AppendLine($"\n✅ Import zakończony pomyślnie!");
                messageBuilder.AppendLine($"   • Wierszy w Excelu: {rowCount}");
                messageBuilder.AppendLine($"   • Wykrytych urzędów: {taxOffices.Count}");
                messageBuilder.AppendLine($"   • Zapisanych do bazy: {savedCount}");
                messageBuilder.AppendLine($"   • Poprawnie sparsowanych adresów: {parsedCount}");
                messageBuilder.AppendLine($"   • Dopasowanych ulic (UlicaId): {matchedStreets} ({(double)matchedStreets / taxOffices.Count * 100:F1}%)");

                // Sprawdź ile jest rekordów w bazie
                var countInDb = await context.UrzedySkarbowe.CountAsync();
                var withUlicaId = await context.UrzedySkarbowe.CountAsync(u => u.UlicaId != null);
                messageBuilder.AppendLine($"   • Rekordów w bazie (łącznie): {countInDb}");
                messageBuilder.AppendLine($"   • Z wypełnionym UlicaId: {withUlicaId}");

                // Pokaż próbkę danych (pierwsze 5 urzędów)
                messageBuilder.AppendLine($"\n📋 Przykładowe dane (pierwsze 5 urzędów):");
                var sampleOffices = await context.UrzedySkarbowe.Take(5).ToListAsync();
                foreach (var office in sampleOffices)
                {
                    messageBuilder.AppendLine($"\n🏢 {office.Nazwa}");
                    messageBuilder.AppendLine($"   📮 Kod: {office.Kod}");
                    messageBuilder.AppendLine($"   🏙️  Miasto: {office.Miasto}");
                    messageBuilder.AppendLine($"   🛣️  Ulica: {office.Ulica}");
                    messageBuilder.AppendLine($"   🏠 Nr domu: {office.NrDomu}");
                    messageBuilder.AppendLine($"   🔑 UlicaId: {office.UlicaId?.ToString() ?? "NULL"}");
                    messageBuilder.AppendLine($"   ✉️  Email: {office.Email}");
                    messageBuilder.AppendLine($"   🌐 WWW: {office.Www}");
                }

                // Pokaż przykłady niedopasowanych ulic
                var unmatchedOffices = await context.UrzedySkarbowe
                    .Where(u => u.UlicaId == null && u.Ulica != null)
                    .Take(5)
                    .ToListAsync();

                if (unmatchedOffices.Any())
                {
                    messageBuilder.AppendLine($"\n⚠️ Przykłady niedopasowanych ulic (pierwsze 5):");
                    foreach (var office in unmatchedOffices)
                    {
                        messageBuilder.AppendLine($"   • {office.Miasto}, {office.Ulica} - {office.Nazwa}");
                    }
                }

                Message = messageBuilder.ToString();
                Stats = new ProcessStats
                {
                    TotalRows = rowCount,
                    ProcessedRecords = taxOffices.Count,
                    SavedRecords = savedCount,
                    MatchedStreets = matchedStreets
                };
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null
                    ? $"\n\nInner Exception:\n{ex.InnerException.Message}"
                    : "";

                Message = $"❌ BŁĄD podczas przetwarzania pliku:{Environment.NewLine}{ex.Message}{innerMsg}{Environment.NewLine}{Environment.NewLine}Stack trace:{Environment.NewLine}{ex.StackTrace}";
            }

            return Page();
        }

        /// <summary>
        /// Pobiera wartość z komórki Excel
        /// </summary>
        private static string GetCellValue(WorkbookPart workbookPart, Cell cell)
        {
            if (cell.CellValue == null)
                return string.Empty;

            string value = cell.CellValue.InnerText;

            // Jeśli komórka zawiera odwołanie do wspólnego ciągu znaków
            if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
            {
                SharedStringTablePart? stringTable = workbookPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault();
                if (stringTable != null)
                {
                    return stringTable.SharedStringTable.ElementAt(int.Parse(value)).InnerText;
                }
            }

            return value;
        }

        /// <summary>
        /// Pobiera URL z hiperłącza w komórce Excel
        /// </summary>
        private static string GetHyperlinkUrl(WorksheetPart worksheetPart, Cell cell)
        {
            if (cell.CellReference == null)
                return string.Empty;

            var cellReference = cell.CellReference.Value;
            var hyperlinks = worksheetPart.Worksheet.Descendants<Hyperlinks>().FirstOrDefault();

            if (hyperlinks == null)
                return string.Empty;

            var hyperlink = hyperlinks.Elements<Hyperlink>()
                .FirstOrDefault(h => h.Reference != null && h.Reference.Value == cellReference);

            if (hyperlink?.Id == null)
                return string.Empty;

            // Pobierz relację hiperłącza
            var hyperlinkRelationship = worksheetPart.HyperlinkRelationships
                .FirstOrDefault(r => r.Id == hyperlink.Id);

            if (hyperlinkRelationship?.Uri == null)
                return string.Empty;

            return hyperlinkRelationship.Uri.ToString();
        }

        /// <summary>
        /// Czyści wartość z niepotrzebnych prefiksów i formatuje
        /// </summary>
        private static string CleanValue(string value, params string[] prefixesToRemove)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value.Trim();

            // Usuń wszystkie możliwe prefiksy
            foreach (var prefix in prefixesToRemove)
            {
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(prefix.Length).Trim();
                    break;
                }
            }

            // Usuń prowadzący przecinek i spację po nim (format: ", wartość")
            if (value.StartsWith(","))
            {
                value = value.Substring(1).Trim();
            }

            return value;
        }

        /// <summary>
        /// Parsuje adres i wydobywa komponenty
        /// Przykład: "ul. Gen. Stanisława Maczka 73, 43-300 Bielsko-Biała" 
        /// -> ("43-300", "Bielsko-Biała", "ul. Gen. Stanisława Maczka", "73")
        /// </summary>
        private static (string? Kod, string? Miasto, string? Ulica, string? NrDomu) ParseAddress(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return (null, null, null, null);

            // Regex dla kodu pocztowego XX-XXX
            var postalCodeRegex = new Regex(@"\b(\d{2}-\d{3})\b");
            var postalCodeMatch = postalCodeRegex.Match(address);

            string? kod = null;
            string? miasto = null;
            string? ulica = null;
            string? nrDomu = null;

            // Wyciągnij kod pocztowy
            if (postalCodeMatch.Success)
            {
                kod = postalCodeMatch.Groups[1].Value;

                // Część po kodzie pocztowym to miasto
                var afterPostalCode = address.Substring(postalCodeMatch.Index + postalCodeMatch.Length).Trim();
                if (afterPostalCode.StartsWith(","))
                {
                    afterPostalCode = afterPostalCode.Substring(1).Trim();
                }
                miasto = afterPostalCode;

                // Część przed kodem pocztowym to ulica + numer
                var beforePostalCode = address.Substring(0, postalCodeMatch.Index).Trim();
                if (beforePostalCode.EndsWith(","))
                {
                    beforePostalCode = beforePostalCode.Substring(0, beforePostalCode.Length - 1).Trim();
                }

                // Wyciągnij numer domu (ostatni token z cyfrą)
                var tokens = beforePostalCode.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int i = tokens.Length - 1; i >= 0; i--)
                {
                    if (tokens[i].Any(char.IsDigit))
                    {
                        nrDomu = tokens[i];
                        ulica = string.Join(" ", tokens.Take(i)).Trim();
                        break;
                    }
                }

                // Jeśli nie znaleziono numeru, cała część to ulica
                if (string.IsNullOrEmpty(nrDomu))
                {
                    ulica = beforePostalCode;
                }
            }
            else
            {
                // Brak kodu pocztowego - spróbuj split po przecinku
                var parts = address.Split(',');
                if (parts.Length >= 2)
                {
                    // Ostatnia część może być miastem
                    miasto = parts[^1].Trim();

                    // Pierwsza część to ulica + numer
                    var streetPart = parts[0].Trim();
                    var tokens = streetPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    for (int i = tokens.Length - 1; i >= 0; i--)
                    {
                        if (tokens[i].Any(char.IsDigit))
                        {
                            nrDomu = tokens[i];
                            ulica = string.Join(" ", tokens.Take(i)).Trim();
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(nrDomu))
                    {
                        ulica = streetPart;
                    }
                }
            }

            return (kod, miasto, ulica, nrDomu);
        }

        public class ProcessStats
        {
            public int TotalRows { get; set; }
            public int ProcessedRecords { get; set; }
            public int SavedRecords { get; set; }
            public int MatchedStreets { get; set; }
        }

        private class TaxOfficeData
        {
            public string Name { get; set; } = string.Empty;
            public string Address { get; set; } = string.Empty;
            public string PhoneAndFax { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Website { get; set; } = string.Empty;
        }
    }
}