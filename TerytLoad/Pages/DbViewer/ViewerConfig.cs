using System.Reflection;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using AddressLibrary.Attributes;

namespace TerytLoad.Pages.DbViewer
{
    /// <summary>
    /// Konfiguracja słownika do przeglądania
    /// </summary>
    public class ViewerConfig
    {
        public string EntityName { get; set; } = string.Empty;
        public Type EntityType { get; set; } = null!;
        public string DisplayName { get; set; } = string.Empty;
        public string Icon { get; set; } = "📄";
        public string Description { get; set; } = string.Empty;
        public List<ColumnConfig> Columns { get; set; } = new();
        public List<ChildRelationship> ChildRelationships { get; set; } = new();

        /// <summary>
        /// Automatycznie generuje konfigurację z wykrywaniem Foreign Keys
        /// </summary>
        public static ViewerConfig FromType<T>(string displayName, string icon, string? descriptionOverride = null) where T : class
        {
            var type = typeof(T);
            
            // Pobierz Description z atrybutu TableParam
            var tableParam = type.GetCustomAttribute<TableParamAttribute>();
            var description = descriptionOverride ?? tableParam?.Description ?? $"Przeglądaj i edytuj rekordy {type.Name}";
            
            var config = new ViewerConfig
            {
                EntityName = type.Name,
                EntityType = type,
                DisplayName = displayName,
                Icon = icon,
                Description = description
            };

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead)
                .OrderBy(p => GetPropertyOrder(p))
                .ToList();

            foreach (var prop in properties)
            {
                if (!IsSimpleType(prop.PropertyType) && !IsForeignKeyProperty(prop))
                {
                    continue;
                }

                // Pobierz MemberParam z właściwości
                var memberParam = prop.GetCustomAttribute<MemberParamAttribute>();

                // Pomiń właściwości oznaczone jako niewidoczne
                if (memberParam?.Visible == false)
                    continue;
                
                var column = new ColumnConfig
                {
                    PropertyName = prop.Name,
                    DisplayName = memberParam?.Description ?? GetFriendlyName(prop.Name),
                    Type = prop.PropertyType,
                    IsFilterable = IsSimpleType(prop.PropertyType),
                    IsEditable = prop.CanWrite && prop.Name != "Id",
                    IsRequired = !IsNullable(prop.PropertyType)
                };

                DetectForeignKey(type, prop, column);

                config.Columns.Add(column);
            }

            return config;
        }

        /// <summary>
        /// Automatycznie wykrywa Foreign Key relationships i pobiera Choice z klasy docelowej
        /// </summary>
        private static void DetectForeignKey(Type entityType, PropertyInfo property, ColumnConfig column)
        {
            var foreignKeyAttr = property.GetCustomAttribute<ForeignKeyAttribute>();
            
            if (foreignKeyAttr != null)
            {
                column.IsForeignKey = true;
                column.ForeignKeyNavigationProperty = foreignKeyAttr.Name;
                
                var navProp = entityType.GetProperty(foreignKeyAttr.Name);
                if (navProp != null)
                {
                    var targetType = navProp.PropertyType;
                    column.ForeignKeyEntity = targetType.Name;
                    column.HasOpisMethod = HasOpisMethod(targetType);
                    
                    // ✅ KLUCZOWA ZMIANA: Pobierz Choice z klasy DOCELOWEJ
                    var targetTableParam = targetType.GetCustomAttribute<TableParamAttribute>();
                    column.ChoiceMode = targetTableParam?.Choice ?? ChoiceMode.Standard;
                }
                
                return;
            }

            if (property.Name.EndsWith("Id") && property.PropertyType == typeof(int))
            {
                var navigationPropertyName = property.Name.Substring(0, property.Name.Length - 2);
                var navigationProperty = entityType.GetProperty(navigationPropertyName);
                
                if (navigationProperty != null && !IsSimpleType(navigationProperty.PropertyType))
                {
                    var targetType = navigationProperty.PropertyType;
                    
                    column.IsForeignKey = true;
                    column.ForeignKeyEntity = targetType.Name;
                    column.ForeignKeyNavigationProperty = navigationPropertyName;
                    column.HasOpisMethod = HasOpisMethod(targetType);
                    
                    // ✅ KLUCZOWA ZMIANA: Pobierz Choice z klasy DOCELOWEJ
                    var targetTableParam = targetType.GetCustomAttribute<TableParamAttribute>();
                    column.ChoiceMode = targetTableParam?.Choice ?? ChoiceMode.Standard;
                }
            }
        }

