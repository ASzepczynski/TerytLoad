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
                    var description = GetDescriptionForEntity(entityName);

                    var config = configMethod.Invoke(null, new object[] { displayName, icon, description }) as ViewerConfig;

                    if (config != null)
                    {
                        _configs[entityName] = config;
                    }
                }
            }

            _initialized = true;
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

        private static string GetDescriptionForEntity(string entityName)
        {
            return entityName switch
            {
                "CechaUlicy" => "Przeglądaj i edytuj cechy ulic (ul., al., pl., itp.)",
                "TytulStopien" => "Przeglądaj i edytuj tytuły i stopnie (gen., płk., dr., itp.)",
                "TypUlicy" => "Przeglądaj i edytuj typy ulic (osobowe: imiona, nazwiska)",
                "RodzajGminy" => "Przeglądaj i edytuj rodzaje gmin",
                "RodzajMiasta" => "Przeglądaj i edytuj rodzaje miast",
                "Wojewodztwo" => "Przeglądaj i edytuj województwa",
                "Powiat" => "Przeglądaj i edytuj powiaty",
                "Gmina" => "Przeglądaj i edytuj gminy",
                "Miasto" => "Przeglądaj i edytuj miasta",
                "Ulica" => "Przeglądaj i edytuj ulice",
                "KodPocztowy" => "Przeglądaj i edytuj kody pocztowe",
                "UrzadSkarbowy" => "Przeglądaj i edytuj urzędy skarbowe",
                "TerytUlicPoprawka" => "Przeglądaj i edytuj poprawki nazw ulic z TERYT",
                _ => $"Przeglądaj i edytuj rekordy {entityName}"
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
