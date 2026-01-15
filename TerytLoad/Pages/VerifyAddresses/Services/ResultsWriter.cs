// Copyright (c) 2025-2026 Andrzej Szepczyński. All rights reserved.

using System.Text;
using TerytLoad.Pages.VerifyAddresses.Models;

namespace TerytLoad.Pages.VerifyAddresses.Services
{
    public class ResultsWriter
    {
        private readonly AddressNormalizer _normalizer;

        public ResultsWriter()
        {
            _normalizer = new AddressNormalizer();
        }

        public async Task SaveResultsAsync(string outputDirectory, List<VerificationResult> results)
        {
            try
            {
                // Podziel wyniki na sukcesy i błędy
                var successes = results.Where(r => r.Status == "SUKCES").ToList();
                var errors = results.Where(r => r.Status != "SUKCES").ToList();

                // Zapisz sukcesy do adresy_ok.txt
                var successFilePath = Path.Combine(outputDirectory, "adresy_ok.txt");
                await SaveSuccessResultsAsync(successFilePath, successes);

                // Zapisz błędy do adresy_bledy.txt
                var errorFilePath = Path.Combine(outputDirectory, "adresy_bledy.txt");
                await SaveErrorResultsAsync(errorFilePath, errors);

                Console.WriteLine($"[VerifyAddresses] ✓ Zapisano {successes.Count} sukcesów do: {successFilePath}");
                Console.WriteLine($"[VerifyAddresses] ✓ Zapisano {errors.Count} błędów do: {errorFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VerifyAddresses] ✗ Błąd podczas zapisywania wyników: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Zapisuje sukcesy do pliku adresy_ok.txt (bez Status i Komunikat)
        /// </summary>
        private async Task SaveSuccessResultsAsync(string filePath, List<VerificationResult> results)
        {
            var sb = new StringBuilder();

            // Nagłówek BEZ kolumn Status i Komunikat
            sb.AppendLine("ID|" +
                         "Kod_Źródło|Kod_Wynik|" +
                         "Miasto_Źródło|Miasto_Wynik|" +
                         "Ulica_Źródło|Ulica_Wynik|" +
                         "Budynek_Źródło|Budynek_Wynik|" +
                         "Lokal_Źródło|Lokal_Wynik|" +
                         "Wojewodztwo_Źródło|Wojewodztwo_Wynik|" +
                         "Powiat_Źródło|Powiat_Wynik|" +
                         "Gmina_Źródło|Gmina_Wynik");

            foreach (var result in results)
            {
                if (result.FoundData != null)
                {
                    sb.AppendLine($"{result.SourceId}|" +
                                 $"{_normalizer.MarkIfDifferent(result.SourceData.Kod, result.FoundData.Kod)}|{result.FoundData.Kod}|" +
                                 $"{_normalizer.MarkIfDifferent(result.SourceData.Miasto, result.FoundData.Miasto)}|{result.FoundData.Miasto}|" +
                                 $"{_normalizer.MarkIfDifferent(result.SourceData.Ulica, result.FoundData.Ulica)}|{result.FoundData.Ulica}|" +
                                 $"{result.SourceData.Budynek}|{result.FoundData.Budynek}|" +
                                 $"{result.SourceData.Lokal}|{result.FoundData.Lokal}|" +
                                 $"{_normalizer.MarkIfDifferent(result.SourceData.Wojewodztwo, result.FoundData.Wojewodztwo)}|{result.FoundData.Wojewodztwo}|" +
                                 $"{_normalizer.MarkIfDifferent(result.SourceData.Powiat, result.FoundData.Powiat)}|{result.FoundData.Powiat}|" +
                                 $"{_normalizer.MarkIfDifferent(result.SourceData.Gmina, result.FoundData.Gmina)}|{result.FoundData.Gmina}");
                }
            }

            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Zapisuje błędy do pliku adresy_bledy.txt (z Status i Komunikat)
        /// </summary>
        private async Task SaveErrorResultsAsync(string filePath, List<VerificationResult> results)
        {
            var sb = new StringBuilder();

            // Nagłówek Z kolumnami Status i Komunikat
            sb.AppendLine("ID|Status|Komunikat|" +
                         "Kod_Źródło|Kod_Wynik|" +
                         "Miasto_Źródło|Miasto_Wynik|" +
                         "Ulica_Źródło|Ulica_Wynik|" +
                         "Budynek_Źródło|Budynek_Wynik|" +
                         "Lokal_Źródło|Lokal_Wynik|" +
                         "Wojewodztwo_Źródło|Wojewodztwo_Wynik|" +
                         "Powiat_Źródło|Powiat_Wynik|" +
                         "Gmina_Źródło|Gmina_Wynik");

            foreach (var result in results)
            {
                var komunikat = EscapePipeCharacter(result.ErrorMessage ?? string.Empty);

                if (result.FoundData != null)
                {
                    // Częściowe dopasowanie (znaleziono miejscowość, ale błąd z ulicą itp.)
                    sb.AppendLine($"{result.SourceId}|{result.Status}|{komunikat}|" +
                                 $"{_normalizer.MarkIfDifferent(result.SourceData.Kod, result.FoundData.Kod)}|{result.FoundData.Kod}|" +
                                 $"{_normalizer.MarkIfDifferent(result.SourceData.Miasto, result.FoundData.Miasto)}|{result.FoundData.Miasto}|" +
                                 $"*|{result.FoundData.Ulica}|" +
                                 $"{result.SourceData.Budynek}|{result.FoundData.Budynek}|" +
                                 $"{result.SourceData.Lokal}|{result.FoundData.Lokal}|" +
                                 $"{_normalizer.MarkIfDifferent(result.SourceData.Wojewodztwo, result.FoundData.Wojewodztwo)}|{result.FoundData.Wojewodztwo}|" +
                                 $"{_normalizer.MarkIfDifferent(result.SourceData.Powiat, result.FoundData.Powiat)}|{result.FoundData.Powiat}|" +
                                 $"{_normalizer.MarkIfDifferent(result.SourceData.Gmina, result.FoundData.Gmina)}|{result.FoundData.Gmina}");
                }
                else
                {
                    // Całkowity brak dopasowania
                    sb.AppendLine($"{result.SourceId}|{result.Status}|{komunikat}|" +
                                 $"*{result.SourceData.Kod}||" +
                                 $"*{result.SourceData.Miasto}||" +
                                 $"*{result.SourceData.Ulica}||" +
                                 $"*{result.SourceData.Budynek}||" +
                                 $"*{result.SourceData.Lokal}||" +
                                 $"*{result.SourceData.Wojewodztwo}||" +
                                 $"*{result.SourceData.Powiat}||" +
                                 $"*{result.SourceData.Gmina}|");
                }
            }

            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Usuwa znaki pipe '|' z tekstu, aby nie zaburzyć struktury pliku
        /// </summary>
        private string EscapePipeCharacter(string text)
        {
            return text?.Replace("|", ";") ?? string.Empty;
        }
    }
}