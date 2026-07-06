// pfx — Power Fx CLI stub
// This is a placeholder for the Power Fx evaluator CLI.
// The actual implementation would be a .NET 9 console app using
// the Microsoft.PowerFx NuGet packages.
//
// Build command:
//   dotnet publish -c Release -r linux-x64 --self-contained -o /opt/tools/pfx/
//
// Usage:
//   pfx eval "Sum(1,2,3)"
//   pfx eval "Filter([{Name:\"A\",Age:30},{Name:\"B\",Age:25}], Age > 27)"
//   pfx eval --data /workspace/data.csv "CountRows(data)"
//
// NuGet packages:
//   Microsoft.PowerFx.Core
//   Microsoft.PowerFx.Interpreter
//   Microsoft.PowerFx.Transport.Attributes
//
// Output format:
//   For scalars: prints the value directly (e.g., "6", "true", "Hello")
//   For tables: prints JSON array (e.g., [{"Name":"A","Age":30}])
//   For records: prints JSON object (e.g., {"Name":"A","Age":30})
//   For errors: prints to stderr and exits non-zero
//
// Supported types:
//   Number, Text, Boolean, Date, Time, DateTime, Table, Record, Blank, Error
//
// When --data is used:
//   Loads the CSV into a PowerFx table variable named after the file stem.
//   e.g., --data /workspace/sales.csv creates variable "sales" as a Table.
