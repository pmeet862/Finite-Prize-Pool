using System.Runtime.CompilerServices;

// Deal internals (the ticket list) are visible to the test assembly only.
// The game, debug and editor assemblies must go through IDealService.
[assembly: InternalsVisibleTo("FinitePool.Tests.EditMode")]
