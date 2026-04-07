using AddressLibrary.Data;
using Microsoft.EntityFrameworkCore;

namespace TerytLoad.Pages.DbViewer
{
    /// <summary>
    /// Rejestr automatycznie wykrywa słowniki z DbContext
    /// </summary>
    public static class ViewerRegistry
    {
        private static readonly Dictionary<string, ViewerConfig> _configs = new();
        private static bool _initialized = false;

        public static void InitializeFromDbContext(AddressDbContext context)
        {
            if (_initialized) return;

            var dbContextType = typeof(AddressDbContext);

            var dbSetProperties = dbContextType.GetProperties()
                .Where(p => p.PropertyType.IsGenericType &&
                           p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                .ToList();

            foreach (var dbSetProperty in dbSetProperties)
            {
                var entityType = dbSetProperty.PropertyType.GetGenericArguments()[0];
                var entityName = entityType.Name;

                if (entityName.StartsWith("Teryt") || entityName == "Pna" || entityName == "Adres")
                    continue;

                var configMethod = typeof(ViewerConfig)
                    .GetMethod(nameof(ViewerConfig.FromType))
                    ?.MakeGenericMethod(entityType);

                if (configMethod != null)
                {
                    var icon = GetIconForEntity(entityName);
                    var displayName = GetDisplayNameForEntity(entityName);

                    // Nie przekazuj description - niech FromType użyje atrybutu TableParam
                    var config = configMethod.Invoke(null, new object?[] { displayName, icon, null }) as ViewerConfig;

                    if (config != null)
                    {
                        _configs[entityName] = config;
                    }
                }
            }

            // Druga pętla: wykryj relacje dzieci (child relationships)
            DetectChildRelationships();

            _initialized = true;
        }

        private static void DetectChildRelationships()
        {
            foreach (var parentConfig in _configs.Values)
            {
                foreach (var childConfig in _configs.Values)
                {
                    if (parentConfig.EntityName == childConfig.EntityName)
                        continue;

                    // Sprawdź, czy childConfig ma FK do parentConfig
                    var fkColumn = childConfig.Columns.FirstOrDefault(c =>
                        c.IsForeignKey && c.ForeignKeyEntity == parentConfig.EntityName);

                    if (fkColumn != null)
                    {
                        parentConfig.ChildRelationships.Add(new ChildRelationship
                        {
                            ChildEntityName = childConfig.EntityName,
                            ChildDisplayName = childConfig.DisplayName,
                            ChildIcon = childConfig.Icon,
                            ForeignKeyPropertyName = fkColumn.PropertyName
                        });
                    }
                }
            }
        }

        private static string GetIconForEntity(string entityName)
        {
            return entityName switch
            {
                "CechaUlicy" => "🛣️",
                "TytulStopien" => "🎖️",
                "TypUlicy" => "👤",
                "RodzajGminy" => "🏛️",
                "RodzajMiasta" => "🏙️",
                "Wojewodztwo" => "🗺️",
                "Powiat" => "📍",
                "Gmina" => "🏘️",
                "Miasto" => "🌆",
                "Ulica" => "🛤️",
                "KodPocztowy" => "📮",
                "UrzadSkarbowy" => "🏦",
                "TerytUlicPoprawka" => "✏️",
                _ => "📄"
            };
        }

        private static string GetDisplayNameForEntity(string entityName)
        {
            return entityName switch
            {
                "CechaUlicy" => "Cechy Ulic",
                "TytulStopien" => "Tytuły i Stopnie",
                "TypUlicy" => "Typy Ulic",
                "RodzajGminy" => "Rodzaje Gmin",
                "RodzajMiasta" => "Rodzaje Miast",
                "Wojewodztwo" => "Województwa",
                "Powiat" => "Powiaty",
                "Gmina" => "Gminy",
                "Miasto" => "Miasta",
                "Ulica" => "Ulice",
                "KodPocztowy" => "Kody Pocztowe",
                "UrzadSkarbowy" => "Urzędy Skarbowe",
                "TerytUlicPoprawka" => "Poprawki Ulic TERYT",
                _ => entityName
            };
        }

        public static void Register(ViewerConfig config)
        {
            _configs[config.EntityName] = config;
        }

        public static ViewerConfig? GetConfig(string entityName)
        {
            return _configs.TryGetValue(entityName, out var config) ? config : null;
        }

        public static IEnumerable<ViewerConfig> GetAllConfigs()
        {
            return _configs.Values;
        }
    }
}
