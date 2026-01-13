// Copyright (c) 2025-2026 Andrzej Szepczyñski. All rights reserved.

namespace TerytLoad.Configuration
{
    public static class DatabaseConfig
    {
        /// <summary>
        /// Domyœlny connection string u¿ywany gdy nie znaleziono konfiguracji w appsettings.json
        /// </summary>
        public const string DefaultConnectionString = "Server=.\\SQLEXPRESS;Database=Address;Trusted_Connection=True;TrustServerCertificate=True;";
    }
}