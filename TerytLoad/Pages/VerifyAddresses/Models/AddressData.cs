// Copyright (c) 2025-2026 Andrzej Szepczyñski. All rights reserved.

namespace TerytLoad.Pages.VerifyAddresses.Models
{
    public class AddressData
    {
        public string Id { get; set; } = "";
        public string Kod { get; set; } = "";
        public string Miejscowosc { get; set; } = "";
        public string Ulica { get; set; } = "";
        public string Budynek { get; set; } = "";
        public string Lokal { get; set; } = "";
        public string Wojewodztwo { get; set; } = "";
        public string Powiat { get; set; } = "";
        public string Gmina { get; set; } = "";
    }
}