using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Types;

namespace PowerFxCli;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2 || args[0] != "eval")
        {
            Console.Error.WriteLine("Usage: pfx eval \"<expression>\" [--data /path/to/file.csv]");
            return 1;
        }

        var expression = args[1];
        string? dataFile = null;

        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i] == "--data" && i + 1 < args.Length)
            {
                dataFile = args[i + 1];
                i++;
            }
        }

        try
        {
            var config = new PowerFxConfig();
            var engine = new RecalcEngine(config);

            // Load CSV data if provided
            if (!string.IsNullOrEmpty(dataFile) && File.Exists(dataFile))
            {
                var tableName = Path.GetFileNameWithoutExtension(dataFile);
                var tableValue = LoadCsvAsTable(dataFile);
                if (tableValue != null)
                {
                    engine.UpdateVariable(tableName, tableValue);
                }
            }

            var result = engine.Eval(expression);

            if (result is ErrorValue errorValue)
            {
                foreach (var error in errorValue.Errors)
                {
                    Console.Error.WriteLine($"Error: {error.Message}");
                }
                return 1;
            }

            var output = FormatResult(result);
            Console.WriteLine(output);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static string FormatResult(FormulaValue value)
    {
        return value switch
        {
            NumberValue nv => nv.Value.ToString(CultureInfo.InvariantCulture),
            StringValue sv => sv.Value,
            BooleanValue bv => bv.Value ? "true" : "false",
            DateValue dv => dv.GetConvertedValue(null).ToString("yyyy-MM-dd"),
            DateTimeValue dtv => dtv.GetConvertedValue(null).ToString("O"),
            TimeValue tv => tv.Value.ToString(),
            BlankValue => "Blank()",
            TableValue tv => FormatTable(tv),
            RecordValue rv => FormatRecord(rv),
            _ => value.ToObject()?.ToString() ?? "Blank()"
        };
    }

    static string FormatTable(TableValue table)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (var row in table.Rows)
        {
            if (row.IsValue)
            {
                rows.Add(RecordToDict(row.Value));
            }
        }
        return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
    }

    static string FormatRecord(RecordValue record)
    {
        var dict = RecordToDict(record);
        return JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
    }

    static Dictionary<string, object?> RecordToDict(RecordValue record)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var field in record.Fields)
        {
            dict[field.Name] = field.Value switch
            {
                NumberValue nv => nv.Value,
                StringValue sv => sv.Value,
                BooleanValue bv => bv.Value,
                BlankValue => null,
                _ => field.Value.ToObject()
            };
        }
        return dict;
    }

    static TableValue? LoadCsvAsTable(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length < 2) return null;

        var headers = lines[0].Split(',');
        var rows = new List<RecordValue>();

        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(',');
            var fields = new List<NamedValue>();

            for (int j = 0; j < headers.Length && j < values.Length; j++)
            {
                var header = headers[j].Trim().Trim('"');
                var val = values[j].Trim().Trim('"');

                if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var numVal))
                {
                    fields.Add(new NamedValue(header, FormulaValue.New(numVal)));
                }
                else if (bool.TryParse(val, out var boolVal))
                {
                    fields.Add(new NamedValue(header, FormulaValue.New(boolVal)));
                }
                else
                {
                    fields.Add(new NamedValue(header, FormulaValue.New(val)));
                }
            }

            rows.Add(FormulaValue.NewRecordFromFields(fields));
        }

        if (rows.Count == 0) return null;
        return FormulaValue.NewTable(rows[0].Type, rows);
    }
}
