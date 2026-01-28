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
        private readonly StringBuilder? _diagnosticLog;
        private readonly string? _logFilePath;
        private readonly bool _enableLogging;

        public AddressVerificationService(AddressSearchService searchService, string? logFilePath = null)
        {
            _searchService = searchService;
            _parser = new AddressDataParser();
            _logFilePath = logFilePath;
            _enableLogging = !string.IsNullOrEmpty(logFilePath);

            if (_enableLogging)
            {
                _diagnosticLog = new StringBuilder();
                
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
            // 🚀 OPTYMALIZACJA: Loguj tylko jeśli włączone
            if (_enableLogging)
            {
                LogDiagnostic($"\n{'=',-80}");
                LogDiagnostic($"ID: {sourceData.Id}");
                LogDiagnostic($"Adres źródłowy: {sourceData.Kod}, {sourceData.Miasto}, {sourceData.Ulica} {sourceData.Budynek}/{sourceData.Lokal}");
            }

            // ✅ SPRAWDZENIE 1: Jeśli brak miejscowości -> ZA MAŁO INFORMACJI
            if (string.IsNullOrWhiteSpace(sourceData.Miasto))
            {
                if (_enableLogging)
                {
                    LogDiagnostic("WYKRYTO: Brak nazwy miejscowości - za mało informacji");
                }

                return new VerificationResult
                {
                    SourceId = sourceData.Id,
                    SourceData = sourceData,
                    Status = "ZA_MALO_INFORMACJI",
                    ErrorMessage = "Nazwa miejscowości jest wymagana"
                };
            }

            // ✅ SPRAWDZENIE 2: Jeśli jest tylko miejscowość bez kodu i ulicy -> ZA MAŁO INFORMACJI
            if (string.IsNullOrWhiteSpace(sourceData.Kod) &&
                string.IsNullOrWhiteSpace(sourceData.Ulica))
            {
                if (_enableLogging)
                {
                    LogDiagnostic("WYKRYTO: Tylko miejscowość bez kodu pocztowego i ulicy - za mało informacji");
                }

                return new VerificationResult
                {
                    SourceId = sourceData.Id,
                    SourceData = sourceData,
                    Status = "ZA_MALO_INFORMACJI",
                    ErrorMessage = "Za mało informacji"
                };
            }

            // Utwórz żądanie wyszukiwania
            var searchRequest = new AddressSearchRequest
            {
                KodPocztowy = sourceData.Kod,
                Miasto = sourceData.Miasto,
                Ulica = sourceData.Ulica,
                NumerDomu = sourceData.Budynek,
                NumerMieszkania = sourceData.Lokal
            };

            // Wykonaj wyszukiwanie Z diagnostyką (tylko gdy włączone logowanie)
            var searchResult = await _searchService.SearchAsync(searchRequest, enableDiagnostics: _enableLogging);

            // 🚀 OPTYMALIZACJA: Nie loguj DiagnosticInfo (i tak jest pusty gdy diagnostyka wyłączona)
            if (_enableLogging && !string.IsNullOrEmpty(searchResult.DiagnosticInfo))
            {
                LogDiagnostic("--- Log wyszukiwania ---");
                LogDiagnostic(searchResult.DiagnosticInfo);
            }

            // Mapuj wynik na VerificationResult
            var result = MapSearchResultToVerificationResult(sourceData, searchResult);

            if (_enableLogging)
            {
                LogDiagnostic($"Status końcowy: {result.Status}");
                LogDiagnostic($"Komunikat: {result.ErrorMessage ?? "(brak)"}");
            }

            return result;
        }

        /// <summary>
        /// Zapisuje logi diagnostyczne do pliku
        /// </summary>
        public async Task SaveDiagnosticLogAsync()
        {
            if (_enableLogging && _diagnosticLog != null)
            {
                _diagnosticLog.AppendLine();
                _diagnosticLog.AppendLine($"=== KONIEC LOGU - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

                try
                {
                    await File.WriteAllTextAsync(_logFilePath!, _diagnosticLog.ToString(), Encoding.UTF8);
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
            // 🚀 OPTYMALIZACJA: Sprawdź flagę zamiast string
            if (_enableLogging && _diagnosticLog != null)
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

            // ✅ SUKCES
            if (searchResult.Status == AddressSearchStatus.Success)
            {
                result.Status = "SUKCES";
                result.FoundData = CreateFoundAddress(searchResult, sourceData);
                return result;
            }

            // ✅ WIELOKROTNE DOPASOWANIE
            if (searchResult.Status == AddressSearchStatus.MultipleMatches)
            {
                result.Status = "BŁĄD";
                result.FoundData = CreateFoundAddress(searchResult, sourceData);
                result.ErrorMessage = "[F]" +searchResult.Message ?? AddressSearchStatusInfo.GetMessage(searchResult.Status);
                return result;
            }

            // ✅ WSZYSTKIE POZOSTAŁE BŁĘDY
            result.Status = "BŁĄD";
            
            // Użyj komunikatu z wyniku lub pobierz ze słownika
            result.ErrorMessage = searchResult.Message 
                ?? AddressSearchStatusInfo.GetMessage(searchResult.Status);

            // Jeśli znaleziono częściowy adres (np. miasto), dodaj go
            if (searchResult.Miasto != null || searchResult.Ulica != null)
            {
                result.FoundData = CreatePartialFoundAddress(searchResult, sourceData);
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
                Miasto = searchResult.Miasto?.Nazwa ?? sourceData.Miasto,
                Ulica = searchResult.Ulica != null
                    ? $"{searchResult.Ulica.Cecha} {searchResult.Ulica.Nazwa1}".Trim()
                    : sourceData.Ulica,
                Budynek = searchResult.NormalizedBuildingNumber ?? sourceData.Budynek,
                Lokal = searchResult.NormalizedApartmentNumber ?? sourceData.Lokal,
                Wojewodztwo = searchResult.Miasto?.Gmina?.Powiat?.Wojewodztwo?.Nazwa ?? sourceData.Wojewodztwo,
                Powiat = searchResult.Miasto?.Gmina?.Powiat?.Nazwa ?? sourceData.Powiat,
                Gmina = searchResult.Miasto?.Gmina?.Nazwa ?? sourceData.Gmina
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
                Miasto = searchResult.Miasto?.Nazwa ?? sourceData.Miasto,
                Ulica = searchResult.Ulica != null
                    ? $"{searchResult.Ulica.Cecha} {searchResult.Ulica.Nazwa1}".Trim()
                    : sourceData.Ulica, // ✅ Zostaw puste, NIE "Brak"
                Budynek = searchResult.NormalizedBuildingNumber ?? sourceData.Budynek,
                Lokal = searchResult.NormalizedApartmentNumber ?? sourceData.Lokal,
                Wojewodztwo = searchResult.Miasto?.Gmina?.Powiat?.Wojewodztwo?.Nazwa ?? sourceData.Wojewodztwo,
                Powiat = searchResult.Miasto?.Gmina?.Powiat?.Nazwa ?? sourceData.Powiat,
                Gmina = searchResult.Miasto?.Gmina?.Nazwa ?? sourceData.Gmina
            };
        }
    }
}