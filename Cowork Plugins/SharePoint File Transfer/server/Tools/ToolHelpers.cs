using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharePointTransferMcp.Tools;

internal static class ToolHelpers
{
	public static JsonObject ContentResult(JsonNode? payload, bool isError = false)
	{
		string text = payload?.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = false
		}) ?? "null";
		return new JsonObject
		{
			["content"] = new JsonArray
			{
				new JsonObject
				{
					["type"] = "text",
					["text"] = text
				}
			},
			["isError"] = isError
		};
	}

	public static string RequireString(JsonObject args, string name)
	{
		string text = args[name]?.GetValue<string>();
		if (string.IsNullOrEmpty(text))
		{
			throw new ArgumentException("missing required parameter: " + name);
		}
		return text;
	}

	public static string? OptString(JsonObject args, string name)
	{
		string value;
		return (args[name] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out value)) ? value : null;
	}

	public static int? OptInt(JsonObject args, string name)
	{
		if (args[name] is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<int>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<long>(out var value2))
			{
				return (int)value2;
			}
			if (jsonValue.TryGetValue<string>(out string value3) && int.TryParse(value3, out var result))
			{
				return result;
			}
		}
		return null;
	}

	public static long? OptLong(JsonObject args, string name)
	{
		if (args[name] is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<long>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<int>(out var value2))
			{
				return value2;
			}
			if (jsonValue.TryGetValue<string>(out string value3) && long.TryParse(value3, out var result))
			{
				return result;
			}
		}
		return null;
	}

	public static bool OptBool(JsonObject args, string name)
	{
		if (args[name] is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<bool>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<string>(out string value2) && bool.TryParse(value2, out var result))
			{
				return result;
			}
		}
		return false;
	}

	public static JsonObject RequireObject(JsonObject args, string name)
	{
		if (args[name] is JsonObject result)
		{
			return result;
		}
		throw new ArgumentException("missing required object parameter: " + name);
	}

	public static JsonObject? OptObject(JsonObject args, string name)
	{
		return args[name] as JsonObject;
	}

	public static JsonObject Schema(JsonObject properties, params string[] required)
	{
		JsonArray jsonArray = new JsonArray();
		foreach (string value in required)
		{
			jsonArray.Add(value);
		}
		JsonObject jsonObject = new JsonObject
		{
			["type"] = "object",
			["properties"] = properties,
			["additionalProperties"] = false
		};
		if (required.Length != 0)
		{
			jsonObject["required"] = jsonArray;
		}
		return jsonObject;
	}

	public static JsonObject SchemaOpenObject(JsonObject properties, params string[] required)
	{
		JsonArray jsonArray = new JsonArray();
		foreach (string value in required)
		{
			jsonArray.Add(value);
		}
		JsonObject jsonObject = new JsonObject
		{
			["type"] = "object",
			["properties"] = properties,
			["additionalProperties"] = true
		};
		if (required.Length != 0)
		{
			jsonObject["required"] = jsonArray;
		}
		return jsonObject;
	}

	public static JsonObject Prop(string type, string description, JsonObject? extra = null)
	{
		JsonObject jsonObject = new JsonObject
		{
			["type"] = type,
			["description"] = description
		};
		if (extra != null)
		{
			foreach (KeyValuePair<string, JsonNode> item in extra)
			{
				jsonObject[item.Key] = item.Value?.DeepClone();
			}
		}
		return jsonObject;
	}
}
