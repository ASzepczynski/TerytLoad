// Copyright (c) 2025-2026 Andrzej Szepczyñski. All rights reserved.

using TerytLoad.Pages.VerifyAddresses.Models;

namespace TerytLoad.Pages.VerifyAddresses.Services
{
    public class AddressDataParser
    {
        public AddressData ParseLine(string line)
        {
            var parts = line.Split('|');

            if (parts.Length < 9)
            {
                throw new FormatException("Nieprawid³owy format linii");
            }

            return new AddressData
            {
                Id = parts[0],
                Kod = parts[1],
                Miasto = parts[2],
                Ulica = parts[3],
                Budynek = parts[4],
                Lokal = parts[5],
                Wojewodztwo = parts[6],
                Powiat = parts[7],
                Gmina = parts[8]
            };
        }
    }
}