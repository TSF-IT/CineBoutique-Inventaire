using Xunit;

// Désactive le parallélisme des tests *dans ce projet*.
// Utile pour les tests d'intégration qui partagent une même base/Postgres.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
