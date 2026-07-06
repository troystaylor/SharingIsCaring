using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExpressionEvaluator;

/// <summary>
/// Power Automate Workflow Definition Language expression evaluator.
/// Supports a subset of functions available in Power Automate expressions.
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: expr-eval \"<expression>\"");
            return 1;
        }

        var expression = args[0];

        try
        {
            var result = Evaluate(expression);
            var output = new Dictionary<string, object?>
            {
                ["result"] = result.Value,
                ["type"] = result.Type,
                ["error"] = null
            };
            Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            var output = new Dictionary<string, object?>
            {
                ["result"] = null,
                ["type"] = null,
                ["error"] = ex.Message
            };
            Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
    }

    static (object? Value, string Type) Evaluate(string expression)
    {
        // Strip @{} wrapper if present
        expression = expression.Trim();
        if (expression.StartsWith("@{") && expression.EndsWith("}"))
            expression = expression[2..^1];

        // Parse function call
        var match = Regex.Match(expression, @"^(\w+)\((.*)\)$", RegexOptions.Singleline);
        if (!match.Success)
        {
            // Literal string or number
            if (double.TryParse(expression, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                return (num, "number");
            if (bool.TryParse(expression, out var b))
                return (b, "boolean");
            return (expression.Trim('\''), "string");
        }

        var funcName = match.Groups[1].Value.ToLower();
        var argsStr = match.Groups[2].Value;

        return funcName switch
        {
            "utcnow" => (DateTime.UtcNow.ToString("O"), "string"),
            "guid" => (Guid.NewGuid().ToString(), "string"),
            "newguid" => (Guid.NewGuid().ToString(), "string"),

            "concat" => EvalConcat(argsStr),
            "tolower" => EvalStringFunc(argsStr, s => s.ToLower()),
            "toupper" => EvalStringFunc(argsStr, s => s.ToUpper()),
            "trim" => EvalStringFunc(argsStr, s => s.Trim()),
            "length" => EvalLength(argsStr),

            "formatdatetime" => EvalFormatDateTime(argsStr),
            "adddays" => EvalDateAdd(argsStr, "days"),
            "addhours" => EvalDateAdd(argsStr, "hours"),
            "addminutes" => EvalDateAdd(argsStr, "minutes"),
            "addseconds" => EvalDateAdd(argsStr, "seconds"),
            "startofday" => EvalStartOf(argsStr, "day"),
            "startofmonth" => EvalStartOf(argsStr, "month"),
            "startofyear" => EvalStartOf(argsStr, "year"),
            "dayofweek" => (((int)DateTime.UtcNow.DayOfWeek), "integer"),
            "dayofmonth" => (DateTime.UtcNow.Day, "integer"),
            "dayofyear" => (DateTime.UtcNow.DayOfYear, "integer"),
            "ticks" => (DateTime.UtcNow.Ticks, "integer"),

            "add" => EvalMath(argsStr, (a, b) => a + b),
            "sub" => EvalMath(argsStr, (a, b) => a - b),
            "mul" => EvalMath(argsStr, (a, b) => a * b),
            "div" => EvalMath(argsStr, (a, b) => b != 0 ? a / b : throw new DivideByZeroException()),
            "mod" => EvalMath(argsStr, (a, b) => a % b),
            "min" => EvalMath(argsStr, Math.Min),
            "max" => EvalMath(argsStr, Math.Max),
            "rand" => (new Random().Next(ParseInt(argsStr.Split(',')[0]), ParseInt(argsStr.Split(',')[1])), "integer"),

            "int" => (ParseInt(argsStr.Trim().Trim('\'')), "integer"),
            "float" => (double.Parse(argsStr.Trim().Trim('\''), CultureInfo.InvariantCulture), "float"),
            "string" => (argsStr.Trim().Trim('\''), "string"),
            "bool" => (bool.Parse(argsStr.Trim().Trim('\'')), "boolean"),

            "if" => EvalIf(argsStr),
            "equals" => EvalEquals(argsStr),
            "and" => EvalLogical(argsStr, true),
            "or" => EvalLogical(argsStr, false),
            "not" => (!(bool.Parse(argsStr.Trim().Trim('\''))), "boolean"),
            "greater" => EvalCompare(argsStr, (a, b) => a > b),
            "greaterorequals" => EvalCompare(argsStr, (a, b) => a >= b),
            "less" => EvalCompare(argsStr, (a, b) => a < b),
            "lessorequals" => EvalCompare(argsStr, (a, b) => a <= b),

            "split" => EvalSplit(argsStr),
            "join" => EvalJoin(argsStr),
            "replace" => EvalReplace(argsStr),
            "substring" => EvalSubstring(argsStr),
            "indexof" => EvalIndexOf(argsStr),
            "lastindexof" => EvalLastIndexOf(argsStr),
            "startswith" => EvalStartsWith(argsStr),
            "endswith" => EvalEndsWith(argsStr),
            "contains" => EvalContains(argsStr),

            "base64" => (Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(argsStr.Trim().Trim('\''))), "string"),
            "decodebase64" or "base64tostring" => (System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(argsStr.Trim().Trim('\''))), "string"),

            "json" => EvalJson(argsStr),
            "empty" => (string.IsNullOrEmpty(argsStr.Trim().Trim('\'')), "boolean"),

            _ => throw new NotSupportedException($"Unknown function: {funcName}")
        };
    }

    // ─── Helper Methods ───────────────────────────────────────────────────────

    static string[] SplitArgs(string argsStr)
    {
        var args = new List<string>();
        int depth = 0;
        int start = 0;
        bool inString = false;
        char strChar = '\'';

        for (int i = 0; i < argsStr.Length; i++)
        {
            var c = argsStr[i];
            if ((c == '\'' || c == '"') && (i == 0 || argsStr[i - 1] != '\\'))
            {
                if (!inString) { inString = true; strChar = c; }
                else if (c == strChar) { inString = false; }
            }
            else if (!inString)
            {
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (c == ',' && depth == 0)
                {
                    args.Add(argsStr[start..i].Trim());
                    start = i + 1;
                }
            }
        }
        args.Add(argsStr[start..].Trim());
        return args.ToArray();
    }

    static string CleanArg(string arg) => arg.Trim().Trim('\'').Trim('"');

    static int ParseInt(string s) => int.Parse(CleanArg(s), CultureInfo.InvariantCulture);
    static double ParseDouble(string s) => double.Parse(CleanArg(s), CultureInfo.InvariantCulture);

    static (object?, string) EvalConcat(string argsStr)
    {
        var parts = SplitArgs(argsStr);
        var result = string.Join("", parts.Select(CleanArg));
        return (result, "string");
    }

    static (object?, string) EvalStringFunc(string argsStr, Func<string, string> func)
        => (func(CleanArg(argsStr)), "string");

    static (object?, string) EvalLength(string argsStr)
        => (CleanArg(argsStr).Length, "integer");

    static (object?, string) EvalFormatDateTime(string argsStr)
    {
        var args = SplitArgs(argsStr);
        var dt = args[0].Trim().Trim('\'');
        var format = args.Length > 1 ? CleanArg(args[1]) : "O";

        DateTime dateTime;
        if (dt.Equals("utcNow()", StringComparison.OrdinalIgnoreCase))
            dateTime = DateTime.UtcNow;
        else
            dateTime = DateTime.Parse(dt, CultureInfo.InvariantCulture);

        return (dateTime.ToString(format), "string");
    }

    static (object?, string) EvalDateAdd(string argsStr, string unit)
    {
        var args = SplitArgs(argsStr);
        var dt = DateTime.Parse(CleanArg(args[0]), CultureInfo.InvariantCulture);
        var amount = ParseDouble(args[1]);

        var result = unit switch
        {
            "days" => dt.AddDays(amount),
            "hours" => dt.AddHours(amount),
            "minutes" => dt.AddMinutes(amount),
            "seconds" => dt.AddSeconds(amount),
            _ => dt
        };
        return (result.ToString("O"), "string");
    }

    static (object?, string) EvalStartOf(string argsStr, string unit)
    {
        var dt = DateTime.Parse(CleanArg(argsStr), CultureInfo.InvariantCulture);
        var result = unit switch
        {
            "day" => dt.Date,
            "month" => new DateTime(dt.Year, dt.Month, 1),
            "year" => new DateTime(dt.Year, 1, 1),
            _ => dt
        };
        return (result.ToString("O"), "string");
    }

    static (object?, string) EvalMath(string argsStr, Func<double, double, double> op)
    {
        var args = SplitArgs(argsStr);
        var a = ParseDouble(args[0]);
        var b = ParseDouble(args[1]);
        return (op(a, b), "number");
    }

    static (object?, string) EvalIf(string argsStr)
    {
        var args = SplitArgs(argsStr);
        var condition = bool.Parse(CleanArg(args[0]));
        return condition ? (CleanArg(args[1]), "string") : (CleanArg(args[2]), "string");
    }

    static (object?, string) EvalEquals(string argsStr)
    {
        var args = SplitArgs(argsStr);
        return (CleanArg(args[0]) == CleanArg(args[1]), "boolean");
    }

    static (object?, string) EvalLogical(string argsStr, bool isAnd)
    {
        var args = SplitArgs(argsStr);
        var values = args.Select(a => bool.Parse(CleanArg(a)));
        return (isAnd ? values.All(v => v) : values.Any(v => v), "boolean");
    }

    static (object?, string) EvalCompare(string argsStr, Func<double, double, bool> op)
    {
        var args = SplitArgs(argsStr);
        return (op(ParseDouble(args[0]), ParseDouble(args[1])), "boolean");
    }

    static (object?, string) EvalSplit(string argsStr)
    {
        var args = SplitArgs(argsStr);
        var result = CleanArg(args[0]).Split(CleanArg(args[1]));
        return (JsonSerializer.Serialize(result), "array");
    }

    static (object?, string) EvalJoin(string argsStr)
    {
        var args = SplitArgs(argsStr);
        var arr = JsonSerializer.Deserialize<string[]>(CleanArg(args[0])) ?? Array.Empty<string>();
        return (string.Join(CleanArg(args[1]), arr), "string");
    }

    static (object?, string) EvalReplace(string argsStr)
    {
        var args = SplitArgs(argsStr);
        return (CleanArg(args[0]).Replace(CleanArg(args[1]), CleanArg(args[2])), "string");
    }

    static (object?, string) EvalSubstring(string argsStr)
    {
        var args = SplitArgs(argsStr);
        var str = CleanArg(args[0]);
        var start = ParseInt(args[1]);
        var len = args.Length > 2 ? ParseInt(args[2]) : str.Length - start;
        return (str.Substring(start, Math.Min(len, str.Length - start)), "string");
    }

    static (object?, string) EvalIndexOf(string argsStr)
    {
        var args = SplitArgs(argsStr);
        return (CleanArg(args[0]).IndexOf(CleanArg(args[1])), "integer");
    }

    static (object?, string) EvalLastIndexOf(string argsStr)
    {
        var args = SplitArgs(argsStr);
        return (CleanArg(args[0]).LastIndexOf(CleanArg(args[1])), "integer");
    }

    static (object?, string) EvalStartsWith(string argsStr)
    {
        var args = SplitArgs(argsStr);
        return (CleanArg(args[0]).StartsWith(CleanArg(args[1])), "boolean");
    }

    static (object?, string) EvalEndsWith(string argsStr)
    {
        var args = SplitArgs(argsStr);
        return (CleanArg(args[0]).EndsWith(CleanArg(args[1])), "boolean");
    }

    static (object?, string) EvalContains(string argsStr)
    {
        var args = SplitArgs(argsStr);
        return (CleanArg(args[0]).Contains(CleanArg(args[1])), "boolean");
    }

    static (object?, string) EvalJson(string argsStr)
    {
        var input = CleanArg(argsStr);
        try
        {
            var doc = JsonDocument.Parse(input);
            return (input, "object");
        }
        catch
        {
            return (input, "string");
        }
    }
}
