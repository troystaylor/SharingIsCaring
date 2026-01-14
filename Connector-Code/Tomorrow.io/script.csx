using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private static readonly string SERVER_NAME = "TomorrowIOConnectorMcpServer";
    private static readonly string SERVER_VERSION = "1.0.0";
    
    // Tool definitions - Tomorrow.io weather API tools
    private static readonly JArray AVAILABLE_TOOLS = new JArray
    {
        new JObject
        {
            ["name"] = "get_weather_forecast",
            ["description"] = "Get comprehensive weather forecast for any location worldwide. Returns all core weather data automatically. Premium users can access minutely forecasts.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["location"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Location for weather forecast. Supports coordinates (lat,lon), city names, US zip codes, UK postcodes. Examples: '42.3478,-71.0466', 'New York', '10001 US', 'SW1A 1AA UK'"
                    },
                    ["timesteps"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Time intervals for forecasts. Premium: 1m, 5m, 15m, 30m. Standard: 1h, 1d",
                        ["items"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray { "1m", "5m", "15m", "30m", "1h", "1d" }
                        },
                        ["default"] = new JArray { "1h", "1d" }
                    },
                    ["units"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Unit system for response values",
                        ["enum"] = new JArray { "metric", "imperial" },
                        ["default"] = "metric"
                    }
                },
                ["required"] = new JArray { "location" }
            }
        },
        new JObject
        {
            ["name"] = "get_realtime_weather",
            ["description"] = "Get current weather conditions in real-time for any location with minute-by-minute precision.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["location"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Location for current weather. Supports coordinates (lat,lon), city names, US zip codes, UK postcodes"
                    },
                    ["units"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Unit system for response values",
                        ["enum"] = new JArray { "metric", "imperial" },
                        ["default"] = "metric"
                    }
                },
                ["required"] = new JArray { "location" }
            }
        },
        new JObject
        {
            ["name"] = "get_weather_alerts",
            ["description"] = "Get active weather alerts for proactive risk management and business intelligence.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["location"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Location to check for weather alerts"
                    },
                    ["severity"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Filter by alert severity level",
                        ["enum"] = new JArray { "Minor", "Moderate", "Severe", "Extreme" }
                    },
                    ["event_type"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Filter by specific weather event type"
                    }
                },
                ["required"] = new JArray { "location" }
            }
        },
        new JObject
        {
            ["name"] = "get_historical_weather",
            ["description"] = "Get historical weather data for analysis and comparison. Useful for trends and pattern analysis.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["location"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Location for historical weather data"
                    },
                    ["timesteps"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Time intervals for historical data",
                        ["items"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray { "1h", "1d" }
                        },
                        ["default"] = new JArray { "1d" }
                    },
                    ["start_date"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Start date for historical data (YYYY-MM-DD format)",
                        ["pattern"] = "^\\d{4}-\\d{2}-\\d{2}$"
                    },
                    ["end_date"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "End date for historical data (YYYY-MM-DD format)",
                        ["pattern"] = "^\\d{4}-\\d{2}-\\d{2}$"
                    },
                    ["units"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Unit system for response values",
                        ["enum"] = new JArray { "metric", "imperial" },
                        ["default"] = "metric"
                    }
                },
                ["required"] = new JArray { "location" }
            }
        },
        new JObject
        {
            ["name"] = "get_air_quality",
            ["description"] = "Get current and forecast air quality data including PM2.5, PM10, ozone, NO2, and CO levels for environmental monitoring.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["location"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Location for air quality data"
                    },
                    ["forecast"] = new JObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Whether to include air quality forecast",
                        ["default"] = false
                    }
                },
                ["required"] = new JArray { "location" }
            }
        },
        new JObject
        {
            ["name"] = "get_marine_weather",
            ["description"] = "Get marine weather conditions including wave heights, swell information, and water temperature for maritime activities.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["location"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Marine location coordinates (lat,lon) for weather data"
                    },
                    ["timesteps"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Time intervals for marine forecasts",
                        ["items"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray { "1h", "1d" }
                        },
                        ["default"] = new JArray { "1h" }
                    }
                },
                ["required"] = new JArray { "location" }
            }
        },
        new JObject
        {
            ["name"] = "get_aviation_weather",
            ["description"] = "Get aviation weather data including visibility, turbulence, and icing conditions for flight planning and operations.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["location"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Aviation location (airport code or coordinates) for weather data"
                    },
                    ["timesteps"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Time intervals for aviation forecasts",
                        ["items"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray { "1h", "1d" }
                        },
                        ["default"] = new JArray { "1h" }
                    }
                },
                ["required"] = new JArray { "location" }
            }
        },
        new JObject
        {
            ["name"] = "get_climate_normals",
            ["description"] = "Get 20-year climate statistics and normals for long-term planning and climate analysis.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["location"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Location for climate normal data"
                    },
                    ["start_date"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Start date for climate normals (MM-DD format)",
                        ["pattern"] = "^\\d{2}-\\d{2}$"
                    },
                    ["end_date"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "End date for climate normals (MM-DD format)",
                        ["pattern"] = "^\\d{2}-\\d{2}$"
                    }
                },
                ["required"] = new JArray { "location" }
            }
        },
        new JObject
        {
            ["name"] = "get_route_weather",
            ["description"] = "Get weather conditions along a travel route for logistics and transportation planning.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["origin"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Starting location for the route"
                    },
                    ["destination"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Ending location for the route"
                    },
                    ["departure_time"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Planned departure time (ISO 8601 format)",
                        ["format"] = "date-time"
                    }
                },
                ["required"] = new JArray { "origin", "destination" }
            }
        }
    };
    
    // Tool implementations for Tomorrow.io API calls
    
    private async Task<JObject> ExecuteGetWeatherForecastTool(JObject arguments)
    {
        var location = arguments.GetValue("location")?.ToString();
        var timesteps = arguments.GetValue("timesteps") as JArray ?? new JArray { "1h", "1d" };
        var units = arguments.GetValue("units")?.ToString() ?? "metric";

        if (string.IsNullOrEmpty(location))
        {
            throw new ArgumentException("location parameter is required");
        }

        var queryParams = new List<string>
        {
            $"location={Uri.EscapeDataString(location)}",
            $"timesteps={Uri.EscapeDataString(string.Join(",", timesteps.Select(t => t.ToString())))}",
            $"units={units}"
        };

        var endpoint = "/weather/forecast?" + string.Join("&", queryParams);
        var response = await MakeTomorrowIOApiCall(endpoint);

        return FormatWeatherForecastResponse(response, location, timesteps.Select(t => t.ToString()).ToArray());
    }
    
    private async Task<JObject> ExecuteGetRealtimeWeatherTool(JObject arguments)
    {
        var location = arguments.GetValue("location")?.ToString();
        var units = arguments.GetValue("units")?.ToString() ?? "metric";

        if (string.IsNullOrEmpty(location))
        {
            throw new ArgumentException("location parameter is required");
        }

        var queryParams = new List<string>
        {
            $"location={Uri.EscapeDataString(location)}",
            $"units={units}"
        };

        var endpoint = "/weather/realtime?" + string.Join("&", queryParams);
        var response = await MakeTomorrowIOApiCall(endpoint);

        return FormatRealtimeWeatherResponse(response, location);
    }
    
    private async Task<JObject> ExecuteGetWeatherAlertsTool(JObject arguments)
    {
        var location = arguments.GetValue("location")?.ToString();
        var severity = arguments.GetValue("severity")?.ToString();
        var eventType = arguments.GetValue("event_type")?.ToString();

        if (string.IsNullOrEmpty(location))
        {
            throw new ArgumentException("location parameter is required");
        }

        var queryParams = new List<string>
        {
            $"location={Uri.EscapeDataString(location)}"
        };

        if (!string.IsNullOrEmpty(severity))
            queryParams.Add($"severity={Uri.EscapeDataString(severity)}");
        if (!string.IsNullOrEmpty(eventType))
            queryParams.Add($"event={Uri.EscapeDataString(eventType)}");

        var endpoint = "/alerts?" + string.Join("&", queryParams);
        var response = await MakeTomorrowIOApiCall(endpoint);

        return FormatWeatherAlertsResponse(response, location);
    }
    
    private async Task<JObject> ExecuteGetHistoricalWeatherTool(JObject arguments)
    {
        var location = arguments.GetValue("location")?.ToString();
        var timesteps = arguments.GetValue("timesteps") as JArray ?? new JArray { "1d" };
        var startDate = arguments.GetValue("start_date")?.ToString();
        var endDate = arguments.GetValue("end_date")?.ToString();
        var units = arguments.GetValue("units")?.ToString() ?? "metric";

        if (string.IsNullOrEmpty(location))
        {
            throw new ArgumentException("location parameter is required");
        }

        var queryParams = new List<string>
        {
            $"location={Uri.EscapeDataString(location)}",
            $"timesteps={Uri.EscapeDataString(string.Join(",", timesteps.Select(t => t.ToString())))}",
            $"units={units}"
        };

        if (!string.IsNullOrEmpty(startDate))
            queryParams.Add($"startTime={startDate}T00:00:00Z");
        if (!string.IsNullOrEmpty(endDate))
            queryParams.Add($"endTime={endDate}T23:59:59Z");

        var endpoint = "/weather/history/recent?" + string.Join("&", queryParams);
        var response = await MakeTomorrowIOApiCall(endpoint);

        return FormatHistoricalWeatherResponse(response, location, startDate, endDate);
    }
    
    private async Task<JObject> ExecuteGetAirQualityTool(JObject arguments)
    {
        var location = arguments.GetValue("location")?.ToString();
        var forecast = arguments.GetValue("forecast")?.ToObject<bool?>() ?? false;

        if (string.IsNullOrEmpty(location))
        {
            throw new ArgumentException("location parameter is required");
        }

        var queryParams = new List<string>
        {
            $"location={Uri.EscapeDataString(location)}"
        };

        var endpoint = forecast ? "/air-quality/forecast?" : "/air-quality/realtime?";
        endpoint += string.Join("&", queryParams);
        
        var response = await MakeTomorrowIOApiCall(endpoint);

        return FormatAirQualityResponse(response, location, forecast);
    }
    
    private async Task<JObject> ExecuteGetMarineWeatherTool(JObject arguments)
    {
        var location = arguments.GetValue("location")?.ToString();
        var timesteps = arguments.GetValue("timesteps") as JArray ?? new JArray { "1h" };

        if (string.IsNullOrEmpty(location))
        {
            throw new ArgumentException("location parameter is required");
        }

        var queryParams = new List<string>
        {
            $"location={Uri.EscapeDataString(location)}",
            $"timesteps={Uri.EscapeDataString(string.Join(",", timesteps.Select(t => t.ToString())))}"
        };

        var endpoint = "/weather/marine?" + string.Join("&", queryParams);
        var response = await MakeTomorrowIOApiCall(endpoint);

        return FormatMarineWeatherResponse(response, location);
    }
    
    private async Task<JObject> ExecuteGetAviationWeatherTool(JObject arguments)
    {
        var location = arguments.GetValue("location")?.ToString();
        var timesteps = arguments.GetValue("timesteps") as JArray ?? new JArray { "1h" };

        if (string.IsNullOrEmpty(location))
        {
            throw new ArgumentException("location parameter is required");
        }

        var queryParams = new List<string>
        {
            $"location={Uri.EscapeDataString(location)}",
            $"timesteps={Uri.EscapeDataString(string.Join(",", timesteps.Select(t => t.ToString())))}"
        };

        var endpoint = "/weather/aviation?" + string.Join("&", queryParams);
        var response = await MakeTomorrowIOApiCall(endpoint);

        return FormatAviationWeatherResponse(response, location);
    }
    
    private async Task<JObject> ExecuteGetClimateNormalsTool(JObject arguments)
    {
        var location = arguments.GetValue("location")?.ToString();
        var startDate = arguments.GetValue("start_date")?.ToString();
        var endDate = arguments.GetValue("end_date")?.ToString();

        if (string.IsNullOrEmpty(location))
        {
            throw new ArgumentException("location parameter is required");
        }

        var queryParams = new List<string>
        {
            $"location={Uri.EscapeDataString(location)}"
        };

        if (!string.IsNullOrEmpty(startDate))
            queryParams.Add($"startDay={startDate}");
        if (!string.IsNullOrEmpty(endDate))
            queryParams.Add($"endDay={endDate}");

        var endpoint = "/weather/climate-normals?" + string.Join("&", queryParams);
        var response = await MakeTomorrowIOApiCall(endpoint);

        return FormatClimateNormalsResponse(response, location);
    }
    
    private async Task<JObject> ExecuteGetRouteWeatherTool(JObject arguments)
    {
        var origin = arguments.GetValue("origin")?.ToString();
        var destination = arguments.GetValue("destination")?.ToString();
        var departureTime = arguments.GetValue("departure_time")?.ToString();

        if (string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(destination))
        {
            throw new ArgumentException("origin and destination parameters are required");
        }

        var queryParams = new List<string>
        {
            $"origin={Uri.EscapeDataString(origin)}",
            $"destination={Uri.EscapeDataString(destination)}"
        };

        if (!string.IsNullOrEmpty(departureTime))
            queryParams.Add($"departureTime={Uri.EscapeDataString(departureTime)}");

        var endpoint = "/weather/route?" + string.Join("&", queryParams);
        var response = await MakeTomorrowIOApiCall(endpoint);

        return FormatRouteWeatherResponse(response, origin, destination);
    }

    // Response formatting methods
    
    private JObject FormatWeatherForecastResponse(JObject response, string location, string[] timesteps)
    {
        var timelines = response["timelines"];
        var locationInfo = response["location"];
        var text = new StringBuilder();
        
        text.AppendLine($"Weather Forecast for {location}:");
        text.AppendLine($"Location: {locationInfo?["name"]} ({locationInfo?["lat"]}, {locationInfo?["lon"]})");
        text.AppendLine();

        foreach (var timestep in timesteps)
        {
            var timeline = timelines?[GetTimelineKey(timestep)] as JArray;
            if (timeline != null && timeline.Count > 0)
            {
                text.AppendLine($"== {GetTimelineLabel(timestep)} Forecast ==");
                
                var count = Math.Min(timestep == "1d" ? 5 : 12, timeline.Count);
                for (int i = 0; i < count; i++)
                {
                    var period = timeline[i];
                    var time = DateTime.Parse(period["time"].ToString());
                    var values = period["values"];
                    
                    text.AppendLine($"{time:MMM dd, h:mm tt}:");
                    text.AppendLine($"  Temperature: {values["temperature"]}°, Feels like: {values["temperatureApparent"]}°");
                    text.AppendLine($"  Conditions: {GetWeatherDescription(values["weatherCode"]?.ToObject<int>() ?? 0)}");
                    text.AppendLine($"  Humidity: {values["humidity"]}%, Wind: {values["windSpeed"]} from {values["windDirection"]}°");
                    if (values["precipitationProbability"] != null)
                        text.AppendLine($"  Precipitation: {values["precipitationProbability"]}% chance");
                    text.AppendLine();
                }
            }
        }

        return CreateTextToolResponse(text.ToString());
    }
    
    private JObject FormatRealtimeWeatherResponse(JObject response, string location)
    {
        var data = response["data"];
        var locationInfo = response["location"];
        var values = data?["values"];
        var time = DateTime.Parse(data?["time"]?.ToString() ?? DateTime.UtcNow.ToString());
        
        var text = $"Current Weather for {location}:\n" +
                   $"Location: {locationInfo?["name"]} ({locationInfo?["lat"]}, {locationInfo?["lon"]})\n" +
                   $"Time: {time:MMM dd, yyyy h:mm tt}\n\n" +
                   $"Temperature: {values?["temperature"]}°\n" +
                   $"Feels like: {values?["temperatureApparent"]}°\n" +
                   $"Conditions: {GetWeatherDescription(values?["weatherCode"]?.ToObject<int>() ?? 0)}\n" +
                   $"Humidity: {values?["humidity"]}%\n" +
                   $"Wind: {values?["windSpeed"]} from {values?["windDirection"]}°\n" +
                   $"Pressure: {values?["pressureSeaLevel"]} hPa\n" +
                   $"Visibility: {values?["visibility"]} km\n" +
                   $"UV Index: {values?["uvIndex"]}";

        return CreateTextToolResponse(text);
    }
    
    private JObject FormatWeatherAlertsResponse(JObject response, string location)
    {
        var alerts = response["alerts"] as JArray;
        var text = new StringBuilder();
        
        text.AppendLine($"Weather Alerts for {location}:");
        
        if (alerts == null || alerts.Count == 0)
        {
            text.AppendLine("No active weather alerts");
        }
        else
        {
            text.AppendLine($"{alerts.Count} active alert(s):");
            text.AppendLine();
            
            foreach (var alert in alerts.Take(10))
            {
                var severity = alert["severity"]?.ToString() ?? "Unknown";
                var eventType = alert["event"]?.ToString() ?? "Weather Alert";
                var description = alert["description"]?.ToString() ?? "No description available";
                var startTime = alert["startTime"]?.ToString();
                var endTime = alert["endTime"]?.ToString();
                
                text.AppendLine($"🚨 {eventType} - {severity}");
                if (!string.IsNullOrEmpty(startTime))
                {
                    var start = DateTime.Parse(startTime);
                    text.AppendLine($"   Start: {start:MMM dd, h:mm tt}");
                }
                if (!string.IsNullOrEmpty(endTime))
                {
                    var end = DateTime.Parse(endTime);
                    text.AppendLine($"   End: {end:MMM dd, h:mm tt}");
                }
                text.AppendLine($"   {description}");
                text.AppendLine();
            }
        }

        return CreateTextToolResponse(text.ToString());
    }
    
    private JObject FormatHistoricalWeatherResponse(JObject response, string location, string startDate, string endDate)
    {
        var timelines = response["timelines"];
        var locationInfo = response["location"];
        var text = new StringBuilder();
        
        text.AppendLine($"Historical Weather for {location}:");
        text.AppendLine($"Location: {locationInfo?["name"]} ({locationInfo?["lat"]}, {locationInfo?["lon"]})");
        if (!string.IsNullOrEmpty(startDate) && !string.IsNullOrEmpty(endDate))
            text.AppendLine($"Period: {startDate} to {endDate}");
        text.AppendLine();

        var daily = timelines?["daily"] as JArray;
        if (daily != null && daily.Count > 0)
        {
            text.AppendLine("== Historical Data ==");
            
            foreach (var day in daily.Take(30))
            {
                var time = DateTime.Parse(day["time"].ToString());
                var values = day["values"];
                
                text.AppendLine($"{time:MMM dd, yyyy}:");
                text.AppendLine($"  Temperature: {values["temperatureMax"]}° / {values["temperatureMin"]}°");
                text.AppendLine($"  Precipitation: {values["precipitationSum"]} mm");
                text.AppendLine($"  Wind: {values["windSpeedMax"]} max, {values["windSpeedAvg"]} avg");
                text.AppendLine();
            }
        }

        return CreateTextToolResponse(text.ToString());
    }
    
    private JObject FormatAirQualityResponse(JObject response, string location, bool forecast)
    {
        var data = response["data"];
        var locationInfo = response["location"];
        var text = new StringBuilder();
        
        text.AppendLine($"Air Quality {(forecast ? "Forecast" : "Current")} for {location}:");
        text.AppendLine($"Location: {locationInfo?["name"]} ({locationInfo?["lat"]}, {locationInfo?["lon"]})");
        text.AppendLine();

        if (forecast)
        {
            var hourly = data?["hourly"] as JArray;
            if (hourly != null && hourly.Count > 0)
            {
                text.AppendLine("== Air Quality Forecast ==");
                foreach (var hour in hourly.Take(24))
                {
                    var time = DateTime.Parse(hour["time"].ToString());
                    var values = hour["values"];
                    
                    text.AppendLine($"{time:MMM dd, h:mm tt}:");
                    text.AppendLine($"  PM2.5: {values["pm25"]} µg/m³");
                    text.AppendLine($"  PM10: {values["pm10"]} µg/m³");
                    text.AppendLine($"  Ozone: {values["o3"]} µg/m³");
                    text.AppendLine($"  NO2: {values["no2"]} µg/m³");
                    text.AppendLine($"  CO: {values["co"]} mg/m³");
                    text.AppendLine();
                }
            }
        }
        else
        {
            var values = data?["values"];
            var time = DateTime.Parse(data?["time"]?.ToString() ?? DateTime.UtcNow.ToString());
            
            text.AppendLine($"Current Air Quality ({time:MMM dd, h:mm tt}):");
            text.AppendLine($"PM2.5: {values?["pm25"]} µg/m³");
            text.AppendLine($"PM10: {values?["pm10"]} µg/m³");
            text.AppendLine($"Ozone: {values?["o3"]} µg/m³");
            text.AppendLine($"NO2: {values?["no2"]} µg/m³");
            text.AppendLine($"CO: {values?["co"]} mg/m³");
        }

        return CreateTextToolResponse(text.ToString());
    }
    
    private JObject FormatMarineWeatherResponse(JObject response, string location)
    {
        var timelines = response["timelines"];
        var locationInfo = response["location"];
        var text = new StringBuilder();
        
        text.AppendLine($"Marine Weather for {location}:");
        text.AppendLine($"Location: {locationInfo?["name"]} ({locationInfo?["lat"]}, {locationInfo?["lon"]})");
        text.AppendLine();

        var hourly = timelines?["hourly"] as JArray;
        if (hourly != null && hourly.Count > 0)
        {
            text.AppendLine("== Marine Forecast ==");
            
            foreach (var hour in hourly.Take(12))
            {
                var time = DateTime.Parse(hour["time"].ToString());
                var values = hour["values"];
                
                text.AppendLine($"{time:MMM dd, h:mm tt}:");
                text.AppendLine($"  Wave Height: {values["waveHeight"]} m");
                text.AppendLine($"  Wave Period: {values["wavePeriod"]} s");
                text.AppendLine($"  Wave Direction: {values["waveDirection"]}°");
                text.AppendLine($"  Water Temperature: {values["waterTemperature"]}°");
                text.AppendLine($"  Wind: {values["windSpeed"]} from {values["windDirection"]}°");
                text.AppendLine();
            }
        }

        return CreateTextToolResponse(text.ToString());
    }
    
    private JObject FormatAviationWeatherResponse(JObject response, string location)
    {
        var timelines = response["timelines"];
        var locationInfo = response["location"];
        var text = new StringBuilder();
        
        text.AppendLine($"Aviation Weather for {location}:");
        text.AppendLine($"Location: {locationInfo?["name"]} ({locationInfo?["lat"]}, {locationInfo?["lon"]})");
        text.AppendLine();

        var hourly = timelines?["hourly"] as JArray;
        if (hourly != null && hourly.Count > 0)
        {
            text.AppendLine("== Aviation Forecast ==");
            
            foreach (var hour in hourly.Take(12))
            {
                var time = DateTime.Parse(hour["time"].ToString());
                var values = hour["values"];
                
                text.AppendLine($"{time:MMM dd, h:mm tt}:");
                text.AppendLine($"  Visibility: {values["visibility"]} km");
                text.AppendLine($"  Cloud Base: {values["cloudBase"]} km");
                text.AppendLine($"  Cloud Ceiling: {values["cloudCeiling"]} km");
                text.AppendLine($"  Turbulence: {values["turbulence"]}");
                text.AppendLine($"  Icing: {values["icing"]}");
                text.AppendLine($"  Wind: {values["windSpeed"]} from {values["windDirection"]}°");
                text.AppendLine();
            }
        }

        return CreateTextToolResponse(text.ToString());
    }
    
    private JObject FormatClimateNormalsResponse(JObject response, string location)
    {
        var data = response["data"];
        var locationInfo = response["location"];
        var text = new StringBuilder();
        
        text.AppendLine($"Climate Normals for {location}:");
        text.AppendLine($"Location: {locationInfo?["name"]} ({locationInfo?["lat"]}, {locationInfo?["lon"]})");
        text.AppendLine();

        if (data != null)
        {
            text.AppendLine("== 20-Year Climate Statistics ==");
            text.AppendLine($"Average Temperature: {data["temperatureAvg"]}°");
            text.AppendLine($"Temperature Range: {data["temperatureMin"]}° to {data["temperatureMax"]}°");
            text.AppendLine($"Average Precipitation: {data["precipitationAvg"]} mm");
            text.AppendLine($"Average Humidity: {data["humidityAvg"]}%");
            text.AppendLine($"Average Wind Speed: {data["windSpeedAvg"]}");
        }

        return CreateTextToolResponse(text.ToString());
    }
    
    private JObject FormatRouteWeatherResponse(JObject response, string origin, string destination)
    {
        var route = response["route"];
        var text = new StringBuilder();
        
        text.AppendLine($"Route Weather from {origin} to {destination}:");
        text.AppendLine();

        var waypoints = route?["waypoints"] as JArray;
        if (waypoints != null && waypoints.Count > 0)
        {
            text.AppendLine("== Route Weather Conditions ==");
            
            foreach (var waypoint in waypoints)
            {
                var location = waypoint["location"];
                var weather = waypoint["weather"]["values"];
                var distance = waypoint["distance"];
                
                text.AppendLine($"Distance: {distance} km");
                text.AppendLine($"Location: {location["lat"]}, {location["lon"]}");
                text.AppendLine($"  Temperature: {weather["temperature"]}°");
                text.AppendLine($"  Conditions: {GetWeatherDescription(weather["weatherCode"]?.ToObject<int>() ?? 0)}");
                text.AppendLine($"  Precipitation: {weather["precipitationProbability"]}% chance");
                text.AppendLine($"  Wind: {weather["windSpeed"]} from {weather["windDirection"]}°");
                text.AppendLine();
            }
        }

        return CreateTextToolResponse(text.ToString());
    }

    // Helper methods
    
    private string GetTimelineKey(string timestep)
    {
        return timestep switch
        {
            "1m" => "minutely",
            "1h" => "hourly",
            "1d" => "daily",
            _ => "hourly"
        };
    }
    
    private string GetTimelineLabel(string timestep)
    {
        return timestep switch
        {
            "1m" => "Minutely",
            "1h" => "Hourly",
            "1d" => "Daily",
            _ => "Hourly"
        };
    }
    
    private string GetWeatherDescription(int weatherCode)
    {
        return weatherCode switch
        {
            1000 => "Clear",
            1001 => "Cloudy",
            1100 => "Mostly Clear",
            1101 => "Partly Cloudy",
            1102 => "Mostly Cloudy",
            2000 => "Fog",
            2100 => "Light Fog",
            4000 => "Drizzle",
            4001 => "Rain",
            4200 => "Light Rain",
            4201 => "Heavy Rain",
            5000 => "Snow",
            5001 => "Flurries",
            5100 => "Light Snow",
            5101 => "Heavy Snow",
            6000 => "Freezing Drizzle",
            6001 => "Freezing Rain",
            6200 => "Light Freezing Rain",
            6201 => "Heavy Freezing Rain",
            7000 => "Ice Pellets",
            7101 => "Heavy Ice Pellets",
            7102 => "Light Ice Pellets",
            8000 => "Thunderstorm",
            _ => "Unknown"
        };
    }

    // Helper method to make calls to Tomorrow.io API using connector's HTTP client
    private async Task<JObject> MakeTomorrowIOApiCall(string endpoint)
    {
        try
        {
            var url = $"https://api.tomorrow.io/v4{endpoint}";
            
            // Add API key parameter if not already present
            var separator = endpoint.Contains("?") ? "&" : "?";
            
            // Extract API key from query string (Power Platform passes it as query parameter)
            string apiKey = null;
            
            var query = this.Context.Request.RequestUri.Query;
            if (!string.IsNullOrEmpty(query))
            {
                var queryParams = query.TrimStart('?').Split('&');
                foreach (var param in queryParams)
                {
                    var keyValue = param.Split('=');
                    if (keyValue.Length == 2 && keyValue[0].Equals("apikey", StringComparison.OrdinalIgnoreCase))
                    {
                        apiKey = Uri.UnescapeDataString(keyValue[1]);
                        break;
                    }
                }
            }
            
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("API key not found in headers or query string. Please ensure the apikey parameter is provided.");
            }
            
            if (!endpoint.Contains("apikey="))
            {
                url += $"{separator}apikey={apiKey}";
            }
            
            // Create HTTP request message
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            
            // Set required headers for Tomorrow.io API
            request.Headers.Add("Accept", "application/json");
            
            // Use the connector's HTTP client
            var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            
            if (response.IsSuccessStatusCode)
            {
                return JObject.Parse(content);
            }
            else
            {
                throw new HttpRequestException($"Tomorrow.io API Error ({response.StatusCode}): {content}");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse Tomorrow.io API response: {ex.Message}");
        }
        catch (HttpRequestException)
        {
            throw; // Re-throw HTTP exceptions as-is
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Tomorrow.io API call failed: {ex.Message}");
        }
    }

    // ****** DO NOT MODIFY BELOW THIS LINE ******
    // Server capabilities - MCP protocol configuration
    // The code below this point implements the MCP protocol specification
    // and should not be changed unless you understand the JSON-RPC 2.0 and MCP standards.
    // Only modify AVAILABLE_TOOLS above and add tool logic to HandleToolsCallAsync() method above.
    private static readonly string PROTOCOL_VERSION = "2025-06-18";
    private static readonly JObject SERVER_CAPABILITIES = new JObject
    {
        ["tools"] = new JObject
        {
            ["listChanged"] = true
        }
    };
    
    // Dynamically get tool names from AVAILABLE_TOOLS
    private static string[] GetToolNames()
    {
        return AVAILABLE_TOOLS.Select(tool => tool["name"]?.ToString()).Where(name => !string.IsNullOrEmpty(name)).ToArray();
    }
    
    // Convert tool name to method name (e.g., "hello_world" -> "HelloWorld")
    private static string ConvertToMethodName(string toolName)
    {
        if (string.IsNullOrEmpty(toolName)) return "";
        
        var parts = toolName.Split('_');
        var result = new StringBuilder();
        
        foreach (var part in parts)
        {
            if (!string.IsNullOrEmpty(part))
            {
                result.Append(char.ToUpper(part[0]));
                if (part.Length > 1)
                {
                    result.Append(part.Substring(1).ToLower());
                }
            }
        }
        
        return result.ToString();
    }
    
    // Tool response helpers - create properly formatted MCP tool results
    private static JObject CreateTextToolResponse(string text)
    {
        return new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            }
        };
    }
    
    private static JObject CreateImageToolResponse(string data, string mimeType)
    {
        return new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "image",
                    ["data"] = data,
                    ["mimeType"] = mimeType
                }
            }
        };
    }
    
    private static JObject CreateResourceToolResponse(string uri, string name = null, string mimeType = null)
    {
        var resourceObj = new JObject
        {
            ["type"] = "resource",
            ["resource"] = new JObject
            {
                ["uri"] = uri
            }
        };
        
        if (!string.IsNullOrEmpty(name))
            resourceObj["resource"]["name"] = name;
        if (!string.IsNullOrEmpty(mimeType))
            resourceObj["resource"]["mimeType"] = mimeType;
        
        return new JObject
        {
            ["content"] = new JArray { resourceObj }
        };
    }
    
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            var operationId = GetOperationId();
            
            // Handle specific MCP operations first
            if (operationId == "InvokeServer" || operationId == "GetInvokeServer")
            {
                if (operationId == "InvokeServer")
                {
                    return await HandleMcpRequestAsync().ConfigureAwait(false);
                }
                else
                {
                    return await HandleGetProtocolSchemaAsync().ConfigureAwait(false);
                }
            }
            
            // For all other operations, check if this is an MCP request by examining the request body
            var requestBody = await ParseRequestBodyAsync().ConfigureAwait(false);
            
            // If the request contains MCP protocol markers, handle as MCP request
            if (IsMcpRequest(requestBody))
            {
                return await HandleMcpRequestDirectly(requestBody).ConfigureAwait(false);
            }
            
            // For regular API operations, forward the request using SendAsync
            // This preserves the original request and lets the connector handle it normally
            return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(null, -32603, "Internal error", ex.Message);
        }
    }
    
    // Helper method to detect if a request is an MCP request
    private bool IsMcpRequest(JObject requestBody)
    {
        if (requestBody == null) return false;
        
        // Check for JSON-RPC 2.0 protocol markers - this is the primary indicator
        if (requestBody["jsonrpc"]?.ToString() == "2.0") return true;
        
        // Check for MCP method names only if we have a method field
        var method = requestBody["method"]?.ToString();
        if (!string.IsNullOrEmpty(method))
        {
            return method.StartsWith("initialize") || 
                   method.StartsWith("notifications/") || 
                   method.StartsWith("tools/") ||
                   method.StartsWith("resources/") ||
                   method.StartsWith("prompts/");
        }
        
        // Check if the request body is empty AND we have an MCP operation ID
        // Only consider empty body as MCP if we're already in an MCP context
        var operationId = GetOperationId();
        if (requestBody.Count == 0 && (operationId == "InvokeServer" || operationId == "GetInvokeServer")) 
        {
            return true;
        }
        
        return false;
    }
    
    // Handle MCP requests directly with the parsed request body
    private async Task<HttpResponseMessage> HandleMcpRequestDirectly(JObject requestBody)
    {
        // Handle the case where the body is just {} (for notifications/initialized)
        if (requestBody.Count == 0 || string.IsNullOrEmpty(GetStringProperty(requestBody, "method", "")))
        {
            return await HandleInitializedAsync().ConfigureAwait(false);
        }
        
        var method = GetStringProperty(requestBody, "method", "");
        var requestId = GetRequestId(requestBody);
        
        switch (method)
        {
            case "initialize":
                return await HandleInitializeAsync(requestBody, requestId).ConfigureAwait(false);
            case "notifications/initialized":
                return await HandleInitializedAsync().ConfigureAwait(false);
            case "tools/list":
                return await HandleToolsListAsync(requestId).ConfigureAwait(false);
            case "tools/call":
                return await HandleToolsCallAsync(requestBody, requestId).ConfigureAwait(false);
            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", $"Unknown method '{method}'");
        }
    }
    
    private async Task<HttpResponseMessage> HandleMcpRequestAsync()
    {
        var requestBody = await ParseRequestBodyAsync().ConfigureAwait(false);
        
        // Handle the case where the body is just {} (for notifications/initialized)
        // This happens when the client sends an empty object as the initialized notification
        if (requestBody.Count == 0 || string.IsNullOrEmpty(GetStringProperty(requestBody, "method", "")))
        {
            // This is the notifications/initialized message
            return await HandleInitializedAsync().ConfigureAwait(false);
        }
        
        var method = GetStringProperty(requestBody, "method", "");
        var requestId = GetRequestId(requestBody);
        
        switch (method)
        {
            case "initialize":
                return await HandleInitializeAsync(requestBody, requestId).ConfigureAwait(false);
            case "notifications/initialized":
                return await HandleInitializedAsync().ConfigureAwait(false);
            case "tools/list":
                return await HandleToolsListAsync(requestId).ConfigureAwait(false);
            case "tools/call":
                return await HandleToolsCallAsync(requestBody, requestId).ConfigureAwait(false);
            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", $"Unknown method '{method}'");
        }
    }
    
    private async Task<HttpResponseMessage> HandleInitializeAsync(JObject requestBody, object requestId)
    {
        try
        {
            var paramsObj = requestBody["params"] as JObject;
            var clientVersion = GetStringProperty(paramsObj, "protocolVersion", "");
            
            if (string.IsNullOrEmpty(clientVersion))
            {
                return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "protocolVersion is required");
            }
            
            var initializeResult = new JObject
            {
                ["protocolVersion"] = PROTOCOL_VERSION,
                ["capabilities"] = SERVER_CAPABILITIES,
                ["serverInfo"] = new JObject
                {
                    ["name"] = SERVER_NAME,
                    ["version"] = SERVER_VERSION
                },
                ["instructions"] = "This is a Model Context Protocol server for Tomorrow.io weather API. It provides comprehensive weather data including forecasts, real-time conditions, alerts, air quality, marine weather, aviation weather, and climate data."
            };
            
            return CreateJsonRpcSuccessResponse(requestId, initializeResult);
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }
    
    private async Task<HttpResponseMessage> HandleInitializedAsync()
    {
        // Power Platform hosted MCP servers are stateless - just return confirmation
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var confirmationResponse = new JObject
        {
            ["status"] = "initialized",
            ["message"] = "MCP server initialization complete - ready to handle tool requests",
            ["serverName"] = SERVER_NAME,
            ["serverVersion"] = SERVER_VERSION,
            ["protocolVersion"] = PROTOCOL_VERSION,
            ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ["capabilities"] = new JObject
            {
                ["tools"] = new JArray(GetToolNames())
            }
        };
        
        response.Content = CreateJsonContent(confirmationResponse.ToString());
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return response;
    }
    
    private async Task<HttpResponseMessage> HandleToolsListAsync(object requestId)
    {
        // Power Platform hosted MCP servers are stateless - no initialization check needed
        try
        {
            var result = new JObject
            {
                ["tools"] = AVAILABLE_TOOLS
            };
            
            return CreateJsonRpcSuccessResponse(requestId, result);
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }
    
    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject requestBody, object requestId)
    {
        // Power Platform hosted MCP servers are stateless - no initialization check needed
        try
        {
            var paramsObj = requestBody["params"] as JObject;
            if (paramsObj == null)
            {
                return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "params object is required");
            }
            
            var toolName = GetStringProperty(paramsObj, "name", "");
            if (string.IsNullOrEmpty(toolName))
            {
                return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "tool name is required");
            }
            
            // Dynamically route tool calls to their implementations
            // Converts tool name to method name: "get_weather_forecast" -> "ExecuteGetWeatherForecastTool"
            var methodName = "Execute" + ConvertToMethodName(toolName) + "Tool";
            var method = this.GetType().GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (method == null)
            {
                return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", $"Unknown tool: {toolName}");
            }
            
            var arguments = paramsObj["arguments"] as JObject ?? new JObject();
            
            // Check if method is async
            if (method.ReturnType == typeof(Task<JObject>))
            {
                var task = method.Invoke(this, new object[] { arguments }) as Task<JObject>;
                var result = await task;
                return CreateJsonRpcSuccessResponse(requestId, result);
            }
            else
            {
                var result = method.Invoke(this, new object[] { arguments }) as JObject;
                return CreateJsonRpcSuccessResponse(requestId, result);
            }
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }
    
    private async Task<HttpResponseMessage> HandleGetProtocolSchemaAsync()
    {
        try
        {
            // Return the MCP protocol schema information
            var schema = new JObject
            {
                ["protocol"] = "mcp",
                ["version"] = PROTOCOL_VERSION,
                ["serverInfo"] = new JObject
                {
                    ["name"] = SERVER_NAME,
                    ["version"] = SERVER_VERSION,
                    ["description"] = "Model Context Protocol server for Tomorrow.io weather API - provides comprehensive weather data including forecasts, real-time conditions, alerts, air quality, marine weather, aviation weather, and climate data"
                },
                ["capabilities"] = SERVER_CAPABILITIES,
                ["methods"] = new JArray
                {
                    "initialize",
                    "notifications/initialized", 
                    "tools/list",
                    "tools/call"
                },
                ["tools"] = AVAILABLE_TOOLS
            };
            
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = CreateJsonContent(schema.ToString());
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return response;
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(null, -32603, "Internal error", ex.Message);
        }
    }
    
    private string GetOperationId()
    {
        string operationId = this.Context.OperationId;
        
        // For MCP agentic protocol, the operation ID should be directly available
        if (string.IsNullOrEmpty(operationId))
        {
            return "InvokeServer"; // Default for MCP protocol
        }
        
        // Only try Base64 decoding if it looks like Base64 and isn't already "InvokeServer"
        if (operationId != "InvokeServer" && IsBase64String(operationId))
        {
            try 
            {
                byte[] data = Convert.FromBase64String(operationId);
                operationId = System.Text.Encoding.UTF8.GetString(data);
            }
            catch (FormatException) 
            {
                // If Base64 decoding fails, use the original value
            }
        }
        
        return operationId;
    }
    
    private bool IsBase64String(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        
        // Basic check for Base64 format
        return s.Length % 4 == 0 && 
               System.Text.RegularExpressions.Regex.IsMatch(s, @"^[a-zA-Z0-9\+/]*={0,3}$", System.Text.RegularExpressions.RegexOptions.None);
    }
    
    private static JObject _cachedRequestBody = null;
    
    private async Task<JObject> ParseRequestBodyAsync()
    {
        if (_cachedRequestBody != null)
        {
            return _cachedRequestBody;
        }
        
        try
        {
            var contentAsString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            
            // Handle empty or whitespace-only content
            if (string.IsNullOrWhiteSpace(contentAsString))
            {
                _cachedRequestBody = new JObject();
                return _cachedRequestBody;
            }
            
            _cachedRequestBody = JObject.Parse(contentAsString);
            return _cachedRequestBody;
        }
        catch (JsonException)
        {
            // If JSON parsing fails, return empty object
            _cachedRequestBody = new JObject();
            return _cachedRequestBody;
        }
    }
    
    private object GetRequestId(JObject requestBody)
    {
        var id = requestBody["id"];
        if (id == null) return null;
        if (id.Type == JTokenType.String)
            return id.ToString();
        if (id.Type == JTokenType.Integer)
            return id.ToObject<int>();
        if (id.Type == JTokenType.Float)
            return id.ToObject<double>();
            
        return id.ToString();
    }
    
    private HttpResponseMessage CreateJsonRpcSuccessResponse(object id, JObject result)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var jsonRpcResponse = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id != null ? JToken.FromObject(id) : null,
            ["result"] = result
        };
        
        response.Content = CreateJsonContent(jsonRpcResponse.ToString());
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return response;
    }
    
    private HttpResponseMessage CreateJsonRpcErrorResponse(object id, int code, string message, string data = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var errorObject = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        
        if (!string.IsNullOrEmpty(data))
        {
            errorObject["data"] = data;
        }
        
        var jsonRpcResponse = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id != null ? JToken.FromObject(id) : null,
            ["error"] = errorObject
        };
        
        response.Content = CreateJsonContent(jsonRpcResponse.ToString());
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return response;
    }
    
    private string GetStringProperty(JObject json, string propertyName, string defaultValue = "")
    {
        if (json == null) return defaultValue;
        return json[propertyName]?.ToString() ?? defaultValue;
    }
    
    private int GetIntProperty(JObject json, string propertyName, int defaultValue = 0)
    {
        if (json == null) return defaultValue;
        return json[propertyName]?.ToObject<int>() ?? defaultValue;
    }
    
    private bool GetBoolProperty(JObject json, string propertyName, bool defaultValue = false)
    {
        if (json == null) return defaultValue;
        return json[propertyName]?.ToObject<bool>() ?? defaultValue;
    }
    
    private StringContent CreateJsonContent(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}