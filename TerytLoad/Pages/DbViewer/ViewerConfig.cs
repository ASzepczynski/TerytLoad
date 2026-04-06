using System.Reflection;
using System.ComponentModel.DataAnnotations.Schema;

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

        /// <summary>
        /// Automatycznie generuje konfigurację z wykrywaniem Foreign Keys
        /// </summary>
        public static ViewerConfig FromType<T>(string displayName, string icon, string description) where T : class
        {
            var type = typeof(T);
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

                var column = new ColumnConfig
                {
                    PropertyName = prop.Name,
                    DisplayName = GetFriendlyName(prop.Name),
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
        /// Automatycznie wykrywa Foreign Key relationships
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
                    column.ForeignKeyEntity = navProp.PropertyType.Name;
                    // ✅ ZMIENIONE: Sprawdź czy encja ma metodę Opis()
                    column.HasOpisMethod = HasOpisMethod(navProp.PropertyType);
                }
                return;
            }

            if (property.Name.EndsWith("Id") && property.PropertyType == typeof(int))
            {
                var navigationPropertyName = property.Name.Substring(0, property.Name.Length - 2);
                var navigationProperty = entityType.GetProperty(navigationPropertyName);
                
                if (navigationProperty != null && !IsSimpleType(navigationProperty.PropertyType))
                {
                    column.IsForeignKey = true;
                    column.ForeignKeyEntity = navigationProperty.PropertyType.Name;
                    column.ForeignKeyNavigationProperty = navigationPropertyName;
                    // ✅ ZMIENIONE: Sprawdź czy encja ma metodę Opis()
                    column.HasOpisMethod = HasOpisMethod(navigationProperty.PropertyType);
                }
            }
        }

        /// <summary>
        /// ✅ ZMIENIONA: Sprawdza czy typ ma PROPERTY Opis zwracające string
        /// </summary>
        private static bool HasOpisMethod(Type type)
        {
            var property = type.GetProperty("Opis", BindingFlags.Public | BindingFlags.Instance);
            return property != null && property.PropertyType == typeof(string) && property.CanRead;
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
            if (prop.Name == "Id") return 0;
            if (prop.Name.EndsWith("Id")) return 1;
            return 2;
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
        /// ✅ NOWE: Czy encja FK ma metodę Opis()
        /// </summary>
        public bool HasOpisMethod { get; set; }
    }
}
