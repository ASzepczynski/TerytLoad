using AddressLibrary;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TerytLoad.Configuration;

namespace TerytLoad.Pages
{
    public class MissingPostalCodesModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public MissingPostalCodesModel(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public void OnGet() { }

        public async Task<IActionResult> OnGetExportAsync()
        {
            var connectionString = _configuration.GetConnectionString("AddressDatabase")
                ?? DatabaseConfig.DefaultConnectionString;
            var db = new AddressDatabase(connectionString, _environment.ContentRootPath);
            var context = db.GetContext();

            // --- Miasta bez kodów (z wy³¹czeniem pod-miejscowoœci w formacie "G³ówne(Podrzêdna)") ---
            var miastaBezKodow = await context.Miasta
                .Where(m => m.Id != -1 &&
                            !m.Nazwa.Contains("(") &&
                            !context.KodyPocztowe.Any(k => k.MiastoId == m.Id))
                .Select(m => new
                {
                    Miejscowosc = m.Nazwa,
                    Gmina       = m.Gmina.Nazwa,
                    Powiat      = m.Gmina.Powiat.Nazwa,
                    Wojewodztwo = m.Gmina.Powiat.Wojewodztwo.Nazwa
                })
                .OrderBy(x => x.Wojewodztwo)
                .ThenBy(x => x.Powiat)
                .ThenBy(x => x.Gmina)
                .ThenBy(x => x.Miejscowosc)
                .ToListAsync();

            // --- Ulice bez kodów (z wy³¹czeniem ulic w miastach z kodem na ca³e miasto UlicaId=-1) ---
            var uliceBezKodow = await context.Ulice
                .Where(u => u.Id != -1 &&
                            !context.KodyPocztowe.Any(k => k.UlicaId == u.Id) &&
                            !context.KodyPocztowe.Any(k => k.MiastoId == u.MiastoId && k.UlicaId == -1))
                .Select(u => new
                {
                    Ulica = (u.CechaUlicy.Skrot ?? "") + " " +
                            (u.TypUlicy != null
                                ? (u.TypUlicy.Nazwisko != null && u.TypUlicy.Nazwisko != ""
                                    ? u.TypUlicy.Nazwisko
                                    : (u.TypUlicy.Imie != null && u.TypUlicy.Imie != ""
                                        ? u.TypUlicy.Imie
                                        : (u.TypUlicy.Postfiks ?? "")))
                                : ""),
                    Miejscowosc = u.Miasto.Nazwa,
                    Gmina       = u.Miasto.Gmina.Nazwa,
                    Powiat      = u.Miasto.Gmina.Powiat.Nazwa,
                    Wojewodztwo = u.Miasto.Gmina.Powiat.Wojewodztwo.Nazwa
                })
                .OrderBy(x => x.Wojewodztwo)
                .ThenBy(x => x.Powiat)
                .ThenBy(x => x.Gmina)
                .ThenBy(x => x.Miejscowosc)
                .ThenBy(x => x.Ulica)
                .ToListAsync();

            // --- Generuj Excel ---
            using var ms = new MemoryStream();
            using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = doc.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = BuildStylesheet();
                stylesPart.Stylesheet.Save();

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());

                // Arkusz 1: Miasta
                AddSheet(doc, workbookPart, sheets, 1, "Miasta bez kodow",
                    new[] { "Miejscowoœæ", "Gmina", "Powiat", "Województwo" },
                    miastaBezKodow.Select(r => new[] { r.Miejscowosc, r.Gmina, r.Powiat, r.Wojewodztwo }));

                // Arkusz 2: Ulice
                AddSheet(doc, workbookPart, sheets, 2, "Ulice bez kodow",
                    new[] { "Ulica", "Miejscowoœæ", "Gmina", "Powiat", "Województwo" },
                    uliceBezKodow.Select(r => new[] { r.Ulica, r.Miejscowosc, r.Gmina, r.Powiat, r.Wojewodztwo }));

                workbookPart.Workbook.Save();
            }

            var fileName = $"BrakiKodowPocztowych_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        private static void AddSheet(
            SpreadsheetDocument doc,
            WorkbookPart workbookPart,
            Sheets sheets,
            uint sheetId,
            string sheetName,
            string[] headers,
            IEnumerable<string[]> rows)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            sheets.Append(new Sheet
            {
                Id = doc.WorkbookPart!.GetIdOfPart(worksheetPart),
                SheetId = sheetId,
                Name = sheetName
            });

            // Nag³ówek
            var headerRow = new Row();
            foreach (var h in headers)
                headerRow.Append(new Cell
                {
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(h)),
                    StyleIndex = 1
                });
            sheetData.Append(headerRow);

            // Dane
            foreach (var row in rows)
            {
                var dataRow = new Row();
                foreach (var cell in row)
                    dataRow.Append(new Cell
                    {
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text(cell ?? string.Empty))
                    });
                sheetData.Append(dataRow);
            }
        }

        private static Stylesheet BuildStylesheet() =>
            new Stylesheet(
                new Fonts(
                    new Font(),
                    new Font(new Bold())),
                new Fills(
                    new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                    new Fill(new PatternFill(
                        new ForegroundColor { Rgb = "FFC6EFCE" })
                    { PatternType = PatternValues.Solid })),
                new Borders(
                    new Border()),
                new CellStyleFormats(
                    new CellFormat()),
                new CellFormats(
                    new CellFormat { FontId = 0, ApplyFont = true },
                    new CellFormat { FontId = 1, FillId = 2, BorderId = 0, ApplyFont = true, ApplyFill = true }));
    }
}
