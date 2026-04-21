using AddressLibrary;
using AddressLibrary.Data;
using Microsoft.EntityFrameworkCore;
using TerytLoad.Pages.DbViewer;

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

// ✅ DODANE: Inicjalizacja ViewerRegistry przy starcie
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AddressDbContext>();
    await context.Database.EnsureCreatedAsync();
    await AddressLibrary.Data.SchemaMigrator.ApplyAsync(context);
    ViewerRegistry.InitializeFromDbContext(context);
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