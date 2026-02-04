using AddressLibrary;
using AddressLibrary.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddSignalR(); // Dodaj SignalR

// Rejestracja AddressDbContext w kontenerze DI
var connectionString = builder.Configuration.GetConnectionString("AddressDatabase")
    ?? throw new InvalidOperationException("Connection string 'AddressDatabase' not found in appsettings.json");

builder.Services.AddDbContext<AddressDbContext>(options =>
    options.UseSqlServer(connectionString)); // ZMIENIONO: UseSqlServer zamiast UseSqlite

var app = builder.Build();

// ✅ POPRAWKA: Użyj app.Environment zamiast _environment
var appDataPath = app.Environment.ContentRootPath;
var database = new AddressDatabase(connectionString, appDataPath);

// Wykonaj automatyczną migrację
try
{
    app.Logger.LogInformation("Rozpoczynanie migracji bazy danych...");
    await database.MigrateDatabaseAsync();
    app.Logger.LogInformation("Migracja bazy danych zakończona pomyślnie.");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Błąd podczas migracji bazy danych.");
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
app.MapHub<TerytLoad.Hubs.ProgressHub>("/progressHub"); // Dodaj mapowanie Hub

app.Run();