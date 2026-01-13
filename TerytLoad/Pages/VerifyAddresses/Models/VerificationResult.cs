// Copyright (c) 2025-2026 Andrzej Szepczyñski. All rights reserved.

namespace TerytLoad.Pages.VerifyAddresses.Models
{
    public class VerificationResult
    {
        public string SourceId { get; set; } = "";
        public string Status { get; set; } = "";
        public AddressData SourceData { get; set; } = new();
        public AddressData? FoundData { get; set; }
        public string? ErrorMessage { get; set; }
    }
}