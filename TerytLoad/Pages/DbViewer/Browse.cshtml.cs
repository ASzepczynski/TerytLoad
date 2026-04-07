using AddressLibrary.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
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

        [BindProperty(SupportsGet = true)]
        public string? FilterColumn { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? FilterOperator { get; set; } = "contains";

        [BindProperty(SupportsGet = true)]
        public string? FilterValue { get; set; }

        // Kontekst rodzica (gdy przeglądamy relację dziecko)
        [BindProperty(SupportsGet = true)]
        public string? ParentEntity { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? ParentId { get; set; }

        public ViewerConfig? Config { get; set; }
        public List<object> Items { get; set; } = new();
        public object? ParentItem { get; set; }
        public string? ParentDescription { get; set; }

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
                    {
                        ParentDescription = EntityDescriptionHelper.GetDescription(ParentItem, parentConfig);
                    }
                }
            }

            var items = await GetItemsFromDbAsync(Config.EntityType, FilterColumn, FilterOperator, FilterValue);
            if (items == null)
                return NotFound($"Nie znaleziono DbSet dla: {Entity}");

            Items = items;
            return Page();
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

                var items = await GetItemsFromDbAsync(config.EntityType);
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

                var items = await GetItemsFromDbAsync(config.EntityType);
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

        public async Task<IActionResult> OnPostSaveAsync([FromForm] string entity, [FromForm] Dictionary<string, string> formData)
        {
            Config = ViewerRegistry.GetConfig(entity);

            var config = ViewerRegistry.GetConfig(entity);
            if (config == null)
                return BadRequest("Nieprawidłowa konfiguracja");

            if (!formData.ContainsKey("Id") || !int.TryParse(formData["Id"], out var id))
                return BadRequest("Brak ID");

            var existing = await FindEntityByIdAsync(config.EntityType, id);
            if (existing == null)
                return NotFound();

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
                            prop.SetValue(existing, convertedValue);
                        }
                    }
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"✅ Zaktualizowano rekord ID={id}";
                return RedirectToPage(new { entity });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Błąd podczas zapisu");
                TempData["ErrorMessage"] = $"❌ Błąd: {ex.Message}";
                return RedirectToPage(new { entity });
            }
        }

        private async Task<List<object>?> GetItemsFromDbAsync(Type entityType, string? filterColumn = null, string? filterOperator = null, string? filterValue = null)
        {
            try
            {
                var getItemsMethod = typeof(BrowseModel)
                    .GetMethod(nameof(GetItemsFromDbGenericAsync), BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.MakeGenericMethod(entityType);

                if (getItemsMethod == null)
                    return null;

                var task = getItemsMethod.Invoke(this, new object?[] { filterColumn, filterOperator, filterValue }) as Task<List<object>>;
                return task != null ? await task : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Błąd w GetItemsFromDbAsync dla {entityType.Name}");
                return null;
            }
        }

        private async Task<List<object>> GetItemsFromDbGenericAsync<T>(string? filterColumn, string? filterOperator, string? filterValue) where T : class
        {
            var query = _context.Set<T>().AsQueryable();

            if (!string.IsNullOrEmpty(filterColumn) && !string.IsNullOrEmpty(filterValue))
            {
                var config = ViewerRegistry.GetConfig(typeof(T).Name);
                var columnConfig = config?.Columns.FirstOrDefault(c => c.PropertyName == filterColumn);
                
                // Jeśli to kolumna FK
                if (columnConfig?.IsForeignKey == true && !string.IsNullOrEmpty(columnConfig.ForeignKeyNavigationProperty))
                {
                    // Sprawdź, czy filtrujemy po ID (operator "equals" i wartość numeryczna)
                    var isNumericFilter = int.TryParse(filterValue, out var numericValue) && 
                                         filterOperator?.ToLower() == "equals";
                    
                    if (isNumericFilter)
                    {
                        // Filtruj bezpośrednio po wartości FK (przez Expression)
                        var parameter = Expression.Parameter(typeof(T), "x");
                        var property = Expression.Property(parameter, filterColumn);
                        var constant = Expression.Constant(numericValue);
                        var equality = Expression.Equal(property, constant);
                        var lambda = Expression.Lambda<Func<T, bool>>(equality, parameter);
                        query = query.Where(lambda);
                    }
                    else
                    {
                        // Filtrowanie po opisie z właściwości nawigacyjnej (dla tekstowego wyszukiwania)
                        var allItems = await query.ToListAsync();
                        
                        var navPropertyName = columnConfig.ForeignKeyNavigationProperty;
                        var fkEntityName = columnConfig.ForeignKeyEntity!;
                        var currentFilterOperator = filterOperator;
                        var currentFilterValue = filterValue;
                        
                        var filtered = allItems.Where(item =>
                        {
                            var navProp = typeof(T).GetProperty(navPropertyName);
                            var navItem = navProp?.GetValue(item);
                            
                            if (navItem == null)
                                return false;
                            
                            var fkConfig = ViewerRegistry.GetConfig(fkEntityName);
                            var description = EntityDescriptionHelper.GetDescription(navItem, fkConfig);
                            
                            if (string.IsNullOrEmpty(description))
                                return false;

                            return currentFilterOperator?.ToLower() switch
                            {
                                "contains" => description.Contains(currentFilterValue, StringComparison.OrdinalIgnoreCase),
                                "notcontains" => !description.Contains(currentFilterValue, StringComparison.OrdinalIgnoreCase),
                                "equals" => description.Equals(currentFilterValue, StringComparison.OrdinalIgnoreCase),
                                "notequals" => !description.Equals(currentFilterValue, StringComparison.OrdinalIgnoreCase),
                                _ => description.Contains(currentFilterValue, StringComparison.OrdinalIgnoreCase)
                            };
                        }).ToList();
                        
                        return filtered.Cast<object>().ToList();
                    }
                }
                else
                {
                    // Standardowe filtrowanie dla zwykłych kolumn
                    var parameter = Expression.Parameter(typeof(T), "x");
                    var property = Expression.Property(parameter, filterColumn);
                    var propertyAsString = Expression.Call(property, typeof(object).GetMethod("ToString")!);
                    var valueExpression = Expression.Constant(filterValue, typeof(string));

                    Expression? filterExpression = null;

                    switch (filterOperator?.ToLower())
                    {
                        case "contains":
                            var containsMethod = typeof(string).GetMethod("Contains", new[] { typeof(string) })!;
                            filterExpression = Expression.Call(propertyAsString, containsMethod, valueExpression);
                            break;

                        case "notcontains":
                            var notContainsMethod = typeof(string).GetMethod("Contains", new[] { typeof(string) })!;
                            var containsCall = Expression.Call(propertyAsString, notContainsMethod, valueExpression);
                            filterExpression = Expression.Not(containsCall);
                            break;

                        case "equals":
                            var equalsMethod = typeof(string).GetMethod("Equals", new[] { typeof(string) })!;
                            filterExpression = Expression.Call(propertyAsString, equalsMethod, valueExpression);
                            break;

                        case "notequals":
                            var notEqualsMethod = typeof(string).GetMethod("Equals", new[] { typeof(string) })!;
                            var equalsCall = Expression.Call(propertyAsString, notEqualsMethod, valueExpression);
                            filterExpression = Expression.Not(equalsCall);
                            break;
                    }

                    if (filterExpression != null)
                    {
                        var lambda = Expression.Lambda<Func<T, bool>>(filterExpression, parameter);
                        query = query.Where(lambda);
                    }
                }
            }

            if (string.IsNullOrEmpty(filterColumn) || string.IsNullOrEmpty(filterValue))
            {
                query = query.Take(100);
            }

            var results = await query.ToListAsync();
            return results.Cast<object>().ToList();
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