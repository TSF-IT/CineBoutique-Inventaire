// Ce fichier n'affecte QUE le projet de tests.
// On garde les analyzers en prod, on tolère les cas classiques en tests.
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
  "Naming",
  "CA1707:Identifiers should not contain underscores",
  Justification = "Underscores allowed in test method names for readability",
  Scope = "module")]

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
  "Reliability",
  "CA2007:Do not directly await tasks",
  Justification = "ConfigureAwait not required in test code",
  Scope = "module")]
