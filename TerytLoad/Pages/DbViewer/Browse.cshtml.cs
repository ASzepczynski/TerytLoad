using AddressLibrary.Data;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using AddressLibrary.Attributes;

namespace TerytLoad.Pages.DbViewer
{
    public class BrowseModel : PageModel
    {
        private readonly AddressDbContext _context;
        private readonly ILogger<BrowseModel> _logger;

        public BrowseModel(AddressDbContext context, ILogger<BrowseModel> logger)
        {
            _context = context;
            _logger = logger;
        }

        [BindProperty(SupportsGet = true)]
        public string Entity { get; set; } = string.Empty;

        // Filtry jako JSON w query stringu
        [BindProperty(SupportsGet = true)]
        public string? Filters { get; set; }

        // Kontekst rodzica (gdy przeglądamy relację dziecko)
        [BindProperty(SupportsGet = true)]
        public string? ParentEntity { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? ParentId { get; set; }

        // Sortowanie
        [BindProperty(SupportsGet = true)]
        public string? SortColumn { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SortDirection { get; set; } = "asc";

        public ViewerConfig? Config { get; set; }
        public List<object> Items { get; set; } = new();
        public object? ParentItem { get; set; }
        public string? ParentDescription { get; set; }
        public List<FilterOption> FilterOptions { get; set; } = new();
        public List<FilterCondition> ActiveFilters { get; set; } = new();

        // Tryb pickera (wybór FK) - gdy ustawiony, podmenu Akcja pokazuje tylko "Wybierz"
        [BindProperty(SupportsGet = true)]
        public bool PickerMode { get; set; }

        /// <summary>Nazwa pola w formularzu edycji, do którego wróci wybrana wartość</summary>
        [BindProperty(SupportsGet = true)]
        public string? PickerTargetField { get; set; }

        // Stary parametr – potrzebny przy nawigacji child→parent (przekazuje FilterColumn=XxxId&FilterValue=ID)
        [BindProperty(SupportsGet = true)]
        public string? FilterColumn { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? FilterOperator { get; set; } = "equals";

        [BindProperty(SupportsGet = true)]
        public string? FilterValue { get; set; }

        public ViewerConfigDto? ConfigDto => Config != null ? new ViewerConfigDto
        {
            EntityName = Config.EntityName,
            DisplayName = Config.DisplayName,
            Icon = Config.Icon,
            Description = Config.Description,
            Columns = Config.Columns.Select(c => new ColumnConfigDto
            {
                PropertyName = c.PropertyName,
                DisplayName = c.DisplayName,
                TypeName = c.Type.Name,
                IsFilterable = c.IsFilterable,
                IsEditable = c.IsEditable,
                IsRequired = c.IsRequired,
                MaxLength = c.MaxLength,
                IsForeignKey = c.IsForeignKey,
                ForeignKeyEntity = c.ForeignKeyEntity,
                ForeignKeyDisplayProperty = c.ForeignKeyDisplayProperty,
                ForeignKeyNavigationProperty = c.ForeignKeyNavigationProperty,
                HasOpisMethod = c.HasOpisMethod,
                ChoiceMode = c.ChoiceMode.ToString()
            }).ToList()
        } : null;

        public string ConfigDtoJson => ConfigDto != null
            ? JsonSerializer.Serialize(ConfigDto, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            })
            : "null";

        public async Task<IActionResult> OnGetAsync()
        {
            Config = ViewerRegistry.GetConfig(Entity);
            if (Config == null)
                return NotFound($"Nie znaleziono konfiguracji dla: {Entity}");

            // Jeśli mamy kontekst rodzica, załaduj informacje o rodzicu
            if (!string.IsNullOrEmpty(ParentEntity) && ParentId.HasValue)
            {
                var parentConfig = ViewerRegistry.GetConfig(ParentEntity);
                if (parentConfig != null)
                {
                    ParentItem = await FindEntityByIdAsync(parentConfig.EntityType, ParentId.Value);
                    if (ParentItem != null)
                        ParentDescription = EntityDescriptionHelper.GetDescription(ParentItem, parentConfig);
                }
            }

            FilterOptions = FilterOption.BuildFor(Config);

            // Parsuj filtry z JSON lub ze starych parametrów (nawigacja child)
            ActiveFilters = ParseFilters();

            var items = await GetItemsFromDbAsync(Config.EntityType, ActiveFilters, SortColumn, SortDirection);
            if (items == null)
                return NotFound($"Nie znaleziono DbSet dla: {Entity}");

            Items = items;
            return Page();
        }

        private List<FilterCondition> ParseFilters()
        {
            // Nowy format: JSON w parametrze Filters
            if (!string.IsNullOrEmpty(Filters))
            {
                try
                {
                    return JsonSerializer.Deserialize<List<FilterCondition>>(Filters,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                }
                catch { /* ignoruj błąd parsowania */ }
            }

            // Stary format: pojedynczy FilterColumn/FilterOperator/FilterValue (z nawigacji child)
            if (!string.IsNullOrEmpty(FilterColumn) && !string.IsNullOrEmpty(FilterValue))
            {
                var label = FilterOptions.FirstOrDefault(o => o.FilterPath == FilterColumn)?.DisplayLabel
                            ?? FilterColumn;
                return new List<FilterCondition>
                {
                    new()
                    {
                        Column    = FilterColumn,
                        Operator  = FilterOperator ?? "equals",
                        Value     = FilterValue,
                        ColumnLabel = label
                    }
                };
            }

            return new();
        }

        public async Task<IActionResult> OnGetExportExcelAsync()
        {
            Config = ViewerRegistry.GetConfig(Entity);
            if (Config == null)
                return NotFound($"Nie znaleziono konfiguracji dla: {Entity}");

            ActiveFilters = ParseFilters();
            var items = await GetItemsFromDbAsync(Config.EntityType, ActiveFilters, SortColumn, SortDirection);
            if (items == null)
                return NotFound();

            var headers = Config.Columns.Select(c => c.DisplayName).ToList();
            var rows = items.Select(item => Config.Columns.Select(col =>
                GetDisplayValue(item, col, Config)).ToList()).ToList();

            using var ms = new MemoryStream();
            using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = doc.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                // Style: jasnozielone tło nagłówków (#C6EFCE)
                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = new Stylesheet(
                    new Fonts(
                        new Font(),                                                     // font 0 – normalny (dane)
                        new Font(new Bold())),                                          // font 1 – pogrubiony (nagłówek)
                    new Fills(
                        new Fill(new PatternFill { PatternType = PatternValues.None }), // fill 0 – wymagany
                        new Fill(new PatternFill { PatternType = PatternValues.Gray125 }), // fill 1 – wymagany
                        new Fill(new PatternFill(                                        // fill 2 – jasnozielony
                            new ForegroundColor { Rgb = "FFC6EFCE" })
                        { PatternType = PatternValues.Solid })),
                    new Borders(
                        new Border()),                                                  // border 0 – pusty
                    new CellStyleFormats(
                        new CellFormat()),                                              // cellStyleFormat 0
                    new CellFormats(
                        new CellFormat { FontId = 0, ApplyFont = true },               // styl 0 – dane (normalny)
                        new CellFormat                                                  // styl 1 – nagłówek
                        {
                            FontId = 1, FillId = 2, BorderId = 0,
                            ApplyFont = true, ApplyFill = true
                        }));
                stylesPart.Stylesheet.Save();

                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();
                worksheetPart.Worksheet = new Worksheet(sheetData);

                var sheets = doc.WorkbookPart!.Workbook.AppendChild(new Sheets());
                sheets.Append(new Sheet
                {
                    Id = doc.WorkbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1,
                    Name = Config.DisplayName.Length > 31
                        ? Config.DisplayName[..31]
                        : Config.DisplayName
                });

                // Nagłówki – styl 1 (jasnozielone tło, pogrubienie)
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

                workbookPart.Workbook.Save();
            }

            var fileName = $"{Entity}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        public async Task<IActionResult> OnGetExportCsvAsync()
        {
            Config = ViewerRegistry.GetConfig(Entity);
            if (Config == null)
                return NotFound($"Nie znaleziono konfiguracji dla: {Entity}");

            ActiveFilters = ParseFilters();
            var items = await GetItemsFromDbAsync(Config.EntityType, ActiveFilters, SortColumn, SortDirection);
            if (items == null)
                return NotFound();

            const string sep = "|";
            var sb = new StringBuilder();

            // Nagłówki
            sb.AppendLine(string.Join(sep, Config.Columns.Select(c => EscapeCsvField(c.DisplayName, sep))));

            // Dane
            foreach (var item in items)
            {
                var cells = Config.Columns.Select(col =>
                    EscapeCsvField(GetDisplayValue(item, col, Config) ?? string.Empty, sep));
                sb.AppendLine(string.Join(sep, cells));
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            var fileName = $"{Entity}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        private static string? GetDisplayValue(object item, ColumnConfig col, ViewerConfig config)
        {
            if (col.IsForeignKey && !string.IsNullOrEmpty(col.ForeignKeyNavigationProperty))
            {
                var navProp = config.EntityType.GetProperty(col.ForeignKeyNavigationProperty);
                var navItem = navProp?.GetValue(item);
                if (navItem == null) return null;
                var fkConfig = ViewerRegistry.GetConfig(col.ForeignKeyEntity!);
                return EntityDescriptionHelper.GetDescription(navItem, fkConfig);
            }
            return config.EntityType.GetProperty(col.PropertyName)?.GetValue(item)?.ToString();
        }

        private static string EscapeCsvField(string value, string sep)
        {
            if (value.Contains(sep) || value.Contains('"') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        public async Task<IActionResult> OnGetEditAsync([FromQuery] string entity, [FromQuery] int id)
        {
            _logger.LogInformation($"OnGetEditAsync: entity={entity}, id={id}");

            try
            {
                var config = ViewerRegistry.GetConfig(entity);
                if (config == null)
                    return NotFound();

                var item = await FindEntityByIdAsync(config.EntityType, id);
                if (item == null)
                    return NotFound($"Nie znaleziono rekordu o ID={id}");

                var result = new Dictionary<string, object?>();
                foreach (var col in config.Columns)
                {
                    var prop = config.EntityType.GetProperty(col.PropertyName);
                    result[col.PropertyName] = prop?.GetValue(item);
                }

                return new JsonResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Błąd w OnGetEditAsync");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnGetForeignKeyOptionsAsync([FromQuery] string entity)
        {
            try
            {
                var config = ViewerRegistry.GetConfig(entity);
                if (config == null)
                    return NotFound();

                var items = await GetItemsFromDbAsync(config.EntityType, new List<FilterCondition>(), null, null);
                if (items == null)
                    return NotFound();

                var idProp = config.EntityType.GetProperty("Id");

                var options = items.Select(item =>
                {
                    var id = idProp?.GetValue(item);
                    var text = EntityDescriptionHelper.GetDescription(item, config);

                    if (string.IsNullOrEmpty(text))
                        text = $"ID: {id}";

                    return new
                    {
                        id = id,
                        text = text
                    };
                }).ToList();

                return new JsonResult(options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Błąd podczas pobierania opcji FK");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnGetForeignKeyDescriptionAsync([FromQuery] string entity, [FromQuery] int id)
        {
            try
            {
                var config = ViewerRegistry.GetConfig(entity);
                if (config == null)
                    return NotFound();

                var item = await FindEntityByIdAsync(config.EntityType, id);
                if (item == null)
                    return new JsonResult(new { id = id, text = $"ID: {id}" });

                var text = EntityDescriptionHelper.GetDescription(item, config);
                if (string.IsNullOrEmpty(text))
                    text = $"ID: {id}";

                return new JsonResult(new { id = id, text = text });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Błąd podczas pobierania opisu FK");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnGetSearchAsync([FromQuery] string entity, [FromQuery] string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
                    return new JsonResult(new List<object>());

                var config = ViewerRegistry.GetConfig(entity);
                if (config == null)
                    return NotFound();

                var items = await GetItemsFromDbAsync(config.EntityType, new List<FilterCondition>(), null, null);
                if (items == null)
                    return new JsonResult(new List<object>());

                var idProp = config.EntityType.GetProperty("Id");
                var queryLower = query.ToLower();

                var results = items
                    .Select(item =>
                    {
                        var id = idProp?.GetValue(item);
                        var text = EntityDescriptionHelper.GetDescription(item, config);
                        if (string.IsNullOrEmpty(text))
                            text = $"ID: {id}";
                        return new { id, text };
                    })
                    .Where(x => x.text.ToLower().Contains(queryLower))
                    .Take(50)
                    .ToList();

                return new JsonResult(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Błąd podczas wyszukiwania: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync([FromForm] string entity, [FromForm] int id)
        {
            var config = ViewerRegistry.GetConfig(entity);
            if (config == null)
                return BadRequest("Nieprawidłowa konfiguracja");

            var item = await FindEntityByIdAsync(config.EntityType, id);
            if (item == null)
                return NotFound();

            try
            {
                _context.Remove(item);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"✅ Usunięto rekord ID={id}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Błąd podczas usuwania");
                TempData["ErrorMessage"] = $"❌ Błąd: {ex.Message}";
            }

            return RedirectToPage(new { entity });
        }

        public async Task<IActionResult> OnPostSaveAsync([FromForm] string entity, [FromForm] Dictionary<string, string> formData)
        {
            Config = ViewerRegistry.GetConfig(entity);

            var config = ViewerRegistry.GetConfig(entity);
            if (config == null)
                return BadRequest("Nieprawidłowa konfiguracja");

            if (!formData.ContainsKey("Id") || !int.TryParse(formData["Id"], out var id))
                return BadRequest("Brak ID");

            bool isNew = id == 0;
            object item;

            if (isNew)
            {
                // Utwórz nową instancję encji
                item = Activator.CreateInstance(config.EntityType)!;
                _context.Add(item);
            }
            else
            {
                var existing = await FindEntityByIdAsync(config.EntityType, id);
                if (existing == null)
                    return NotFound();
                item = existing;
            }

            try
            {
                foreach (var col in config.Columns.Where(c => c.IsEditable))
                {
                    if (formData.TryGetValue(col.PropertyName, out var value))
                    {
                        var prop = config.EntityType.GetProperty(col.PropertyName);
                        if (prop != null && prop.CanWrite)
                        {
                            var convertedValue = ConvertValue(value, col.Type);
                            prop.SetValue(item, convertedValue);
                        }
                    }
                }

                await _context.SaveChangesAsync();

                var savedId = config.EntityType.GetProperty("Id")?.GetValue(item);
                TempData["SuccessMessage"] = isNew
                    ? $"✅ Dodano nowy rekord ID={savedId}"
                    : $"✅ Zaktualizowano rekord ID={id}";
                return RedirectToPage(new { entity });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Błąd podczas zapisu");
                TempData["ErrorMessage"] = $"❌ Błąd: {ex.Message}";
                return RedirectToPage(new { entity });
            }
        }

        private async Task<List<object>?> GetItemsFromDbAsync(Type entityType, List<FilterCondition> filters, string? sortColumn, string? sortDirection)
        {
            try
            {
                var getItemsMethod = typeof(BrowseModel)
                    .GetMethod(nameof(GetItemsFromDbGenericAsync), BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.MakeGenericMethod(entityType);

                if (getItemsMethod == null)
                    return null;

                var task = getItemsMethod.Invoke(this, new object?[] { filters, sortColumn, sortDirection }) as Task<List<object>>;
                return task != null ? await task : null;
            }
            catch (TargetInvocationException tie)
            {
                _logger.LogError(tie.InnerException ?? tie, $"Błąd w GetItemsFromDbAsync dla {entityType.Name}");
                return new List<object>(); // Zwróć pustą listę zamiast null → nie pokaże NotFound
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Błąd w GetItemsFromDbAsync dla {entityType.Name}");
                return new List<object>();
            }
        }

        private async Task<List<object>> GetItemsFromDbGenericAsync<T>(List<FilterCondition> filters, string? sortColumn, string? sortDirection) where T : class
        {
            var query = _context.Set<T>().AsQueryable();
            var config = ViewerRegistry.GetConfig(typeof(T).Name);

            // Podziel filtry na te które da się zrobić w SQL (proste kolumny, FK po ID)
            // i te które wymagają filtrowania w pamięci (ścieżki z kropkami, FK po opisie)
            var sqlFilters    = new List<FilterCondition>();
            var memoryFilters = new List<FilterCondition>();

            foreach (var f in filters)
            {
                var colConfig = config?.Columns.FirstOrDefault(c => c.PropertyName == f.Column);

                // Do SQL kwalifikuje się tylko:
                // - prosta kolumna (nie FK, nie ścieżka z kropką) mapowana do bazy
                // - lub kolumna FK z wartością numeryczną i operatorem equals
                bool isPathFilter  = f.Column.Contains('.');
                bool isNumericFk   = colConfig?.IsForeignKey == true
                                     && int.TryParse(f.Value, out _)
                                     && f.Operator == "equals";
                bool isSimpleCol   = colConfig != null && !colConfig.IsForeignKey;

                if (!isPathFilter && (isNumericFk || isSimpleCol))
                    sqlFilters.Add(f);
                else
                    memoryFilters.Add(f);
            }

            // Zastosuj filtry SQL bezpośrednio na IQueryable
            if (sqlFilters.Any())
            {
                var parameter = Expression.Parameter(typeof(T), "x");
                Expression? combined = null;

                foreach (var f in sqlFilters)
                {
                    var colConfig = config?.Columns.FirstOrDefault(c => c.PropertyName == f.Column);
                    Expression? expr = null;

                    try
                    {
                        if (colConfig?.IsForeignKey == true && int.TryParse(f.Value, out var numVal))
                        {
                            var prop = Expression.Property(parameter, f.Column);
                            expr = Expression.Equal(prop, Expression.Constant(numVal));
                        }
                        else
                        {
                            var prop     = Expression.Property(parameter, f.Column);
                            var propStr  = Expression.Call(prop, typeof(object).GetMethod("ToString")!);
                            var valConst = Expression.Constant(f.Value);

                            expr = f.Operator switch
                            {
                                "contains"    => Expression.Call(propStr,
                                                    typeof(string).GetMethod("Contains", new[] { typeof(string) })!,
                                                    valConst),
                                "notcontains" => Expression.Not(Expression.Call(propStr,
                                                    typeof(string).GetMethod("Contains", new[] { typeof(string) })!,
                                                    valConst)),
                                "equals"      => Expression.Call(propStr,
                                                    typeof(string).GetMethod("Equals", new[] { typeof(string) })!,
                                                    valConst),
                                "notequals"   => Expression.Not(Expression.Call(propStr,
                                                    typeof(string).GetMethod("Equals", new[] { typeof(string) })!,
                                                    valConst)),
                                _             => Expression.Call(propStr,
                                                    typeof(string).GetMethod("Contains", new[] { typeof(string) })!,
                                                    valConst),
                            };
                        }

                        combined = CombineExpressions(combined, expr, f.Connector, f.OpenParens, f.CloseParens);
                    }
                    catch
                    {
                        // Właściwość nieznana EF (np. NotMapped) – przenieś do filtrowania w pamięci
                        memoryFilters.Add(f);
                    }
                }

                if (combined != null)
                    query = query.Where(Expression.Lambda<Func<T, bool>>(combined, parameter));
            }

            // Limit tylko gdy brak filtrów
            if (!filters.Any())
                query = query.Take(100);

            // Sortowanie
            if (!string.IsNullOrEmpty(sortColumn))
            {
                try
                {
                    var param      = Expression.Parameter(typeof(T), "x");
                    var sortProp   = Expression.Property(param, sortColumn);
                    var sortLambda = Expression.Lambda(sortProp, param);
                    var orderMethod = sortDirection?.ToLower() == "desc"
                        ? typeof(Queryable).GetMethods().First(m => m.Name == "OrderByDescending" && m.GetParameters().Length == 2)
                        : typeof(Queryable).GetMethods().First(m => m.Name == "OrderBy"           && m.GetParameters().Length == 2);
                    query = (IQueryable<T>)orderMethod.MakeGenericMethod(typeof(T), sortProp.Type)
                                                      .Invoke(null, new object[] { query, sortLambda })!;
                }
                catch { /* ignoruj błąd sortowania */ }
            }

            var allItems = await query.ToListAsync();

            // Filtrowanie w pamięci (ścieżki z kropkami, FK po opisie)
            if (memoryFilters.Any())
            {
                // Buduj grupy uwzględniające nawiasy i spójniki
                allItems = allItems.Where(item => EvaluateFilters(item, memoryFilters)).ToList();
            }

            return allItems.Cast<object>().ToList();
        }

        /// <summary>Łączy dwa wyrażenia spójnikiem AND/OR z uwzględnieniem nawiasów</summary>
        private static Expression CombineExpressions(
            Expression? left, Expression right,
            string connector, int openParens, int closeParens)
        {
            // Uproszczenie: nawiasy na tym etapie (SQL) traktujemy jako grupowanie AND/OR
            // Pełna obsługa nawiasów działa w EvaluateFilters (pamięć)
            if (left == null) return right;
            return connector.ToUpper() == "OR"
                ? Expression.OrElse(left, right)
                : Expression.AndAlso(left, right);
        }

        /// <summary>
        /// Ewaluuje listę warunków z nawiasami i spójnikami AND/OR w pamięci.
        /// Algorytm: zamień na listę tokenów (wartość bool + spójnik), uwzględniając nawiasy.
        /// </summary>
        private bool EvaluateFilters(object item, List<FilterCondition> conditions)
        {
            // Reprezentacja: lista (bool wynik, connector do następnego)
            // Obsługujemy nawiasy przez rekurencję na zagnieżdżonych grupach.
            var tokens = BuildTokens(item, conditions);
            return EvaluateTokens(tokens);
        }

        private List<(bool Value, string Connector)> BuildTokens(object item, List<FilterCondition> conditions)
        {
            // Rozwiń nawiasy spłaszczając - każdy warunek oceniamy osobno
            var flat = new List<(bool Value, string Connector, int Open, int Close)>();
            foreach (var f in conditions)
            {
                bool match = f.Column.Contains('.')
                    ? MatchByPath(item, f.Column, f.Operator, f.Value)
                    : MatchSingleFilter(item, f);
                flat.Add((match, f.Connector, f.OpenParens, f.CloseParens));
            }

            // Zbuduj wyrażenie boolean z nawiasami przez stos
            return flat.Select(t => (t.Value, t.Connector)).ToList();
        }

        private bool EvaluateTokens(List<(bool Value, string Connector)> tokens)
        {
            if (!tokens.Any()) return true;

            // Najpierw rozwiąż OR (niższy priorytet) po AND (wyższy priorytet)
            // Podziel na grupy AND, połącz OR-em
            var orGroups = new List<List<bool>>();
            var currentGroup = new List<bool> { tokens[0].Value };

            for (int i = 1; i < tokens.Count; i++)
            {
                var (val, conn) = tokens[i];
                if (conn.ToUpper() == "OR")
                {
                    orGroups.Add(currentGroup);
                    currentGroup = new List<bool> { val };
                }
                else
                {
                    currentGroup.Add(val);
                }
            }
            orGroups.Add(currentGroup);

            // Każda grupa AND: wszystkie muszą być true
            // Grupy OR: przynajmniej jedna musi być true
            return orGroups.Any(g => g.All(v => v));
        }

        private bool MatchSingleFilter(object item, FilterCondition f)
        {
            var prop = item.GetType().GetProperty(f.Column);
            if (prop == null) return false;
            var val = prop.GetValue(item)?.ToString() ?? string.Empty;
            return ApplyStringOperator(val, f.Operator, f.Value);
        }

        /// <summary>
        /// Porównuje wartość obiektu na ścieżce z kropkami (np. "GminaId.PowiatId.Nazwa")
        /// Segment "Opis" wywołuje metodę Opis(). Segmenty XxxId przeskakują do właściwości Xxx.
        /// </summary>
        private bool MatchByPath(object item, string path, string? op, string value)
        {
            var segments = path.Split('.');
            object? current = item;

            foreach (var segment in segments)
            {
                if (current == null) return false;

                if (segment == "Opis")
                {
                    var opisMethod = current.GetType()
                        .GetMethod("Opis", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    current = opisMethod?.Invoke(current, null);
                }
                else if (segment.EndsWith("Id") && current.GetType().GetProperty(segment[..^2]) is { } navProp)
                {
                    // XxxId -> przejdź przez właściwość nawigacyjną Xxx (załadowaną przez AutoInclude)
                    current = navProp.GetValue(current);
                }
                else
                {
                    current = current.GetType().GetProperty(segment)?.GetValue(current);
                }
            }

            return ApplyStringOperator(current?.ToString() ?? string.Empty, op, value);
        }

        private bool ApplyStringOperator(string text, string? op, string value)
        {
            return op?.ToLower() switch
            {
                "contains"    => text.Contains(value, StringComparison.OrdinalIgnoreCase),
                "notcontains" => !text.Contains(value, StringComparison.OrdinalIgnoreCase),
                "equals"      => text.Equals(value, StringComparison.OrdinalIgnoreCase),
                "notequals"   => !text.Equals(value, StringComparison.OrdinalIgnoreCase),
                _             => text.Contains(value, StringComparison.OrdinalIgnoreCase)
            };
        }

        private async Task<object?> FindEntityByIdAsync(Type entityType, int id)
        {
            var method = typeof(BrowseModel)
                .GetMethod(nameof(FindEntityByIdGenericAsync), BindingFlags.NonPublic | BindingFlags.Instance)
                ?.MakeGenericMethod(entityType);

            if (method == null)
                return null;

            var task = method.Invoke(this, new object[] { id }) as Task<object?>;
            return task != null ? await task : null;
        }

        private async Task<object?> FindEntityByIdGenericAsync<T>(int id) where T : class
        {
            var query = _context.Set<T>().AsQueryable();

            var idProperty = typeof(T).GetProperty("Id");
            if (idProperty == null)
                return null;

            var parameter = Expression.Parameter(typeof(T), "x");
            var property = Expression.Property(parameter, idProperty);
            var constant = Expression.Constant(id);
            var equality = Expression.Equal(property, constant);
            var lambda = Expression.Lambda<Func<T, bool>>(equality, parameter);

            return await query.FirstOrDefaultAsync(lambda);
        }

        private object? ConvertValue(string value, Type targetType)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (underlyingType == typeof(int))
                return int.Parse(value);
            if (underlyingType == typeof(decimal))
                return decimal.Parse(value);
            if (underlyingType == typeof(DateTime))
                return DateTime.Parse(value);
            if (underlyingType == typeof(bool))
                return bool.Parse(value);

            return value;
        }
    }

    public class ViewerConfigDto
    {
        public string EntityName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<ColumnConfigDto> Columns { get; set; } = new();
    }

    public class ColumnConfigDto
    {
        public string PropertyName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public bool IsFilterable { get; set; }
        public bool IsEditable { get; set; }
        public bool IsRequired { get; set; }
        public int MaxLength { get; set; }
        public bool IsForeignKey { get; set; }
        public string? ForeignKeyEntity { get; set; }
        public string? ForeignKeyDisplayProperty { get; set; }
        public string? ForeignKeyNavigationProperty { get; set; }
        public bool HasOpisMethod { get; set; }
        public string ChoiceMode { get; set; } = "Standard";
    }
}