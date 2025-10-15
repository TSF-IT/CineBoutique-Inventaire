# CineBoutique.Inventory.Domain.Tests

Cette batterie de tests valide la logique de comptage purement Domain, sans dépendance aux couches API ou Infrastructure.

## Couverture fonctionnelle

- **CountingRulesTests** :
  - agrégation des lignes de comptage par EAN ;
  - application des tolérances sur les écarts successifs ;
  - gestion des cas pathologiques (doublons, quantités négatives, overflow).
- **ConflictResolutionTests** :
  - transitions autorisées d'un `CountingRun` (de `not_started` à `completed`) ;
  - règles d'arrêt anticipé (interdiction de compléter un run vide) ;
  - verrouillage d'une zone pour éviter plusieurs runs actifs ;
  - résolution des conflits lorsqu'un nouvel inventaire confirme la même quantité.

## Exécution

```bash
dotnet test tests/inventory.domain.tests/CineBoutique.Inventory.Domain.Tests.csproj
```

Les tests s'exécutent en mémoire et ne nécessitent ni Postgres ni dépendance externe.
