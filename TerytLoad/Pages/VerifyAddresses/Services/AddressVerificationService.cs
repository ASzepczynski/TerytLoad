// Copyright (c) 2025-2026 Andrzej Szepczyński. All rights reserved.

using AddressLibrary.Services.AddressSearch;
using System.Text;
using TerytLoad.Pages.VerifyAddresses.Models;

namespace TerytLoad.Pages.VerifyAddresses.Services
{
    /// <summary>
    /// Serwis weryfikacji adresów używający AddressSearchService z AddressLibrary
    /// </summary>
    public class AddressVerificationService
    {
        private readonly AddressSearchService _searchService;
        private readonly AddressDataParser _parser;
        private readonly StringBuilder _diagnosticLog;
        private readonly string? _logFilePath;

        public AddressVerificationService(AddressSearchService searchService, string? logFilePath = null)
        {
            _searchService = searchService;
            _parser = new AddressDataParser();
            _diagnosticLog = new StringBuilder();
            _logFilePath = logFilePath;

            if (!string.IsNullOrEmpty(_logFilePath))
            {
                // Utwórz katalog jeśli nie istnieje
                var logDir = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                // Inicjalizuj plik logu
                _diagnosticLog.AppendLine($"=== LOG WYSZUKIWANIA ADRESÓW - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                _diagnosticLog.AppendLine();
            }
        }

        /// <summary>
        /// Przetwarza pojedynczą linię z pliku weryfikacyjnego
        /// </summary>
        public async Task<VerificationResult> ProcessLineAsync(string line)
        {
            try
            {
                var sourceData = _parser.ParseLine(line);
                return await VerifyAddressAsync(sourceData);
            }
            catch (FormatException ex)
            {
                var parts = line.Split('|');
                var id = parts.Length > 0 ? parts[0] : "UNKNOWN";

                LogDiagnostic($"[BŁĄD] ID: {id} - {ex.Message}");

                return new VerificationResult
                {
                    SourceId = id,
                    Status = "BŁĄD",
                    ErrorMessage = ex.Message
                };
            }
            catch (Exception ex)
            {
                var parts = line.Split('|');
                var id = parts.Length > 0 ? parts[0] : "UNKNOWN";

                LogDiagnostic($"[BŁĄD] ID: {id} - {ex.Message}");

                return new VerificationResult
                {
                    SourceId = id,
                    Status = "BŁĄD",
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Weryfikuje adres używając AddressSearchService
        /// </summary>
        private async Task<VerificationResult> VerifyAddressAsync(AddressData sourceData)
        {
            LogDiagnostic($"\n{'=',-80}");
            LogDiagnostic($"ID: {sourceData.Id}");
            LogDiagnostic($"Adres źródłowy: {sourceData.Kod}, {sourceData.Miejscowosc}, {sourceData.Ulica} {sourceData.Budynek}/{sourceData.Lokal}");

            // Utwórz żądanie wyszukiwania
            var searchRequest = new AddressSearchRequest
            {
                KodPocztowy = sourceData.Kod,
                Miejscowosc = sourceData.Miejscowosc,
                Ulica = sourceData.Ulica,
                NumerDomu = sourceData.Budynek,
                NumerMieszkania = sourceData.Lokal
            };

            // Wykonaj wyszukiwanie
            var searchResult = await _searchService.SearchAsync(searchRequest);

            // Zapisz log diagnostyczny z wyszukiwania
            if (!string.IsNullOrEmpty(searchResult.DiagnosticInfo))
            {
                LogDiagnostic("--- Log wyszukiwania ---");
                LogDiagnostic(searchResult.DiagnosticInfo);
            }

            // Mapuj wynik na VerificationResult
            var result = MapSearchResultToVerificationResult(sourceData, searchResult);

            LogDiagnostic($"Status końcowy: {result.Status}");
            // ✅ ZAWSZE WYPISUJ ErrorMessage (nawet jeśli jest pusty)
            LogDiagnostic($"Komunikat: {result.ErrorMessage ?? "(brak)"}");

            return result;
        }

        /// <summary>
        /// Zapisuje logi diagnostyczne do pliku
        /// </summary>
        public async Task SaveDiagnosticLogAsync()
        {
            if (!string.IsNullOrEmpty(_logFilePath))
            {
                _diagnosticLog.AppendLine();
                _diagnosticLog.AppendLine($"=== KONIEC LOGU - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

                try
                {
                    await File.WriteAllTextAsync(_logFilePath, _diagnosticLog.ToString(), Encoding.UTF8);
                    Console.WriteLine($"[AddressVerificationService] ✓ Log diagnostyczny zapisany: {_logFilePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AddressVerificationService] ✗ Błąd zapisu logu: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Dodaje wpis do logu diagnostycznego
        /// </summary>
        private void LogDiagnostic(string message)
        {
            if (!string.IsNullOrEmpty(_logFilePath))
            {
                _diagnosticLog.AppendLine(message);
            }
        }

        /// <summary>
        /// Mapuje wynik z AddressSearchService na VerificationResult
        /// </summary>
        private VerificationResult MapSearchResultToVerificationResult(
            AddressData sourceData,
            AddressSearchResult searchResult)
        {
            var result = new VerificationResult
            {
                SourceId = sourceData.Id,
                SourceData = sourceData
            };

            switch (searchResult.Status)
            {
                case AddressSearchStatus.Success:
                    result.Status = "SUKCES";
                    result.FoundData = CreateFoundAddress(searchResult, sourceData);
                    break;

                case AddressSearchStatus.MultipleMatches:
                    result.Status = "BŁĄD";
                    result.FoundData = CreateFoundAddress(searchResult, sourceData);
                    result.ErrorMessage = searchResult.Message ?? "Znaleziono wiele dopasowań";
                    break;

                case AddressSearchStatus.MiejscowoscNotFound:
                    result.Status = "BŁĄD";
                    result.ErrorMessage = searchResult.Message ?? "Nie znaleziono miejscowości";
                    break;

                case AddressSearchStatus.UlicaNotFound:
                    // Jeśli nie podano ulicy w źródle, to specjalny przypadek
                    if (string.IsNullOrWhiteSpace(sourceData.Ulica))
                    {
                        result.Status = "BŁĄD";
                        result.ErrorMessage = "Wymagane podanie ulicy";
                        // Jeśli znaleziono miejscowość, dodaj ją do wyniku
                        if (searchResult.Miejscowosc != null)
                        {
                            result.FoundData = CreatePartialFoundAddress(searchResult, sourceData);
                        }
                    }
                    else
                    {
                        result.Status = "BŁĄD";
                        result.ErrorMessage = searchResult.Message ?? "Nie znaleziono podanej ulicy";
                        // Jeśli znaleziono miejscowość, dodaj ją do wyniku
                        if (searchResult.Miejscowosc != null)
                        {
                            result.FoundData = CreatePartialFoundAddress(searchResult, sourceData);
                        }
                    }
                    break;

                case AddressSearchStatus.KodPocztowyNotFound:
                    result.Status = "BŁĄD";
                    result.ErrorMessage = searchResult.Message ?? "Nie znaleziono kodu pocztowego";
                    // Jeśli znaleziono miejscowość lub ulicę, dodaj do wyniku
                    if (searchResult.Miejscowosc != null || searchResult.Ulica != null)
                    {
                        result.FoundData = CreatePartialFoundAddress(searchResult, sourceData);
                    }
                    break;

                case AddressSearchStatus.ValidationError:
                    result.Status = "BŁĄD";
                    result.ErrorMessage = searchResult.Message ?? "Brak podstawowych informacji";
                    break;

                default:
                    result.Status = "BŁĄD";
                    result.ErrorMessage = "Nieznany status wyszukiwania";
                    break;
            }

            return result;
        }

        /// <summary>
        /// Tworzy pełny znaleziony adres z wyniku wyszukiwania
        /// </summary>
        private AddressData CreateFoundAddress(AddressSearchResult searchResult, AddressData sourceData)
        {
            return new AddressData
            {
                Kod = searchResult.KodPocztowy?.Kod ?? sourceData.Kod,
                Miejscowosc = searchResult.Miejscowosc?.Nazwa ?? sourceData.Miejscowosc,
                Ulica = searchResult.Ulica != null
                    ? $"{searchResult.Ulica.Cecha} {searchResult.Ulica.Nazwa1}".Trim()
                    : sourceData.Ulica,
                // ✅ Użyj znormalizowanych numerów z wyniku wyszukiwania
                Budynek = searchResult.NormalizedBuildingNumber ?? sourceData.Budynek,
                Lokal = searchResult.NormalizedApartmentNumber ?? sourceData.Lokal,
                Wojewodztwo = searchResult.Miejscowosc?.Gmina?.Powiat?.Wojewodztwo?.Nazwa ?? sourceData.Wojewodztwo,
                Powiat = searchResult.Miejscowosc?.Gmina?.Powiat?.Nazwa ?? sourceData.Powiat,
                Gmina = searchResult.Miejscowosc?.Gmina?.Nazwa ?? sourceData.Gmina
            };
        }

        /// <summary>
        /// Tworzy częściowo znaleziony adres (gdy nie znaleziono wszystkich elementów)
        /// </summary>
        private AddressData CreatePartialFoundAddress(AddressSearchResult searchResult, AddressData sourceData)
        {
            return new AddressData
            {
                Kod = searchResult.KodPocztowy?.Kod ?? sourceData.Kod,
                Miejscowosc = searchResult.Miejscowosc?.Nazwa ?? sourceData.Miejscowosc,
                Ulica = searchResult.Ulica != null
                    ? $"{searchResult.Ulica.Cecha} {searchResult.Ulica.Nazwa1}".Trim()
                    : (string.IsNullOrWhiteSpace(sourceData.Ulica) ? "Brak" : sourceData.Ulica),
                // ✅ Również tutaj użyj znormalizowanych numerów
                Budynek = searchResult.NormalizedBuildingNumber ?? sourceData.Budynek,
                Lokal = searchResult.NormalizedApartmentNumber ?? sourceData.Lokal,
                Wojewodztwo = searchResult.Miejscowosc?.Gmina?.Powiat?.Wojewodztwo?.Nazwa ?? sourceData.Wojewodztwo,
                Powiat = searchResult.Miejscowosc?.Gmina?.Powiat?.Nazwa ?? sourceData.Powiat,
                Gmina = searchResult.Miejscowosc?.Gmina?.Nazwa ?? sourceData.Gmina
            };
        }
    }
}