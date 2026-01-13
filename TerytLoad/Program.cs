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

// Inicjalizacja bazy danych przy starcie
var database = new AddressDatabase(connectionString);

// Wykonaj automatyczn¹ migracjê
try
{
    app.Logger.LogInformation("Rozpoczynanie migracji bazy danych...");
    await database.MigrateDatabaseAsync();
    app.Logger.LogInformation("Migracja bazy danych zakoñczona pomyœlnie.");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "B³¹d podczas migracji bazy danych.");
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