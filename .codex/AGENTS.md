# AGENTS – Règles d’exécution Codex

## Build & Tests
Toujours exécuter les tests via le script racine :

    bash ./run_tests.sh

Ne pas lancer `dotnet test` directement : l’agent peut ne pas embarquer le SDK.
Le script `run_tests.sh` installe automatiquement .NET 8 LTS si absent.

## Sorties intéressantes
- TRX: test-results/dotnet/tests.trx
- (option) Couverture: test-results/dotnet/coverage.cobertura.xml
