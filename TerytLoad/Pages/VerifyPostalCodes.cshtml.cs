// Copyright (c) 2025-2026 Andrzej Szepczyński. All rights reserved.

using AddressLibrary.Data;
using AddressLibrary.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using TerytLoad.Hubs;

namespace TerytLoad.Pages
{
    public class VerifyPostalCodesModel : PageModel
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<ProgressHub> _hubContext;

        public VerifyPostalCodesModel(
            IServiceScopeFactory scopeFactory,
            IWebHostEnvironment env,
            IHubContext<ProgressHub> hubContext)
        {
            _scopeFactory = scopeFactory;
            _env = env;
            _hubContext = hubContext;
        }

        public string? Message { get; set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            Console.WriteLine("[VerifyPostalCodes] ========== ROZPOCZĘCIE WERYFIKACJI ==========");

            // Uruchom weryfikację w tle
            _ = Task.Run(async () => await ProcessVerificationAsync());

            Message = "🔄 Weryfikacja rozpoczęta w tle.";

            return Page();
        }

        private async Task ProcessVerificationAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AddressDbContext>();

            var totalStartTime = DateTime.Now;

            try
            {
                var logDirectory = Path.Combine(_env.ContentRootPath, "AppData", "Logs");
                Directory.CreateDirectory(logDirectory);

                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-postal-codes", 0, 100,
                    "🔄 Rozpoczęto weryfikację zakresów numerów w kodach pocztowych...");

                var validator = new PostalCodeRangeValidator(context, logDirectory);
                var report = await validator.ValidateAsync();

                var totalTime = (DateTime.Now - totalStartTime).TotalSeconds;

                var summary = $"✅ Zakończono weryfikację!{Environment.NewLine}{Environment.NewLine}" +
                             $"📊 Statystyki:{Environment.NewLine}" +
                             $"   • Sprawdzono ulic: {report.ProcessedStreets:N0}{Environment.NewLine}" +
                             $"   • Znaleziono konfliktów: {report.TotalConflicts:N0}{Environment.NewLine}" +
                             $"   • Czas wykonania: {report.ElapsedSeconds:F2}s{Environment.NewLine}{Environment.NewLine}" +
                             $"📄 Raport zapisano do: AppData/Logs/VerifyPostalCodes.txt";

                await _hubContext.Clients.All.SendAsync(
                    "ReceiveProgress",
                    "verify-postal-codes",
                    100,
                    100,
                    summary);

                Console.WriteLine($"[VerifyPostalCodes] ========== ZAKOŃCZONO ({DateTime.Now:HH:mm:ss.fff}) ==========");
                Console.WriteLine($"[VerifyPostalCodes] Konfliktów: {report.TotalConflicts}");
            }
            catch (Exception ex)
            {
                var totalTime = (DateTime.Now - totalStartTime).TotalSeconds;
                Console.WriteLine($"[VerifyPostalCodes] ✗ BŁĄD po {totalTime:F2}s: {ex.Message}");
                Console.WriteLine($"[VerifyPostalCodes] StackTrace: {ex.StackTrace}");

                var errorMsg = $"❌ Błąd podczas weryfikacji: {ex.Message}{Environment.NewLine}⏱️ Czas do błędu: {totalTime:F2}s";
                await _hubContext.Clients.All.SendAsync("ReceiveProgress",
                    "verify-postal-codes", 0, 100, errorMsg);
            }
        }
    }
}