        /// <summary>
        /// Sprawdza czy typ ma METODĘ Opis() zwracającą string
        /// </summary>
        private static bool HasOpisMethod(Type type)
        {
            var method = type.GetMethod("Opis", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            return method != null && method.ReturnType == typeof(string);
        }

        private static string GetFriendlyName(string propertyName)
        {
            if (propertyName == "Id") return "ID";
            
            var result = System.Text.RegularExpressions.Regex.Replace(
                propertyName, 
                "([A-Z])", 
                " $1", 
                System.Text.RegularExpressions.RegexOptions.Compiled
            ).Trim();
            
            return result;
        }

        private static int GetPropertyOrder(PropertyInfo prop)
        {
            // Zwróć MetadataToken, który reprezentuje kolejność deklaracji w kodzie źródłowym
            return prop.MetadataToken;
        }

        private static bool IsForeignKeyProperty(PropertyInfo prop)
        {
            return prop.Name.EndsWith("Id") && prop.PropertyType == typeof(int);
        }

        private static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive || 
                   type == typeof(string) || 
                   type == typeof(decimal) || 
                   type == typeof(DateTime) ||
                   Nullable.GetUnderlyingType(type) != null;
        }

        private static bool IsNullable(Type type)
        {
            return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
        }
    }

    /// <summary>
    /// Konfiguracja pojedynczej kolumny
    /// </summary>
    public class ColumnConfig
    {
        public string PropertyName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public Type Type { get; set; } = typeof(string);
        public bool IsFilterable { get; set; } = true;
        public bool IsEditable { get; set; } = true;
        public bool IsRequired { get; set; }
        public int MaxLength { get; set; }
        
        public bool IsForeignKey { get; set; }
        public string? ForeignKeyEntity { get; set; }
        public string? ForeignKeyNavigationProperty { get; set; }
        public string? ForeignKeyDisplayProperty { get; set; }
        
        /// <summary>
        /// Czy encja FK ma metodę Opis()
        /// </summary>
        public bool HasOpisMethod { get; set; }
        
        /// <summary>
        /// Tryb wyboru referencji - pochodzi z TableParam klasy DOCELOWEJ
        /// </summary>
        public ChoiceMode ChoiceMode { get; set; } = ChoiceMode.Standard;
    }

    /// <summary>
    /// Opcja filtrowania - może być zwykłą kolumną lub ścieżką przez relacje FK (np. "GminaId.Powiat.Nazwa")
    /// </summary>
    public class FilterOption
    {
        /// <summary>
        /// Ścieżka filtrowania np. "GminaId", "GminaId.Opis", "GminaId.PowiatId.Nazwa"
        /// </summary>
        public string FilterPath { get; set; } = string.Empty;

        /// <summary>
        /// Etykieta wyświetlana na liście wyboru np. "Gmina.Opis", "Gmina.Powiat.Nazwa"
        /// </summary>
        public string DisplayLabel { get; set; } = string.Empty;

        /// <summary>
        /// Czy to jest filtrowanie po zwykłej kolumnie (nie FK)
        /// </summary>
        public bool IsDirectColumn { get; set; }

        /// <summary>
        /// Buduje listę opcji filtrowania dla danej konfiguracji encji (z rekurencją przez FK)
        /// </summary>
        public static List<FilterOption> BuildFor(ViewerConfig config, int maxDepth = 3)
        {
            var result = new List<FilterOption>();
            BuildRecursive(config, "", "", result, maxDepth, 0, new HashSet<string>());
            return result;
        }

        private static void BuildRecursive(
            ViewerConfig config,
            string pathPrefix,
            string labelPrefix,
            List<FilterOption> result,
            int maxDepth,
            int depth,
            HashSet<string> visitedEntities)
        {
            if (depth > maxDepth) return;
            if (!visitedEntities.Add(config.EntityName)) return; // unikaj cykli

            foreach (var col in config.Columns.Where(c => c.IsFilterable))
            {
                if (col.IsForeignKey && !string.IsNullOrEmpty(col.ForeignKeyEntity))
                {
                    var fkConfig = ViewerRegistry.GetConfig(col.ForeignKeyEntity);
                    if (fkConfig == null) continue;

                    // Nawigacja pośrednia: używamy nazwy właściwości nawigacyjnej (bez "Id")
                    var navName = col.ForeignKeyNavigationProperty ?? col.PropertyName.Replace("Id", "");
                    var fkLabel = string.IsNullOrEmpty(labelPrefix)
                        ? navName
                        : $"{labelPrefix}.{navName}";
                    var fkPath = string.IsNullOrEmpty(pathPrefix)
                        ? col.PropertyName
                        : $"{pathPrefix}.{col.PropertyName}";

                    // Opcja: filtrowanie po Opis() tej encji FK - "Gmina.Opis"
                    if (fkConfig.EntityType != null && HasOpisMethod(fkConfig.EntityType))
                    {
                        result.Add(new FilterOption
                        {
                            FilterPath = $"{fkPath}.Opis",
                            DisplayLabel = $"{fkLabel}.Opis",
                            IsDirectColumn = false
                        });
                    }

                    // Opcja: filtrowanie po każdym polu tekstowym tej encji FK
                    // Ostatni segment = DisplayName z atrybutu, wcześniejsze = nazwa nawigacji
                    foreach (var nestedCol in fkConfig.Columns.Where(c => c.IsFilterable && !c.IsForeignKey && c.Type == typeof(string)))
                    {
                        result.Add(new FilterOption
                        {
                            FilterPath = $"{fkPath}.{nestedCol.PropertyName}",
                            DisplayLabel = $"{fkLabel}.{nestedCol.DisplayName}",
                            IsDirectColumn = false
                        });
                    }

                    // Rekurencja w głąb kolejnych relacji FK
                    BuildRecursive(fkConfig, fkPath, fkLabel, result, maxDepth, depth + 1,
                        new HashSet<string>(visitedEntities));
                }
                else
                {
                    // Zwykła kolumna - dodaj tylko na poziomie 0 (są już w głównej liście)
                    if (depth == 0)
                    {
                        result.Add(new FilterOption
                        {
                            FilterPath = col.PropertyName,
                            DisplayLabel = col.DisplayName,
                            IsDirectColumn = true
                        });
                    }
                }
            }

            visitedEntities.Remove(config.EntityName);
        }

        private static bool HasOpisMethod(Type type)
        {
            var method = type.GetMethod("Opis", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            return method != null && method.ReturnType == typeof(string);
        }
    }

    /// <summary>
    /// Reprezentuje relację do encji-dziecka (encja, która ma FK do tego rejestru)
    /// </summary>
    public class ChildRelationship
    {
        /// <summary>
        /// Nazwa encji-dziecka (np. "Gmina")
        /// </summary>
        public string ChildEntityName { get; set; } = string.Empty;

        /// <summary>
        /// Nazwa wyświetlana encji-dziecka (np. "Gminy")
        /// </summary>
        public string ChildDisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Ikona encji-dziecka
        /// </summary>
        public string ChildIcon { get; set; } = "📄";

        /// <summary>
        /// Nazwa właściwości FK w encji-dziecku (np. "WojewodztwoId")
        /// </summary>
        public string ForeignKeyPropertyName { get; set; } = string.Empty;
    }
}
