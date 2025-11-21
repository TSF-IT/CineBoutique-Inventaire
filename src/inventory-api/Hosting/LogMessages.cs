namespace CineBoutique.Inventory.Api.Hosting
{
    internal static class Log
    {
        private static readonly Action<ILogger, bool, bool, Exception?> _migrationsFlags =
            LoggerMessage.Define<bool, bool>(
                LogLevel.Information,
                new EventId(1000, nameof(MigrationsFlags)),
                "APPLY_MIGRATIONS={Apply} DISABLE_MIGRATIONS={Disable}");

        internal static void MigrationsFlags(ILogger logger, bool apply, bool disable) =>
            _migrationsFlags(logger, apply, disable, null);

        private static readonly Action<ILogger, string, Exception?> _dbHostDb =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(1001, nameof(DbHostDb)),
                "Database host: {Host}");

        internal static void DbHostDb(ILogger logger, string host) =>
            _dbHostDb(logger, host, null);

        private static readonly Action<ILogger, bool, Exception?> _seedOnStartup =
            LoggerMessage.Define<bool>(
                LogLevel.Information,
                new EventId(1002, nameof(SeedOnStartup)),
                "SeedOnStartup={Seed}");

        internal static void SeedOnStartup(ILogger logger, bool seed) =>
            _seedOnStartup(logger, seed, null);

        private static readonly Action<ILogger, int, int, Exception?> _migrationRetry =
            LoggerMessage.Define<int, int>(
                LogLevel.Warning,
                new EventId(1003, nameof(MigrationRetry)),
                "Échec de migration tentative {Attempt}/{MaxAttempts}, nouvel essai...");

        internal static void MigrationRetry(ILogger logger, int attempt, int maxAttempts, Exception exception) =>
            _migrationRetry(logger, attempt, maxAttempts, exception);

        private static readonly Action<ILogger, Exception?> _migrationsSkipped =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(1004, nameof(MigrationsSkipped)),
                "Migrations skipped (DISABLE_MIGRATIONS=true)");

        internal static void MigrationsSkipped(ILogger logger) =>
            _migrationsSkipped(logger, null);
    }
}
