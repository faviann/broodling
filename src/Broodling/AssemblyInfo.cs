using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Broodling.Tests")]
[assembly: InternalsVisibleTo("Broodling.ProcessWitness")]
// The server composes the preparer, progressor and observer with their test-controlled peers and clocks.
[assembly: InternalsVisibleTo("Broodling.Host")]
