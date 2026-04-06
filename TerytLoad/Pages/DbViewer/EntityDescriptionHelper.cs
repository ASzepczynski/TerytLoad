using System.Reflection;

namespace TerytLoad.Pages.DbViewer
{
    /// <summary>
    /// Pomocnik do generowania opisów encji
    /// </summary>
    public static class EntityDescriptionHelper
    {
        /// <summary>
        /// Zwraca opis encji: wywołuje Opis() jeśli istnieje, inaczej konkatenuje pola przez |
        /// </summary>
        public static string GetDescription<T>(T entity) where T : class
        {
            if (entity == null)
                return string.Empty;

            var type = entity.GetType();

            // ✅ ZMIENIONO: Sprawdź czy istnieje METODA Opis()
            var opisMethod = type.GetMethod("Opis", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (opisMethod != null && opisMethod.ReturnType == typeof(string))
            {
                return opisMethod.Invoke(entity, null)?.ToString() ?? string.Empty;
            }

            // Fallback: konkatenacja wszystkich prostych pól (pomijamy Id i FK)
            return GetConcatenatedFields(entity);
        }

        /// <summary>
        /// Zwraca opis encji z użyciem konfiguracji ViewerConfig
        /// </summary>
        public static string GetDescription(object entity, ViewerConfig? config = null)
        {
            if (entity == null)
                return string.Empty;

            var type = entity.GetType();

            // ✅ ZMIENIONO: Sprawdź czy istnieje METODA Opis()
            var opisMethod = type.GetMethod("Opis", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (opisMethod != null && opisMethod.ReturnType == typeof(string))
            {
                return opisMethod.Invoke(entity, null)?.ToString() ?? string.Empty;
            }

            // Użyj ViewerConfig jeśli dostępny
            if (config != null)
            {
                var values = config.Columns
                    .Where(c => c.PropertyName != "Id" && !c.IsForeignKey)
                    .Select(c => type.GetProperty(c.PropertyName)?.GetValue(entity)?.ToString() ?? "")
                    .Where(v => !string.IsNullOrEmpty(v));

                return string.Join(" | ", values);
            }

            // Fallback: konkatenacja wszystkich prostych pól
            return GetConcatenatedFields(entity);
        }

        /// <summary>
        /// Konkatenuje wszystkie proste pola encji (pomija Id, navigation properties)
        /// </summary>
        private static string GetConcatenatedFields(object entity)
        {
            var type = entity.GetType();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && 
                           p.Name != "Id" && 
                           IsSimpleType(p.PropertyType))
                .OrderBy(p => p.Name);

            var values = properties
                .Select(p => p.GetValue(entity)?.ToString() ?? "")
                .Where(v => !string.IsNullOrEmpty(v));

            return string.Join(" | ", values);
        }

        /// <summary>
        /// Sprawdza czy typ jest prosty (nie jest obiektem złożonym)
        /// </summary>
        private static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive ||
                   type == typeof(string) ||
                   type == typeof(decimal) ||
                   type == typeof(DateTime) ||
                   type == typeof(Guid) ||
                   Nullable.GetUnderlyingType(type) != null;
        }
    }
}