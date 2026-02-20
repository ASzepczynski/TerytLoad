using AddressLibrary;
using AddressLibrary.Data;
using AddressLibrary.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text;
using TerytLoad.Configuration;

namespace TerytLoad.Pages
{
    public class UpdateAddressesModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        [BindProperty]
        public string Message { get; set; } = string.Empty;

        public UpdateStats? Stats { get; set; }

        public UpdateAddressesModel(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostImportAddressesAsync()
        {
            const string filePath = @"c:\dane\adresy.txt";

            try
            {
                // Sprawdź czy plik istnieje
                if (!System.IO.File.Exists(filePath))
                {
                    Message = $"❌ BŁĄD: Plik nie istnieje: {filePath}";
                    return Page();
                }

                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? DatabaseConfig.DefaultConnectionString;

                var appDataPath = _environment.ContentRootPath;
                var database = new AddressDatabase(connectionString, appDataPath);
                var context = database.GetContext();

                // Upewnij się że tabela istnieje
                await context.Database.EnsureCreatedAsync();

                var stats = new UpdateStats();
                var messageBuilder = new StringBuilder();

                // Wczytaj plik
                var lines = await System.IO.File.ReadAllLinesAsync(filePath, Encoding.UTF8);
                
                if (lines.Length == 0)
                {
                    Message = "❌ BŁĄD: Plik jest pusty";
                    return Page();
                }

                // Sprawdź czy pierwsza linia to nagłówek
                bool hasHeader = lines[0].Contains("ID|Kod|Miejsc");
                var dataLines = hasHeader 
                    ? lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToList()
                    : lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
                
                stats.TotalRecords = dataLines.Count;

                messageBuilder.AppendLine($"📁 Plik: {filePath}");
                messageBuilder.AppendLine($"📊 Rekordów do importu: {stats.TotalRecords}");
                messageBuilder.AppendLine($"Rozpoczynam import...\n");

                // ✅ POPRAWKA: Wczytaj istniejące ID z bazy
                // var existingIds = await context.Adresy
                //     .Select(a => a.Id)
                //     .ToHashSetAsync();
                // ✅ POPRAWKA: Zmień ToHashSetAsync na ToListAsync + ToHashSet
                var existingIds = (await context.Adresy
                    .Select(a => a.Id)
                    .ToListAsync())
                    .ToHashSet();
                
                messageBuilder.AppendLine($"🔍 Znaleziono {existingIds.Count} istniejących rekordów w bazie");

                var addressesToAdd = new List<Adres>();
                int skippedCount = 0;
                int duplicateCount = 0;

                foreach (var line in dataLines)
                {
                    var parts = line.Split('|');
                    
                    // ✅ ZMIENIONE: Teraz musi mieć 10 kolumn (dodano Kraj)
                    if (parts.Length != 10)
                    {
                        messageBuilder.AppendLine($"⚠️ Pominięto nieprawidłową linię (kolumn: {parts.Length}): {line.Substring(0, Math.Min(80, line.Length))}...");
                        skippedCount++;
                        continue;
                    }

                    var id = parts[0].Trim();
                    
                    // Pomiń jeśli ID jest puste
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        skippedCount++;
                        continue;
                    }

                    // Sprawdź czy rekord już istnieje
                    if (existingIds.Contains(id))
                    {
                        duplicateCount++;
                        continue;
                    }

                    var newAddress = new Adres
                    {
                        Id = id,
                        Kraj = parts[1].Trim(),        // ✅ DODANE
                        Kod = parts[2].Trim(),         // ✅ Przesunięte z [1]
                        Miasto = parts[3].Trim(),      // ✅ Przesunięte z [2]
                        Ulica = parts[4].Trim(),       // ✅ Przesunięte z [3]
                        NrDomu = parts[5].Trim(),      // ✅ Przesunięte z [4]
                        NrLokalu = parts[6].Trim(),    // ✅ Przesunięte z [5]
                        Wojewodztwo = parts[7].Trim(), // ✅ Przesunięte z [6]
                        Powiat = parts[8].Trim(),      // ✅ Przesunięte z [7]
                        Gmina = parts[9].Trim()        // ✅ Przesunięte z [8]
                    };

                    addressesToAdd.Add(newAddress);
                    existingIds.Add(id);
                    stats.NewRecords++;
                }

                if (addressesToAdd.Count == 0)
                {
                    messageBuilder.AppendLine($"\n⚠️ Brak nowych rekordów do importu");
                    messageBuilder.AppendLine($"   • Duplikaty pominięte: {duplicateCount}");
                    messageBuilder.AppendLine($"   • Nieprawidłowe linie: {skippedCount}");
                    Message = messageBuilder.ToString();
                    Stats = stats;
                    return Page();
                }

                // Zapisz do bazy w batch'ach po 1000
                const int batchSize = 1000;
                int totalSaved = 0;

                for (int i = 0; i < addressesToAdd.Count; i += batchSize)
                {
                    var batch = addressesToAdd.Skip(i).Take(batchSize).ToList();
                    
                    try
                    {
                        await context.Adresy.AddRangeAsync(batch);
                        await context.SaveChangesAsync();
                        totalSaved += batch.Count;
                        
                        messageBuilder.AppendLine($"   ✓ Zapisano {totalSaved}/{addressesToAdd.Count} rekordów");
                    }
                    catch (DbUpdateException dbEx)
                    {
                        // ✅ OBSŁUGA BŁĘDÓW: Sprawdź inner exception
                        var innerMsg = dbEx.InnerException?.Message ?? dbEx.Message;
                        messageBuilder.AppendLine($"\n⚠️ BŁĄD podczas zapisu batch'a {i/batchSize + 1}:");
                        messageBuilder.AppendLine($"   {innerMsg}");
                        
                        // Spróbuj zapisać pojedynczo aby zidentyfikować problematyczne rekordy
                        messageBuilder.AppendLine($"   🔄 Próba zapisu pojedynczego...");
                        
                        foreach (var addr in batch)
                        {
                            try
                            {
                                // Sprawdź ponownie czy nie istnieje
                                var exists = await context.Adresy.AnyAsync(a => a.Id == addr.Id);
                                if (!exists)
                                {
                                    context.Adresy.Add(addr);
                                    await context.SaveChangesAsync();
                                    totalSaved++;
                                }
                                else
                                {
                                    duplicateCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                messageBuilder.AppendLine($"      ✗ Błąd dla ID={addr.Id}: {ex.Message}");
                                skippedCount++;
                            }
                        }
                    }
                }

                messageBuilder.AppendLine($"\n✅ Import zakończony!");
                messageBuilder.AppendLine($"   • Rekordów w pliku: {stats.TotalRecords}");
                messageBuilder.AppendLine($"   • Zaimportowanych: {totalSaved}");
                messageBuilder.AppendLine($"   • Duplikatów pominiętych: {duplicateCount}");
                messageBuilder.AppendLine($"   • Błędów/pominiętych: {skippedCount}");

                // Sprawdź ile jest rekordów w bazie
                var countInDb = await context.Adresy.CountAsync();
                messageBuilder.AppendLine($"   • Rekordów w bazie (łącznie): {countInDb}");

                Message = messageBuilder.ToString();
                Stats = stats;
                Stats.NewRecords = totalSaved;
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null 
                    ? $"\n\nInner Exception:\n{ex.InnerException.Message}\n{ex.InnerException.StackTrace}"
                    : "";
                
                Message = $"❌ BŁĄD podczas importu adresów:{Environment.NewLine}{ex.Message}{innerMsg}{Environment.NewLine}{Environment.NewLine}Stack trace:{Environment.NewLine}{ex.StackTrace}";
            }

            return Page();
        }

        public async Task<IActionResult> OnPostUpdateAddressesAsync()
        {
            const string filePath = @"c:\dane\poprawa adresow\Poprawki.txt";

            try
            {
                // Sprawdź czy plik istnieje
                if (!System.IO.File.Exists(filePath))
                {
                    Message = $"❌ BŁĄD: Plik nie istnieje: {filePath}";
                    return Page();
                }

                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? DatabaseConfig.DefaultConnectionString;

                var appDataPath = _environment.ContentRootPath;
                var database = new AddressDatabase(connectionString, appDataPath);
                var context = database.GetContext();

                // Wyczyść ChangeTracker na początku
                context.ChangeTracker.Clear();

                // Upewnij się że tabela istnieje
                await context.Database.EnsureCreatedAsync();

                var stats = new UpdateStats();
                var messageBuilder = new StringBuilder();

                // Wczytaj plik
                var lines = await System.IO.File.ReadAllLinesAsync(filePath, Encoding.UTF8);
                
                if (lines.Length < 2)
                {
                    Message = "❌ BŁĄD: Plik jest pusty lub zawiera tylko nagłówek";
                    return Page();
                }

                // Pomiń nagłówek (pierwsza linia)
                var dataLines = lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
                stats.TotalRecords = dataLines.Count;

                messageBuilder.AppendLine($"Rozpoczynam aktualizację {stats.TotalRecords} rekordów...\n");

                // Użyj AsNoTracking aby nie śledzić encji
                var existingAddresses = await context.Adresy
                    .AsNoTracking()
                    .ToDictionaryAsync(a => a.Id);

                // Wyczyść ponownie po wczytaniu
                context.ChangeTracker.Clear();

                // Przechowuj kompletne dane do aktualizacji
                var addressesToUpdate = new Dictionary<string, Adres>();
                var notFoundIds = new List<string>(); // ✅ NOWE: Lista ID nieznalezionych w bazie

                foreach (var line in dataLines)
                {
                    var parts = line.Split('\t');
                    
                    // ✅ ZMIENIONE: Teraz musi mieć co najmniej 10 kolumn
                    if (parts.Length < 10)
                    {
                        messageBuilder.AppendLine($"⚠️ Pominięto nieprawidłową linię: {line.Substring(0, Math.Min(50, line.Length))}...");
                        continue;
                    }
                    
                    // Format: ID|Kraj|Kod|Miejscowość|Ulica|Nr domu|Nr mieszkania|Województwo|Powiat|Gmina

                    var id = parts[0].Trim();
                    var newAddress = new Adres
                    {
                        Id = id,
                        Kraj = parts[1].Trim(),        // ✅ DODANE
                        Kod = parts[2].Trim(),         // ✅ Przesunięte
                        Miasto = parts[3].Trim(),      // ✅ Przesunięte
                        Ulica = parts[4].Trim(),       // ✅ Przesunięte
                        NrDomu = parts[5].Trim(),      // ✅ Przesunięte
                        NrLokalu = parts[6].Trim(),    // ✅ Przesunięte
                        Wojewodztwo = parts[7].Trim(), // ✅ Przesunięte
                        Powiat = parts[8].Trim(),      // ✅ Przesunięte
                        Gmina = parts[9].Trim()        // ✅ Przesunięte
                    };

                    // Sprawdź czy adres istnieje w bazie
                    if (existingAddresses.TryGetValue(id, out var existingAddress))
                    {
                        // Sprawdź czy są zmiany
                        if (HasChanges(existingAddress, newAddress))
                        {
                            addressesToUpdate[id] = newAddress;
                            stats.UpdatedRecords++;
                        }
                        else
                        {
                            stats.UnchangedRecords++;
                        }
                    }
                    else
                    {
                        notFoundIds.Add(id);
                    }
                }

                // Aktualizuj rekordy
                if (addressesToUpdate.Any())
                {
                    messageBuilder.AppendLine($"🔄 Aktualizuję {addressesToUpdate.Count} rekordów...");
            
                    const int batchSize = 1000;
                    int updatedCount = 0;
            
                    var updateIds = addressesToUpdate.Keys.ToList();
            
                    for (int i = 0; i < updateIds.Count; i += batchSize)
                    {
                        var batchIds = updateIds.Skip(i).Take(batchSize).ToList();
            
                        // Wyczyść przed wczytaniem nowego batcha
                        context.ChangeTracker.Clear();
            
                        // Wczytaj z śledzeniem
                        var recordsToUpdate = await context.Adresy
                            .Where(a => batchIds.Contains(a.Id))
                            .ToListAsync();
            
                        // Aktualizuj wartości
                        foreach (var record in recordsToUpdate)
                        {
                            if (addressesToUpdate.TryGetValue(record.Id, out var newData))
                            {
                                record.Kraj = newData.Kraj;               // ✅ DODANE
                                record.Kod = newData.Kod;
                                record.Miasto = newData.Miasto;
                                record.Ulica = newData.Ulica;
                                record.NrDomu = newData.NrDomu;
                                record.NrLokalu = newData.NrLokalu;
                                record.Wojewodztwo = newData.Wojewodztwo;
                                record.Powiat = newData.Powiat;
                                record.Gmina = newData.Gmina;
                            }
                        }
            
                        await context.SaveChangesAsync();
                        updatedCount += recordsToUpdate.Count;
            
                        messageBuilder.AppendLine($"   ✓ Zaktualizowano {updatedCount}/{addressesToUpdate.Count} rekordów");
                    }
            
                    // Wyczyść po aktualizacji
                    context.ChangeTracker.Clear();
                }

                // ✅ NOWE: Raportuj nieznalezione rekordy
                if (notFoundIds.Any())
                {
                    messageBuilder.AppendLine($"\n⚠️ Nie znaleziono w bazie {notFoundIds.Count} rekordów:");
            
                    // Pokaż pierwsze 20 nieznalezionych ID
                    var samplesToShow = Math.Min(20, notFoundIds.Count);
                    for (int i = 0; i < samplesToShow; i++)
                    {
                        messageBuilder.AppendLine($"   • ID: {notFoundIds[i]}");
                    }
            
                    if (notFoundIds.Count > samplesToShow)
                    {
                        messageBuilder.AppendLine($"   ... i {notFoundIds.Count - samplesToShow} więcej");
                    }
                }

                // Końcowe czyszczenie
                context.ChangeTracker.Clear();

                messageBuilder.AppendLine($"\n✅ Aktualizacja zakończona pomyślnie!");
                messageBuilder.AppendLine($"   • Rekordów w pliku: {stats.TotalRecords}");
                messageBuilder.AppendLine($"   • Zaktualizowanych: {stats.UpdatedRecords}");
                messageBuilder.AppendLine($"   • Bez zmian: {stats.UnchangedRecords}");
                messageBuilder.AppendLine($"   • Nie znaleziono w bazie: {notFoundIds.Count}");

                Message = messageBuilder.ToString();
                Stats = stats;
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null 
                    ? $"\n\nInner Exception:\n{ex.InnerException.Message}"
                    : "";
                
                Message = $"❌ BŁĄD podczas aktualizacji adresów:{Environment.NewLine}{ex.Message}{innerMsg}{Environment.NewLine}{Environment.NewLine}Stack trace:{Environment.NewLine}{ex.StackTrace}";
            }

            return Page();
        }

        public async Task<IActionResult> OnPostExportAddressesAsync()
        {
            const string outputFilePath = @"AppData\Address\adresy.txt";

            try
            {
                var connectionString = _configuration.GetConnectionString("AddressDatabase")
                    ?? DatabaseConfig.DefaultConnectionString;

                var appDataPath = _environment.ContentRootPath;
                var database = new AddressDatabase(connectionString, appDataPath);
                var context = database.GetContext();

                var messageBuilder = new StringBuilder();

                messageBuilder.AppendLine($"Rozpoczynam eksport adresów do pliku {outputFilePath}...\n");

                // Wczytaj wszystkie adresy z kraju 'PL'
                var polishAddresses = await context.Adresy
                    .AsNoTracking()
                    .Where(a => a.Kraj == "PL")
                    .OrderBy(a => a.Id)
                    .ToListAsync();

                messageBuilder.AppendLine($"🔍 Znaleziono {polishAddresses.Count} adresów z kraju 'PL'");

                if (polishAddresses.Count == 0)
                {
                    Message = "⚠️ Brak adresów z kraju 'PL' do eksportu";
                    return Page();
                }

                // Przygotuj ścieżkę do pliku
                var fullPath = Path.Combine(_environment.ContentRootPath, outputFilePath);
                var directory = Path.GetDirectoryName(fullPath);
                
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    messageBuilder.AppendLine($"📁 Utworzono katalog: {directory}");
                }

                // Zapisz do pliku
                using (var writer = new StreamWriter(fullPath, false, Encoding.UTF8))
                {
                    // Nagłówek
                    await writer.WriteLineAsync("ID|Kraj|Kod|Miejscowość|Ulica|Nr domu|Nr mieszkania|Województwo|Powiat|Gmina");

                    // Dane
                    foreach (var addr in polishAddresses)
                    {
                        var line = $"{addr.Id}|{addr.Kraj}|{addr.Kod}|{addr.Miasto}|{addr.Ulica}|{addr.NrDomu}|{addr.NrLokalu}|{addr.Wojewodztwo}|{addr.Powiat}|{addr.Gmina}";
                        await writer.WriteLineAsync(line);
                    }
                }

                messageBuilder.AppendLine($"\n✅ Eksport zakończony pomyślnie!");
                messageBuilder.AppendLine($"   • Wyeksportowano: {polishAddresses.Count} rekordów");
                messageBuilder.AppendLine($"   • Plik: {fullPath}");
                messageBuilder.AppendLine($"   • Rozmiar: {new FileInfo(fullPath).Length / 1024.0:F2} KB");

                // ✅ POPRAWKA: Użyj File.ReadAllLines zamiast ReadLinesAsync
                var allLines = await System.IO.File.ReadAllLinesAsync(fullPath, Encoding.UTF8);
                var sampleLines = allLines.Take(5);
                
                messageBuilder.AppendLine($"\n📄 Przykładowe linie z pliku:");
                foreach (var line in sampleLines)
                {
                    messageBuilder.AppendLine($"   {line.Substring(0, Math.Min(100, line.Length))}...");
                }

                Message = messageBuilder.ToString();
                
                var stats = new UpdateStats
                {
                    TotalRecords = polishAddresses.Count,
                    NewRecords = polishAddresses.Count
                };
                Stats = stats;
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null 
                    ? $"\n\nInner Exception:\n{ex.InnerException.Message}"
                    : "";
                
                Message = $"❌ BŁĄD podczas eksportu adresów:{Environment.NewLine}{ex.Message}{innerMsg}{Environment.NewLine}{Environment.NewLine}Stack trace:{Environment.NewLine}{ex.StackTrace}";
            }

            return Page();
        }

        private bool HasChanges(Adres existing, Adres updated)
        {
            return existing.Kraj != updated.Kraj               // ✅ DODANE
                || existing.Kod != updated.Kod
                || existing.Miasto != updated.Miasto
                || existing.Ulica != updated.Ulica
                || existing.NrDomu != updated.NrDomu
                || existing.NrLokalu != updated.NrLokalu
                || existing.Wojewodztwo != updated.Wojewodztwo
                || existing.Powiat != updated.Powiat
                || existing.Gmina != updated.Gmina;
        }

        public class UpdateStats
        {
            public int TotalRecords { get; set; }
            public int UpdatedRecords { get; set; }
            public int NewRecords { get; set; }
            public int UnchangedRecords { get; set; }
        }
    }
}