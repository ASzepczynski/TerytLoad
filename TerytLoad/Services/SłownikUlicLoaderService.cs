using AddressLibrary;
using AddressLibrary;
using AddressLibrary.Data;
using AddressLibrary.Dictionaries;
using AddressLibrary.Dictionaries.TytulyStopnie;
using AddressLibrary.Helpers;
using AddressLibrary.Logging;
using AddressLibrary.Models;
using AddressLibrary.Services;
using Microsoft.EntityFrameworkCore;

namespace TerytLoad.Services
{
    public class S³ownikUlicLoaderResult
    {
        public int InsertedCountPoprawki { get; set; }
        public int TotalTypyUlic { get; set; }
        public int FoundTypyUlic { get; set; }
        public int NotFoundTypyUlic { get; set; }
    }

    public class S³ownikUlicLoaderService
    {
        public async Task<S³ownikUlicLoaderResult> LoadAsync(
            string connectionString,
            string appDataPath,
            IProgress<(string operation, int current, int total)>? progress = null)
        {
            LoadTerytUlicPoprawkiService? poprawkiLoader = null;
            Dictionary<string, TerytUlicPoprawka>? sharedDictionary = null;

            var db = new AddressDatabase(connectionString, appDataPath);
            var context = db.GetContext();

            // ========================================
            // KROK 1: Za³aduj TerytUlicPoprawki
            // ========================================
            poprawkiLoader = new LoadTerytUlicPoprawkiService(context, appDataPath);

            var progressPoprawki = new Progress<LoadProgress>(p =>
            {
                progress?.Report(($"[1/2] TerytUlicPoprawki: {p.CurrentOperation}", p.ProcessedCount, p.TotalCount));
            });

            var resultPoprawki = await poprawkiLoader.LoadAsync(progressPoprawki);

            if (!string.IsNullOrEmpty(resultPoprawki.ErrorMessage))
                throw new Exception($"B³¹d ³adowania TerytUlicPoprawki: {resultPoprawki.ErrorMessage}");

            poprawkiLoader.Dispose();
            poprawkiLoader = null;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            await Task.Delay(3000);

            // ========================================
            // KROK 2: Za³aduj s³ownik z pliku Excel
            // ========================================
            progress?.Report(("[2/2] TypyUlic: £adowanie s³ownika z Excel...", 0, 0));

            var tempLogger = new PostalCodesLogger(appDataPath, "LoadTypyUlic.txt");
            await tempLogger.InitializeAsync();

            sharedDictionary = TerytUlicPoprawkiDictionary.Load(appDataPath, tempLogger);
            tempLogger.Dispose();

            if (sharedDictionary == null || sharedDictionary.Count == 0)
                throw new Exception("Nie uda³o siê za³adowaæ s³ownika TerytUlicPoprawki z Excel");

            // ========================================
            // KROK 3: Generuj TypyUlic
            // ========================================
            var resultTypyUlic = await GenerateTypyUlicAsync(
                context,
                appDataPath,
                sharedDictionary,
                new Progress<ValidatorProgress>(p =>
                {
                    progress?.Report(($"[2/2] TypyUlic: {p.CurrentOperation}", p.ProcessedCount, p.TotalCount));
                })
            );

            sharedDictionary.Clear();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            return new S³ownikUlicLoaderResult
            {
                InsertedCountPoprawki = resultPoprawki.InsertedCount,
                TotalTypyUlic = resultTypyUlic.TotalCount,
                FoundTypyUlic = resultTypyUlic.FoundCount,
                NotFoundTypyUlic = resultTypyUlic.NotFoundCount
            };
        }

