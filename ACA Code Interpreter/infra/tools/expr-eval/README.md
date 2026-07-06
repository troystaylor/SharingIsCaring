// expr-eval — Power Automate Expression Evaluator stub
// This is a placeholder for the .NET expression evaluator DLL.
// The actual implementation would be a .NET 9 console app using
// the Azure Logic Apps expression evaluation library.
//
// Build command:
//   dotnet publish -c Release -r linux-x64 --self-contained -o /opt/tools/expr-eval/
//
// For now, this README documents the expected behavior.
//
// Usage: dotnet /opt/tools/expr-eval/expr-eval.dll "<expression>"
//
// Supported functions (subset of Workflow Definition Language):
//   String: concat, substring, replace, split, trim, toLower, toUpper, startsWith, endsWith, contains, indexOf, lastIndexOf, length, guid, base64, decodeBase64
//   DateTime: utcNow, formatDateTime, addDays, addHours, addMinutes, addSeconds, dayOfWeek, dayOfMonth, dayOfYear, ticks, startOfDay, startOfMonth, startOfYear
//   Math: add, sub, mul, div, mod, min, max, rand
//   Logical: if, equals, and, or, not, greater, greaterOrEquals, less, lessOrEquals
//   Collection: length, first, last, take, skip, union, intersection, contains, empty, join, array, createArray
//   Conversion: int, float, string, bool, json, xml, base64, decodeBase64, dataUri, decodeDataUri, uriComponent, uriComponentToString
//   Reference: triggerBody, triggerOutputs, body, outputs, actions, workflow, item, items
//
// Output format:
//   {"result": "<evaluated-value>", "type": "<result-type>", "error": null}
//   {"result": null, "type": null, "error": "Unknown function: xyz"}
