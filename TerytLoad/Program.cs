using AddressLibrary;
using AddressLibrary.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddSignalR();

// Rejestracja AddressDbContext w kontenerze DI
var connectionString = builder.Configuration.GetConnectionString("AddressDatabase")
    ?? throw new InvalidOperationException("Connection string 'AddressDatabase' not found in appsettings.json");

builder.Services.AddDbContext<AddressDbContext>(options =>
    options.UseSqlServer(connectionString));

var app = builder.Build();

// ✅ BEZPIECZNA inicjalizacja bazy danych
var appDataPath = app.Environment.ContentRootPath;
var database = new AddressLibrary.AddressDatabase(connectionString, appDataPath);

try
{
    app.Logger.LogInformation("Sprawdzanie bazy danych...");
    
    var canConnect = await database.CanConnectToDatabaseAsync();
    
    if (!canConnect)
    {
        app.Logger.LogWarning("Baza danych nie istnieje. Tworzenie nowej bazy...");
        await database.InitializeDatabaseAsync();
        app.Logger.LogInformation("✓ Baza danych utworzona.");
    }
    else
    {
        app.Logger.LogInformation("✓ Baza danych istnieje i jest dostępna.");
        
        // Opcjonalnie: sprawdź podstawowe tabele
        var hasWojewodztwa = await database.TableExistsAsync("Wojewodztwa");
        if (!hasWojewodztwa)
        {
            app.Logger.LogWarning("⚠️ UWAGA: Baza istnieje ale brak tabeli Wojewodztwa!");
            app.Logger.LogWarning("⚠️ Możliwe że struktura jest niepełna.");
            app.Logger.LogWarning("⚠️ Jeśli chcesz odtworzyć bazę, użyj metody ManualRecreateDatabaseAsync()");
        }
    }
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "❌ Błąd podczas inicjalizacji bazy danych: {Message}", ex.Message);
    throw;
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapHub<TerytLoad.Hubs.ProgressHub>("/progressHub");

app.Run();