        private async Task<ValidatorResult> GenerateTypyUlicAsync(
            AddressDbContext context,
            string appDataPath,
            Dictionary<string, TerytUlicPoprawka> dictionary,
            IProgress<ValidatorProgress>? progress)
        {
            var logger = new PostalCodesLogger(appDataPath, "LoadTypyUlic.txt");
            await logger.InitializeAsync();

            var result = new ValidatorResult();

            try
            {
                logger.LogInfo("=== Rozpoczêcie generowania TypyUlic ===");

                progress?.Report(new ValidatorProgress { CurrentOperation = "Pobieranie danych z TerytUlic..." });

                var terytUlice = await context.TerytUlic
                    .Where(u => !string.IsNullOrEmpty(u.Nazwa1))
                    .AsNoTracking()
                    .ToListAsync();

                result.TotalCount = terytUlice.Count;
                logger.LogInfo($"Pobrano {result.TotalCount} wpisów z TerytUlic");

                progress?.Report(new ValidatorProgress
                {
                    CurrentOperation = $"Przetwarzanie {result.TotalCount} wpisów...",
                    TotalCount = result.TotalCount
                });

                var uliceList = new List<TerytUlicPoprawka>();

                foreach (var terytUlica in terytUlice)
                {
                    result.ProcessedCount++;

                    var originalParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(terytUlica.Cecha))
                        originalParts.Add(terytUlica.Cecha.Trim());
                    if (!string.IsNullOrWhiteSpace(terytUlica.Nazwa2))
                        originalParts.Add(terytUlica.Nazwa2.Trim());
                    if (!string.IsNullOrWhiteSpace(terytUlica.Nazwa1))
                        originalParts.Add(terytUlica.Nazwa1.Trim());

                    var original = string.Join(" ", originalParts);

                    if (dictionary.TryGetValue(original, out var terytUlicPoprawka))
                    {
                        result.FoundCount++;
                        uliceList.Add(terytUlicPoprawka);
                    }
                    else
                    {
                        result.NotFoundCount++;
                        if (result.NotFoundCount <= 100)
                            logger.LogWarning($"BRAK w s³owniku: '{original}'");
                    }

                    if (result.ProcessedCount % 1000 == 0 || result.ProcessedCount == result.TotalCount)
                    {
                        progress?.Report(new ValidatorProgress
                        {
                            CurrentOperation = $"Przetw: {result.ProcessedCount}/{result.TotalCount} | Znaleziono: {result.FoundCount} | Brak: {result.NotFoundCount}",
                            TotalCount = result.TotalCount,
                            ProcessedCount = result.ProcessedCount
                        });
                    }
                }

                await InsertUniqueTypyUlicAsync(context, uliceList, appDataPath, logger, progress);

                logger.LogInfo($"Przetworzono: {result.ProcessedCount}, Znaleziono: {result.FoundCount}, Brak: {result.NotFoundCount}");
                return result;
            }
            finally
            {
                logger.Dispose();
            }
        }

