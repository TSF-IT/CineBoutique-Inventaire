# Audit des dépendances

Lancer l'audit standard :
```
pwsh ./scripts/audit.ps1
```

Lancer l'audit avec la section Docker (trivy requis dans le PATH) :
```
pwsh ./scripts/audit.ps1 -ScanDocker
```
