using AddressLibrary.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Text.Json;
using System.ComponentModel.DataAnnotations.Schema;

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

        public ViewerConfig? Config { get; set; }
        public List<object> Items { get; set; } = new();

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
                HasOpisMethod = c.HasOpisMethod // ✅ DODANE
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

            var items = await GetItemsFromDbAsync(Config.EntityType);
            if (items == null)
                return NotFound($"Nie znaleziono DbSet dla: {Entity}");

            if (!string.IsNullOrEmpty(FilterColumn) && !string.IsNullOrEmpty(FilterValue))
            {
                items = ApplyFilter(items, Config, FilterColumn, FilterOperator!, FilterValue);
            }
            else
            {
                items = items.Take(100).ToList();
            }

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

        /// <summary>
        /// ✅ UPROSZCZONE: Użyj EntityDescriptionHelper
        /// </summary>
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

        /// <summary>
        /// ✅ NOWA METODA: Pobiera opis dla Foreign Key (dla wyświetlenia w tabeli)
        /// </summary>
        //public async Task<IActionResult> OnGetForeignKeyDisplayAsync([FromQuery] string entity, [FromQuery] int id)
        //{
        //    try
        //    {
        //        var config = ViewerRegistry.GetConfig(entity);
        //        if (config == null)
        //            return NotFound();

        //        var item = await FindEntityByIdAsync(config.EntityType, id);
        //        if (item == null)
        //            return NotFound();

        //        var opisMethod = config.EntityType.GetMethod("Opis", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                
        //        string text;
        //        if (opisMethod != null && opisMethod.ReturnType == typeof(string))
        //        {
        //            text = opisMethod.Invoke(item, null)?.ToString() ?? $"ID: {id}";
        //        }
        //        else
        //        {
        //            var displayProperties = config.Columns
        //                .Where(c => c.PropertyName != "Id" && !c.IsForeignKey)
        //                .Select(c => config.EntityType.GetProperty(c.PropertyName))
        //                .Where(p => p != null)
        //                .ToList();

        //            var values = displayProperties
        //                .Select(p => p?.GetValue(item)?.ToString() ?? "")
        //                .Where(v => !string.IsNullOrEmpty(v));
                    
        //            text = string.Join(" | ", values);
        //            if (string.IsNullOrEmpty(text))
        //                text = $"ID: {id}";
        //        }

        //        return new JsonResult(new { text });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, $"Błąd podczas pobierania FK display");
        //        return StatusCode(500, new { error = ex.Message });
        //    }
        //}

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

        private async Task<List<object>?> GetItemsFromDbAsync(Type entityType)
        {
            try
            {
                // ✅ UPROSZCZONE: Użyj generycznej metody
                var getItemsMethod = typeof(BrowseModel)
                    .GetMethod(nameof(GetItemsFromDbGenericAsync), BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.MakeGenericMethod(entityType);

                if (getItemsMethod == null)
                {
                    _logger.LogWarning("Nie znaleziono metody GetItemsFromDbGenericAsync");
                    return null;
                }

                var task = getItemsMethod.Invoke(this, null) as Task<List<object>>;
                if (task == null)
                    return null;

                return await task;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Błąd podczas pobierania danych");
                return null;
            }
        }

        /// <summary>
        /// ✅ UNIWERSALNA METODA: Automatycznie aplikuje Include dla wszystkich navigation properties (z zagnieżdżeniami)
        /// </summary>
        private IQueryable<T> ApplyIncludes<T>(IQueryable<T> query) where T : class
        {
            var entityType = typeof(T);
            var includePaths = GetIncludePathsForEntity(entityType, maxDepth: 3);
            
            foreach (var path in includePaths)
            {
                query = query.Include(path);
            }
            
            return query;
        }

        /// <summary>
        /// ✅ REKURENCYJNA METODA: Znajduje wszystkie ścieżki Include dla encji
        /// </summary>
        private List<string> GetIncludePathsForEntity(Type entityType, int maxDepth, int currentDepth = 0, HashSet<Type>? visitedTypes = null)
        {
            var paths = new List<string>();
            
            if (currentDepth >= maxDepth)
                return paths;

            visitedTypes ??= new HashSet<Type>();
            if (visitedTypes.Contains(entityType))
                return paths;
                
            visitedTypes.Add(entityType);

            // Znajdź wszystkie navigation properties (reference, nie kolekcje)
            var navigationProperties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => IsNavigationProperty(p))
                .ToList();

            foreach (var navProp in navigationProperties)
            {
                var navigationName = navProp.Name;
                var relatedType = navProp.PropertyType;
                
                // Jeśli nullable, weź underlying type
                if (Nullable.GetUnderlyingType(relatedType) != null)
                    relatedType = Nullable.GetUnderlyingType(relatedType)!;

                // Dodaj bezpośrednią navigation property
                paths.Add(navigationName);

                // Rekurencyjnie znajdź zagnieżdżone navigation properties
                var visitedCopy = new HashSet<Type>(visitedTypes);
                var nestedPaths = GetIncludePathsForEntity(relatedType, maxDepth, currentDepth + 1, visitedCopy);
                
                foreach (var nestedPath in nestedPaths)
                {
                    paths.Add($"{navigationName}.{nestedPath}");
                }
            }

            return paths.Distinct().ToList();
        }

        /// <summary>
        /// ✅ Sprawdza czy property jest navigation property (FK reference, nie kolekcja)
        /// </summary>
        private bool IsNavigationProperty(PropertyInfo property)
        {
            // Pomijaj kolekcje (ICollection, IEnumerable, List, etc.)
            if (IsCollection(property.PropertyType))
                return false;

            // Pomijaj proste typy
            if (IsSimpleType(property.PropertyType))
                return false;

            // Sprawdź czy ma atrybut [ForeignKey]
            if (property.GetCustomAttribute<ForeignKeyAttribute>() != null)
                return true;

            // Sprawdź czy jest klasą z namespace zawierającego "Models"
            var propertyType = property.PropertyType;
            if (Nullable.GetUnderlyingType(propertyType) != null)
                propertyType = Nullable.GetUnderlyingType(propertyType)!;

            if (propertyType.IsClass && 
                propertyType != typeof(string) &&
                propertyType.Namespace?.Contains("Models") == true)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Sprawdza czy typ jest kolekcją
        /// </summary>
        private bool IsCollection(Type type)
        {
            return type != typeof(string) && 
                   typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
        }

        /// <summary>
        /// Sprawdza czy typ jest prosty (nie jest obiektem złożonym)
        /// </summary>
        private bool IsSimpleType(Type type)
        {
            return type.IsPrimitive ||
                   type == typeof(string) ||
                   type == typeof(decimal) ||
                   type == typeof(DateTime) ||
                   type == typeof(Guid) ||
                   Nullable.GetUnderlyingType(type) != null;
        }

        /// <summary>
        /// ✅ UPROSZCZONE: Użyj ApplyIncludes
        /// </summary>
        private async Task<List<object>> GetItemsFromDbGenericAsync<T>() where T : class
        {
            IQueryable<T> query = _context.Set<T>();
            query = ApplyIncludes(query);
            
            var items = await query.ToListAsync();
            return items.Cast<object>().ToList();
        }

        /// <summary>
        /// ✅ UPROSZCZONE: Użyj ApplyIncludes
        /// </summary>
        private async Task<object?> FindEntityByIdGenericAsync<T>(int id) where T : class
        {
            IQueryable<T> query = _context.Set<T>();
            query = ApplyIncludes(query);
            
            var entity = await query.FirstOrDefaultAsync(e => EF.Property<int>(e, "Id") == id);
            return entity;
        }

        private async Task<object?> FindEntityByIdAsync(Type entityType, int id)
        {
            try
            {
                var findMethod = typeof(BrowseModel)
                    .GetMethod(nameof(FindEntityByIdGenericAsync), BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.MakeGenericMethod(entityType);

                if (findMethod == null)
                    return null;

                var task = findMethod.Invoke(this, new object[] { id }) as Task<object>;
                if (task == null)
                    return null;

                return await task;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Błąd podczas wyszukiwania encji");
                return null;
            }
        }

        private List<object> ApplyFilter(List<object> items, ViewerConfig config, string columnName, string op, string value)
        {
            var property = config.EntityType.GetProperty(columnName);
            if (property == null)
                return items;

            return items.Where(item =>
            {
                var propValue = property.GetValue(item)?.ToString() ?? "";

                return op switch
                {
                    "contains" => propValue.Contains(value, StringComparison.OrdinalIgnoreCase),
                    "notcontains" => !propValue.Contains(value, StringComparison.OrdinalIgnoreCase),
                    "equals" => propValue.Equals(value, StringComparison.OrdinalIgnoreCase),
                    "notequals" => !propValue.Equals(value, StringComparison.OrdinalIgnoreCase),
                    _ => true
                };
            }).ToList();
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

    // ✅ TYLKO DTO (Data Transfer Objects) - nie duplikujemy ViewerConfig i ColumnConfig
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
        public bool HasOpisMethod { get; set; } // ✅ DODANE
    }
}