        private async Task InsertUniqueTypyUlicAsync(
            AddressDbContext context,
            List<TerytUlicPoprawka> uliceList,
            string appDataPath,
            GeneralLogger logger,
            IProgress<ValidatorProgress>? progress)
        {
            logger.LogInfo("Usuwanie referencji z tabeli Ulice...");
            await context.Database.ExecuteSqlRawAsync("UPDATE Ulice SET TypUlicyId = -1 WHERE TypUlicyId IS NOT NULL AND TypUlicyId != -1");

            logger.LogInfo("Czyszczenie tabeli TypyUlic...");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM TypyUlic WHERE Id != -1");
            await context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('TypyUlic', RESEED, 0)");

            // Za³aduj TytulyStopnie z Excela PRZED mapowaniem — bez tego tytu³y nieobecne
            // w bazie dostawa³y TytulStopienId=-1, przez co TypyUlic by³y b³êdnie zapisywane
            logger.LogInfo("£adowanie s³ownika TytulyStopnie z Excel...");
            var tytulyExcelLoader = new TytulyStopnieExcelLoader(context, appDataPath);
            var tytulyLoadResult = await tytulyExcelLoader.LoadFromExcelAsync(null);
            if (!string.IsNullOrEmpty(tytulyLoadResult.ErrorMessage))
                logger.LogWarning($"Ostrze¿enie przy ³adowaniu TytulyStopnie: {tytulyLoadResult.ErrorMessage}");
            else
                logger.LogInfo($"? TytulyStopnie: Dodano={tytulyLoadResult.InsertedCount}, Zaktualizowano={tytulyLoadResult.UpdatedCount}");

            logger.LogInfo($"Deduplikacja {uliceList.Count} wpisów...");
            var uniqueUliceSet = new HashSet<TypUlicy>(new TypUlicyEqualityComparer());

            var tytulyDict = new TytulyStopnieDictionary(context);
            await tytulyDict.GetDopelniaczToIdMappingAsync();

            foreach (var item in uliceList)
            {
                int tytulStopienId = tytulyDict.MapDopelniaczToId(item.Tytul);
                if (tytulStopienId == -2) tytulStopienId = -1;

                uniqueUliceSet.Add(new TypUlicy
                {
                    Prefiks = item.Prefiks?.Length > 200 ? item.Prefiks.Substring(0, 200) : item.Prefiks,
                    TytulStopienId = tytulStopienId,
                    Imie = item.Imie?.Length > 200 ? item.Imie.Substring(0, 200) : item.Imie,
                    Imie2 = item.Imie2?.Length > 200 ? item.Imie2.Substring(0, 200) : item.Imie2,
                    Nazwisko = item.Nazwisko?.Length > 200 ? item.Nazwisko.Substring(0, 200) : item.Nazwisko,
                    Nazwisko2 = item.Nazwisko2?.Length > 200 ? item.Nazwisko2.Substring(0, 200) : item.Nazwisko2,
                    Pseudonim = item.Pseudonim?.Length > 200 ? item.Pseudonim.Substring(0, 200) : item.Pseudonim,
                    Postfiks = item.Postfiks?.Length > 200 ? item.Postfiks.Substring(0, 200) : item.Postfiks
                });
            }

            var uniqueUlice = uniqueUliceSet.ToList();
            logger.LogInfo($"Znaleziono {uniqueUlice.Count} unikalnych wpisów");

            const int batchSize = 500;
            int insertedCount = 0;

            for (int i = 0; i < uniqueUlice.Count; i += batchSize)
            {
                var batch = uniqueUlice.Skip(i).Take(batchSize).ToList();
                await context.TypyUlic.AddRangeAsync(batch);
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();

                insertedCount += batch.Count;
                logger.LogInfo($"? Wstawiono partiê {i / batchSize + 1}: {insertedCount}/{uniqueUlice.Count}");

                progress?.Report(new ValidatorProgress
                {
                    CurrentOperation = $"Wstawiono {insertedCount}/{uniqueUlice.Count} unikalnych wpisów...",
                    TotalCount = uniqueUlice.Count,
                    ProcessedCount = insertedCount
                });
            }

            logger.LogInfo($"? Zakoñczono wstawianie: {insertedCount} unikalnych wpisów");
        }
    }

    internal class TypUlicyEqualityComparer : IEqualityComparer<TypUlicy>
    {
        public bool Equals(TypUlicy? x, TypUlicy? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return string.Equals(x.Prefiks, y.Prefiks, StringComparison.Ordinal) &&
                   x.TytulStopienId == y.TytulStopienId &&
                   string.Equals(x.Imie, y.Imie, StringComparison.Ordinal) &&
                   string.Equals(x.Imie2, y.Imie2, StringComparison.Ordinal) &&
                   string.Equals(x.Nazwisko, y.Nazwisko, StringComparison.Ordinal) &&
                   string.Equals(x.Nazwisko2, y.Nazwisko2, StringComparison.Ordinal) &&
                   string.Equals(x.Pseudonim, y.Pseudonim, StringComparison.Ordinal) &&
                   string.Equals(x.Postfiks, y.Postfiks, StringComparison.Ordinal);
        }

        public int GetHashCode(TypUlicy obj)
        {
            if (obj is null) return 0;
            return HashCode.Combine(
                obj.Prefiks ?? "",
                obj.TytulStopienId,
                obj.Imie ?? "",
                obj.Imie2 ?? "",
                obj.Nazwisko ?? "",
                obj.Nazwisko2 ?? "",
                HashCode.Combine(obj.Pseudonim ?? "", obj.Postfiks ?? "")
            );
        }
    }
}
