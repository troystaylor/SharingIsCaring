# MCP Server Implementation in Power Automate

This document provides templates and examples for implementing Model Context Protocol (MCP) servers using Power Automate flows, with specific examples using weather connectors.

## Document Structure

1. **Part 1: MCP Flow Template** - Reusable template for any MCP server implementation
2. **Part 2: Tomorrow.io Weather Connector Implementation** - Specific implementation using Tomorrow.io connector
3. **Appendix: Authentication in Power Platform Connectors** - Comprehensive authentication configuration guide (see end of document)

---

# Part 1: MCP Flow Template

## Overview

This section provides a complete template for implementing an MCP (Model Context Protocol) server using Power Automate. This template can be adapted for any connector or API service.

**🎯 Copilot Studio Compatibility**: This template focuses on the **tools** capability, which is currently the primary MCP feature supported by Microsoft Copilot Studio. Additional capabilities like resources and prompts are included for future compatibility.

## MCP Protocol Basics

The Model Context Protocol (MCP) is a JSON-RPC 2.0 based protocol that enables standardized tool discovery and execution. Key methods include:

**Core Lifecycle Methods:**
- `initialize` - Establish server capabilities and protocol version negotiation
- `notifications/initialized` - Acknowledge initialization completion

**Tool Management:**
- `tools/list` - Discover available tools
- `tools/call` - Execute specific tools

**Utility Methods:**
- `ping` - Verify connection health and responsiveness
- `notifications/cancelled` - Cancel in-progress requests
- `notifications/progress` - Send progress updates for long-running operations

**Logging (Server-to-Client):**
- `logging/setLevel` - Configure minimum log level
- `notifications/message` - Send structured log messages

**Additional Capabilities (Implementation Dependent):**
- `resources/list` - List available resources (if server supports resources) ⚠️ **Future**: Not yet supported in Copilot Studio
- `resources/read` - Read resource content ⚠️ **Future**: Not yet supported in Copilot Studio
- `prompts/list` - List available prompt templates (if server supports prompts) ⚠️ **Future**: Not yet supported in Copilot Studio
- `prompts/get` - Get specific prompt template ⚠️ **Future**: Not yet supported in Copilot Studio

**📌 Current Copilot Studio Limitation**: As of September 2025, Microsoft Copilot Studio only supports the **tools** capability from MCP servers. Resources and prompts are not yet supported but may be added in future updates.

## MCP Protocol Flow Diagram

```mermaid
sequenceDiagram
    participant C as Copilot Studio
    participant PA as Power Automate MCP Server
    participant API as External API/Connector
    
    Note over C,API: MCP Initialization Phase
    C->>PA: initialize request
    PA->>C: capabilities & server info
    C->>PA: notifications/initialized
    PA->>C: acknowledgment
    
    Note over C,API: Tool Discovery Phase
    C->>PA: tools/list
    PA->>C: available tools array
    
    Note over C,API: Tool Execution Phase
    C->>PA: tools/call (with arguments)
    PA->>API: connector operation
    API->>PA: data response
    PA->>C: formatted tool result
    
    Note over C,API: Health Check (Optional)
    C->>PA: ping
    PA->>C: empty response
    
    Note over C,API: Error Handling
    C->>PA: invalid method/tool
    PA->>C: JSON-RPC error response
```

## Power Automate Flow Architecture

```mermaid
flowchart TD
    A[HTTP Request Trigger<br/>JSON-RPC Schema] --> B{Method Switch}
    
    B -->|initialize| C[Return Capabilities<br/>& Server Info]
    B -->|notifications/initialized| D[Send Acknowledgment]
    B -->|ping| E[Return Empty Response]
    B -->|logging/setLevel| F[Configure Logging]
    B -->|tools/list| G[Return Tools Array]
    B -->|tools/call| H{Tool Switch}
    B -->|unknown method| I[Method Not Found Error]
    
    H -->|known tool| J[Parse Arguments]
    H -->|unknown tool| K[Tool Not Found Error]
    
    J --> L[Call Connector]
    L --> M[Format Response]
    M --> N[Return Tool Result]
    
    style A fill:#e1f5fe
    style B fill:#fff3e0
    style H fill:#fff3e0
    style I fill:#ffebee
    style K fill:#ffebee
    style N fill:#e8f5e8
```

## Copilot Studio Compatibility

### Current Support (September 2025)
✅ **Fully Supported:**
- `initialize` - Server initialization and capability negotiation
- `notifications/initialized` - Initialization acknowledgment  
- `tools/list` - Tool discovery
- `tools/call` - Tool execution
- `ping` - Connection health checks
- Basic error handling and JSON-RPC 2.0 compliance

✅ **Logging Support:**
- `logging/setLevel` - Configure log levels
- `notifications/message` - Receive server log messages

### Future Capabilities (Not Yet Supported)
🔄 **Planned for Future Updates:**
- `resources/list` - List available resources
- `resources/read` - Read resource content
- `resources/subscribe` - Subscribe to resource changes
- `prompts/list` - List prompt templates
- `prompts/get` - Get specific prompts
- `notifications/cancelled` - Request cancellation
- `notifications/progress` - Progress tracking

```mermaid
gantt
    title MCP Capability Support Timeline
    dateFormat YYYY-MM-DD
    section Current Support
    Tools Capability           :done, tools, 2024-01-01, 2025-09-04
    Logging Capability          :done, logging, 2024-06-01, 2025-09-04
    section Future Roadmap
    Resources Capability        :active, resources, 2025-09-04, 2026-03-01
    Prompts Capability          :active, prompts, 2025-09-04, 2026-03-01
    Progress Notifications      :active, progress, 2025-09-04, 2026-06-01
    Cancellation Support        :active, cancel, 2025-09-04, 2026-06-01
```

### Implementation Strategy
**Current Recommendation**: Implement only the supported capabilities for immediate Copilot Studio integration, but structure your code to easily add future capabilities when they become available.

## MCP Flow Structure

## Flow Components

### JSON-RPC Request Structure

The MCP protocol uses JSON-RPC 2.0 for all communication. Here's the message structure:

```mermaid
graph TD
    A[JSON-RPC Request] --> B[jsonrpc: '2.0']
    A --> C[method: string]
    A --> D[id: string/number]
    A --> E[params: object]
    
    E --> F[MCP Method Parameters]
    
    F -->|initialize| G[clientInfo, capabilities]
    F -->|tools/call| H[name, arguments]
    F -->|logging/setLevel| I[level: debug/info/warning/error]
    
    style A fill:#e3f2fd
    style B fill:#fff8e1
    style C fill:#fff8e1
    style D fill:#fff8e1
    style E fill:#fff8e1
    style F fill:#f3e5f5
    style G fill:#e8f5e8
    style H fill:#e8f5e8
    style I fill:#e8f5e8
```

### 1. HTTP Request Trigger Template

**Description**: HTTP Request trigger that accepts JSON-RPC 2.0 requests for MCP protocol communication.

**⚠️ Important**: You must create your own flow and configure your own connections. Replace all placeholder values with your actual connector details.

**Trigger Configuration**:
```json
{
  "trigger": {
    "nodeId": "manual",
    "type": "Request",
    "settings": {
      "triggerAuthenticationType": "All",
      "schema": {
        "type": "object",
        "properties": {
          "jsonrpc": {
            "type": "string"
          },
          "id": {
            "type": "string"
          },
          "method": {
            "type": "string"
          },
          "params": {
            "type": "object",
            "properties": {
              "capabilities": {
                "type": "object",
                "properties": {}
              },
              "clientInfo": {
                "type": "object",
                "properties": {}
              },
              "protocolVersion": {
                "type": "string"
              },
              "sessionContext": {
                "type": "object",
                "properties": {}
              }
            }
          }
        }
      }
    }
  }
}
```

### 2. MCP Method Switch Template

**Description**: Routes JSON-RPC requests to appropriate handlers based on method name.

**⚠️ Important**: Customize the server info, tool definitions, and connector actions for your specific use case.

**Switch Configuration Template**:
```json
{
  "nodeId": "Switch",
  "serializedValue": {
    "type": "Switch",
    "expression": "@triggerBody()['method']",
    "default": {
      "actions": {
        "Response:_Method_Not_Found": {
          "type": "Response",
          "kind": "Http",
          "inputs": {
            "statusCode": 200,
            "headers": {
              "Content-Type": "application/json"
            },
            "body": {
              "jsonrpc": "2.0",
              "id": "@if(equals(triggerBody()?['id'], null), null, triggerBody()?['id'])",
              "error": {
                "code": -32601,
                "message": "Method not found",
                "data": {
                  "method": "@{triggerBody()?['method']}",
                  "supportedMethods": [
                    "initialize",
                    "notifications/initialized", 
                    "ping",
                    "logging/setLevel",
                    "notifications/cancelled",
                    "tools/list",
                    "tools/call"
                  ]
                }
              }
            }
          }
        }
      }
    },
    "cases": {
      "Case:_Initialize": {
        "actions": {
          "Response:_Initialize": {
            "type": "Response",
            "kind": "Http",
            "inputs": {
              "statusCode": 200,
              "headers": {
                "Content-Type": "application/json"
              },
              "body": {
                "jsonrpc": "2.0",
                "id": "@triggerBody()?['id']",
                "result": {
                  "protocolVersion": "@{triggerBody()?['params']?['protocolVersion']}",
                  "capabilities": {
                    "logging": {},
                    "tools": {
                      "listChanged": false
                    }
                  },
                  "serverInfo": {
                    "name": "[YOUR_SERVER_NAME]",
                    "title": "[YOUR_SERVER_TITLE]",
                    "version": "1.0.0"
                  }
                }
              }
            }
          }
        },
        "case": "initialize"
      },
      "Case:_Notifications_Initialized": {
        "actions": {
          "Response:_Notifications_Initialized": {
            "type": "Response",
            "kind": "Http",
            "inputs": {
              "statusCode": 200,
              "headers": {
                "Content-Type": "application/json"
              },
              "body": {}
            }
          }
        },
        "case": "notifications/initialized"
      },
      "Case:_Ping": {
        "actions": {
          "Response:_Ping": {
            "type": "Response",
            "kind": "Http",
            "inputs": {
              "statusCode": 200,
              "headers": {
                "Content-Type": "application/json"
              },
              "body": {
                "jsonrpc": "2.0",
                "id": "@triggerBody()?['id']",
                "result": {}
              }
            }
          }
        },
        "case": "ping"
      },
      "Case:_Logging_SetLevel": {
        "actions": {
          "Response:_Logging_SetLevel": {
            "type": "Response",
            "kind": "Http",
            "inputs": {
              "statusCode": 200,
              "headers": {
                "Content-Type": "application/json"
              },
              "body": {
                "jsonrpc": "2.0",
                "id": "@triggerBody()?['id']",
                "result": {}
              }
            }
          }
        },
        "case": "logging/setLevel"
      },
      "Case:_Notifications_Cancelled": {
        "actions": {
          "Response:_Notifications_Cancelled": {
            "type": "Response",
            "kind": "Http",
            "inputs": {
              "statusCode": 200,
              "headers": {
                "Content-Type": "application/json"
              },
              "body": {}
            }
          }
        },
        "case": "notifications/cancelled"
      },
      "Case:_Tools_List": {
        "actions": {
          "Response:_Tools_List": {
            "type": "Response",
            "kind": "Http",
            "inputs": {
              "statusCode": 200,
              "headers": {
                "Content-Type": "application/json"
              },
              "body": {
                "jsonrpc": "2.0",
                "id": "@int(triggerBody()?['id'])",
                "result": {
                  "tools": [
                    {
                      "name": "[YOUR_TOOL_NAME]",
                      "description": "[YOUR_TOOL_DESCRIPTION]",
                      "inputSchema": {
                        "type": "object",
                        "properties": {
                          "[PARAMETER_NAME]": {
                            "type": "string",
                            "description": "[PARAMETER_DESCRIPTION]"
                          }
                        },
                        "required": ["[PARAMETER_NAME]"]
                      }
                    }
                  ]
                }
              }
            }
          }
        },
        "case": "tools/list"
      },
      "Case:_Tools_Call": {
        "actions": {
          "Switch_Tools": {
            "type": "Switch",
            "expression": "@triggerBody()?['params']?['name']",
            "default": {
              "actions": {
                "Response:_Unknown_Tool": {
                  "type": "Response",
                  "kind": "Http",
                  "inputs": {
                    "statusCode": 200,
                    "headers": {
                      "Content-Type": "application/json"
                    },
                    "body": {
                      "jsonrpc": "2.0",
                      "id": "@if(equals(triggerBody()?['id'], null), null, int(triggerBody()?['id']))",
                      "error": {
                        "code": -32602,
                        "message": "Invalid params",
                        "data": {
                          "error": "Unknown tool",
                          "requestedTool": "@{triggerBody()?['params']?['name']}",
                          "availableTools": "[LIST_YOUR_TOOL_NAMES_HERE]",
                          "suggestion": "Use tools/list to see all available tools"
                        }
                      }
                    }
                  }
                }
              }
            },
            "cases": {
              "Case:_Your_Tool": {
                "actions": {
                  "Parse_Tool_Arguments": {
                    "type": "ParseJson",
                    "inputs": {
                      "content": "@triggerBody()?['params']?['arguments']",
                      "schema": {
                        "type": "object",
                        "properties": {
                          "[PARAMETER_NAME]": {
                            "type": "string"
                          }
                        }
                      }
                    }
                  },
                  "Call_Your_Connector": {
                    "type": "OpenApiConnection",
                    "inputs": {
                      "parameters": {
                        "[API_PARAMETER]": "@{body('Parse_Tool_Arguments')?['[PARAMETER_NAME]']}"
                      },
                      "host": {
                        "apiId": "[YOUR_CONNECTOR_API_ID]",
                        "connection": "[YOUR_CONNECTION_REFERENCE]",
                        "operationId": "[YOUR_OPERATION_ID]"
                      }
                    },
                    "runAfter": {
                      "Parse_Tool_Arguments": ["Succeeded"]
                    }
                  },
                  "Response:_Tool_Success": {
                    "type": "Response",
                    "kind": "Http",
                    "inputs": {
                      "statusCode": 200,
                      "headers": {
                        "Content-Type": "application/json"
                      },
                      "body": {
                        "jsonrpc": "2.0",
                        "id": "@int(triggerBody()?['id'])",
                        "result": {
                          "content": [
                            {
                              "type": "text",
                              "text": "@{body('Call_Your_Connector')}"
                            }
                          ],
                          "isError": false
                        }
                      }
                    },
                    "runAfter": {
                      "Call_Your_Connector": ["Succeeded"]
                    }
                  }
                },
                "case": "[YOUR_TOOL_NAME]"
              }
            }
          }
        },
        "case": "tools/call"
      }
    }
  }
}
```

### 4. Additional MCP Methods Implementation

The MCP specification includes several additional methods that can enhance your server implementation:

#### Progress Notifications (Optional)
For long-running operations, you can send progress updates:

**Progress Notification Template**:
```json
{
  "jsonrpc": "2.0",
  "method": "notifications/progress",
  "params": {
    "progressToken": "@{triggerBody()?['params']?['_meta']?['progressToken']}",
    "progress": 50,
    "total": 100,
    "message": "Processing request..."
  }
}
```

**Usage**: Include this in long-running tool operations by sending multiple HTTP responses or using a child flow.

#### Logging Support (Server-to-Client)
Implement structured logging by declaring the `logging` capability and sending log messages:

**Log Message Notification Template**:
```json
{
  "jsonrpc": "2.0",
  "method": "notifications/message", 
  "params": {
    "level": "info",
    "logger": "[YOUR_LOGGER_NAME]",
    "data": {
      "message": "Operation completed successfully",
      "details": {
        "duration": "1.2s",
        "operation": "get_weather_data"
      }
    }
  }
}
```

**Log Levels**: `debug`, `info`, `notice`, `warning`, `error`, `critical`, `alert`, `emergency`

#### Cancellation Handling
Handle request cancellations gracefully:

**Cancellation Flow**:
1. Check if cancellation was requested in long-running operations
2. Stop processing and clean up resources
3. Do not send a response for cancelled requests

**Cancellation Check Template** (in child flows):
```json
{
  "type": "If",
  "expression": "@equals(triggerBody()?['method'], 'notifications/cancelled')",
  "actions": {
    "yes": {
      "actions": {
        "Terminate": {
          "type": "Terminate",
          "inputs": {
            "runStatus": "Cancelled"
          }
        }
      }
    }
  }
}
```

#### Extended Capabilities Declaration
Update your initialize response to declare additional capabilities:

**Current Copilot Studio Compatible Capabilities**:
```json
{
  "capabilities": {
    "logging": {},
    "tools": {
      "listChanged": false
    }
  }
}
```

**Full Capabilities Example (Future-Ready)**:
```json
{
  "capabilities": {
    "logging": {},
    "tools": {
      "listChanged": false
    },
    "resources": {
      "subscribe": false,
      "listChanged": false
    },
    "prompts": {
      "listChanged": false
    },
    "experimental": {
      "[YOUR_EXPERIMENTAL_FEATURE]": {}
    }
  }
}
```

**⚠️ Important**: Currently, only declare `logging` and `tools` capabilities for Copilot Studio compatibility. The `resources` and `prompts` capabilities are included above for future-proofing your implementation.

### 5. Error Handling Best Practices

#### Error Flow Diagram

```mermaid
flowchart TD
    A[MCP Request] --> B{Valid JSON-RPC?}
    B -->|No| C[Parse Error<br/>-32700]
    B -->|Yes| D{Valid Method?}
    D -->|No| E[Method Not Found<br/>-32601]
    D -->|Yes| F{Valid Parameters?}
    F -->|No| G[Invalid Params<br/>-32602]
    F -->|Yes| H[Execute Method]
    
    H --> I{Execution Success?}
    I -->|No| J[Internal Error<br/>-32603]
    I -->|Yes| K{Tool Call?}
    
    K -->|No| L[Return Result]
    K -->|Yes| M{Valid Tool?}
    M -->|No| N[Tool Not Found<br/>Custom Error]
    M -->|Yes| O[Execute Tool]
    
    O --> P{Tool Success?}
    P -->|No| Q[Tool Error<br/>isError: true]
    P -->|Yes| R[Tool Result<br/>isError: false]
    
    style C fill:#ffcdd2
    style E fill:#ffcdd2
    style G fill:#ffcdd2
    style J fill:#ffcdd2
    style N fill:#ffcdd2
    style Q fill:#fff3e0
    style R fill:#c8e6c8
    style L fill:#c8e6c8
```

#### JSON-RPC Error Codes

| Code | Meaning | When to Use |
|------|---------|-------------|
| -32700 | Parse error | Invalid JSON received |
| -32600 | Invalid Request | Missing required JSON-RPC fields |
| -32601 | Method not found | Unknown MCP method |
| -32602 | Invalid params | Wrong parameter structure |
| -32603 | Internal error | Server execution failure |
| -32000 to -32099 | Server error | Custom application errors |

#### Implementation Guidelines

**🎯 Copilot Studio Compatibility**: Proper error responses are crucial for Copilot Studio to understand and handle failures gracefully.

**Standard JSON-RPC Error Codes**:
- `-32700`: Parse error (invalid JSON)
- `-32600`: Invalid request
- `-32601`: Method not found
- `-32602`: Invalid params
- `-32603`: Internal error
- `-32000 to -32099`: Server error (implementation defined)

**Enhanced Error Response Template for Copilot Studio**:
```json
{
  "jsonrpc": "2.0",
  "id": "@triggerBody()?['id']",
  "error": {
    "code": -32602,
    "message": "Invalid params",
    "data": {
      "details": "Parameter 'location' is required",
      "received": "@{triggerBody()?['params']}",
      "expectedFormat": {
        "location": "string (required)",
        "units": "string (optional): 'metric' or 'imperial'"
      },
      "suggestion": "Please provide a valid location parameter"
    }
  }
}
```

**Default Response Guidelines for Copilot Studio**:
1. **Always include helpful context** in the error data
2. **List available methods/tools** when appropriate
3. **Provide actionable suggestions** for fixing the error
4. **Use consistent error codes** following JSON-RPC 2.0 standards
5. **Include expected formats** for parameters when validation fails
6. **Handle missing request IDs gracefully** - use `null` if `id` is not provided

**⚠️ Important for Copilot Studio**: Always check if the request has an `id` field before including it in responses:
```json
"id": "@if(equals(triggerBody()?['id'], null), null, triggerBody()?['id'])"
```

### 6. Connection Configuration Template

**⚠️ Important**: Replace with your actual connector and connection details.

```json
{
  "allConnectionData": {
    "[ACTION_NAME]": {
      "connectionReference": {
        "api": {
          "id": "[YOUR_CONNECTOR_API_ID]"
        },
        "connection": {
          "id": "[YOUR_CONNECTION_ID]"
        },
        "connectionName": "[YOUR_CONNECTION_NAME]"
      },
      "referenceKey": "[YOUR_REFERENCE_KEY]"
    }
  }
}
```

## MCP Testing

### Test Requests Template

Replace `[YOUR_FLOW_HTTP_URL]` with your actual flow's HTTP trigger URL.

#### Core Lifecycle Tests

**Initialize Request**
```json
{
  "jsonrpc": "2.0",
  "id": "init-1",
  "method": "initialize",
  "params": {
    "protocolVersion": "2025-06-18",
    "capabilities": {},
    "clientInfo": {
      "name": "Test-Client",
      "version": "1.0"
    }
  }
}
```

**Initialized Notification**
```json
{
  "jsonrpc": "2.0",
  "method": "notifications/initialized"
}
```

#### Tool Management Tests

**List Tools Request**
```json
{
  "jsonrpc": "2.0",
  "id": "list-1",
  "method": "tools/list"
}
```

**Call Tool Request**
```json
{
  "jsonrpc": "2.0",
  "id": "call-1",
  "method": "tools/call",
  "params": {
    "name": "[YOUR_TOOL_NAME]",
    "arguments": {
      "[PARAMETER_NAME]": "[PARAMETER_VALUE]"
    }
  }
}
```

**Call Tool with Progress Token**
```json
{
  "jsonrpc": "2.0",
  "id": "call-2",
  "method": "tools/call",
  "params": {
    "_meta": {
      "progressToken": "progress-123"
    },
    "name": "[YOUR_TOOL_NAME]",
    "arguments": {
      "[PARAMETER_NAME]": "[PARAMETER_VALUE]"
    }
  }
}
```

#### Utility Method Tests

**Ping Request**
```json
{
  "jsonrpc": "2.0",
  "id": "ping-1",
  "method": "ping"
}
```

**Set Log Level Request**
```json
{
  "jsonrpc": "2.0",
  "id": "log-1",
  "method": "logging/setLevel",
  "params": {
    "level": "info"
  }
}
```

**Cancel Request Notification**
```json
{
  "jsonrpc": "2.0",
  "method": "notifications/cancelled",
  "params": {
    "requestId": "call-1",
    "reason": "User requested cancellation"
  }
}
```

#### Error Testing

**Invalid Method Test**
```json
{
  "jsonrpc": "2.0",
  "id": "error-1",
  "method": "invalid/method"
}
```

**Invalid Tool Test**
```json
{
  "jsonrpc": "2.0",
  "id": "error-2",
  "method": "tools/call",
  "params": {
    "name": "nonexistent_tool",
    "arguments": {}
  }
}
```

---

# Part 2: Tomorrow.io Weather Connector Implementation

## Overview

This section provides a specific implementation of the MCP template using the Tomorrow.io weather connector. This serves as a concrete example of how to adapt the template.

**⚠️ Important Security Notice**: 
- All connection IDs, API keys, and sensitive information have been removed or replaced with placeholders
- You must create your own Tomorrow.io connector connection
- Replace all `[PLACEHOLDER]` values with your actual configuration
- Never share actual connection IDs or API keys publicly

## Tomorrow.io Weather Tools Overview

```mermaid
mindmap
  root((Tomorrow.io MCP Tools))
    Forecast
      get_weather_forecast
        ⭐ Location + Time Range
        📊 Temperature, Precipitation, Wind
    Realtime
      get_current_weather
        📍 Current Location Data
        🌡️ Live Conditions
    Climate
      get_climate_normals
        📈 Historical Averages
        📅 30-year Climate Data
    Alerts
      get_weather_alerts
        ⚠️ Severe Weather Warnings
        🚨 Government Advisories
    Historical
      get_historical_weather
        📜 Past Weather Data
        📊 Time Series Analysis
    Air Quality
      get_air_quality
        🌬️ Pollution Levels
        💨 Health Indices
    Marine
      get_marine_weather
        🌊 Ocean Conditions
        ⛵ Coastal Forecasts
    Aviation
      get_aviation_weather
        ✈️ Flight Conditions
        🛫 Airport Weather
    Route
      get_route_weather
        🗺️ Multi-point Analysis
        🚗 Journey Planning
```

## Tool Categories and Use Cases

```mermaid
graph LR
    A[User Query] --> B{Query Type}
    
    B -->|Current Conditions| C[get_current_weather]
    B -->|Future Planning| D[get_weather_forecast]
    B -->|Safety Concerns| E[get_weather_alerts]
    B -->|Historical Analysis| F[get_historical_weather]
    B -->|Air Quality| G[get_air_quality]
    B -->|Marine Activities| H[get_marine_weather]
    B -->|Aviation| I[get_aviation_weather]
    B -->|Travel Planning| J[get_route_weather]
    B -->|Climate Research| K[get_climate_normals]
    
    C --> L[Real-time Response]
    D --> M[Forecast Response]
    E --> N[Alert Response]
    F --> O[Historical Response]
    G --> P[Air Quality Response]
    H --> Q[Marine Response]
    I --> R[Aviation Response]
    J --> S[Route Response]
    K --> T[Climate Response]
    
    style A fill:#e3f2fd
    style B fill:#fff3e0
    style L fill:#e8f5e8
    style M fill:#e8f5e8
    style N fill:#ffebee
    style O fill:#e8f5e8
    style P fill:#e1f5fe
    style Q fill:#e0f2f1
    style R fill:#fff8e1
    style S fill:#f3e5f5
    style T fill:#fce4ec
```

## Tomorrow.io Connector Overview

The Tomorrow.io connector provides comprehensive weather data through various endpoints:
- Real-time weather conditions
- Weather forecasts (hourly and daily)  
- Weather alerts and notifications
- Air quality data
- Marine and aviation weather
- Historical weather data
- Climate normals

### Tomorrow.io MCP Server Implementation

**Description**: Specific MCP server implementation using Tomorrow.io weather connector.

**⚠️ Setup Requirements**:
- Create your own Tomorrow.io connector connection
- Replace `[TOMORROW_IO_CONNECTION_ID]` with your actual connection ID
- Replace `[TOMORROW_IO_API_ID]` with your connector's API ID
- Configure your own API key in the connector settings

**Flow Structure**:
1. HTTP Request Trigger (uses template from Part 1)
2. Method Switch with Tomorrow.io specific responses
3. Tool execution using Tomorrow.io connector actions

**🎯 Copilot Studio Focus**: This implementation prioritizes the tools capability for immediate Copilot Studio compatibility, with extensible design for future resource and prompt support.

**Tomorrow.io Specific Tools List Response**:
```json
{
  "tools": [
    {
      "name": "get_weather_forecast",
      "description": "Get comprehensive weather forecast for a location",
      "inputSchema": {
        "type": "object",
        "properties": {
          "location": {"type": "string", "description": "Location coordinates or address"},
          "timesteps": {"type": "array", "description": "Forecast intervals (1h, 1d)"},
          "units": {"type": "string", "description": "metric or imperial"}
        },
        "required": ["location"]
      }
    },
    {
      "name": "get_realtime_weather", 
      "description": "Get current weather conditions",
      "inputSchema": {
        "type": "object",
        "properties": {
          "location": {"type": "string", "description": "Location coordinates or address"},
          "units": {"type": "string", "description": "metric or imperial"}
        },
        "required": ["location"]
      }
    },
    {
      "name": "get_weather_alerts",
      "description": "Get active weather alerts for a location",
      "inputSchema": {
        "type": "object", 
        "properties": {
          "location": {"type": "string", "description": "Location coordinates or address"}
        },
        "required": ["location"]
      }
    },
    {
      "name": "get_air_quality",
      "description": "Get air quality data for a location", 
      "inputSchema": {
        "type": "object",
        "properties": {
          "location": {"type": "string", "description": "Location coordinates or address"}
        },
        "required": ["location"]
      }
    }
  ]
}
```

**Example Tool Implementation** (Replace placeholders with your actual values):
```json
{
  "Case:_Get_Weather_Forecast": {
    "actions": {
      "Parse_Forecast_Arguments": {
        "type": "ParseJson",
        "inputs": {
          "content": "@triggerBody()?['params']?['arguments']",
          "schema": {
            "type": "object",
            "properties": {
              "location": {"type": "string"},
              "timesteps": {"type": "array"},
              "units": {"type": "string"}
            }
          }
        }
      },
      "Get_Tomorrow_Forecast": {
        "type": "OpenApiConnection", 
        "inputs": {
          "parameters": {
            "location": "@{body('Parse_Forecast_Arguments')?['location']}",
            "timesteps": "@{join(body('Parse_Forecast_Arguments')?['timesteps'], ',')}",
            "units": "@{body('Parse_Forecast_Arguments')?['units']}"
          },
          "host": {
            "apiId": "[TOMORROW_IO_API_ID]",
            "connection": "[TOMORROW_IO_CONNECTION_REFERENCE]", 
            "operationId": "GetWeatherForecast"
          }
        },
        "runAfter": {
          "Parse_Forecast_Arguments": ["Succeeded"]
        }
      },
      "Response:_Forecast_Success": {
        "type": "Response",
        "kind": "Http",
        "inputs": {
          "statusCode": 200,
          "headers": {"Content-Type": "application/json"},
          "body": {
            "jsonrpc": "2.0",
            "id": "@int(triggerBody()?['id'])",
            "result": {
              "content": [
                {
                  "type": "text", 
                  "text": "Weather Forecast for @{body('Parse_Forecast_Arguments')?['location']}\n\n@{body('Get_Tomorrow_Forecast')}"
                }
              ],
              "isError": false
            }
          }
        },
        "runAfter": {
          "Get_Tomorrow_Forecast": ["Succeeded"]
        }
      }
    },
    "case": "get_weather_forecast"
  }
}
```

### Tomorrow.io Connector Configuration

**⚠️ Replace with your actual connection details**:
```json
{
  "allConnectionData": {
    "Get_Tomorrow_Forecast": {
      "connectionReference": {
        "api": {
          "id": "[TOMORROW_IO_API_ID]"
        },
        "connection": {
          "id": "[YOUR_TOMORROW_IO_CONNECTION_ID]" 
        },
        "connectionName": "[YOUR_CONNECTION_NAME]"
      },
      "referenceKey": "[YOUR_REFERENCE_KEY]"
    }
  }
}
```

### Tomorrow.io MCP Testing Examples

#### Testing Workflow

```mermaid
sequenceDiagram
    participant T as Tester
    participant F as Power Automate Flow
    participant TC as Tomorrow.io Connector
    participant API as Tomorrow.io API
    
    Note over T,API: Phase 1: Initialize MCP Server
    T->>F: initialize request
    F->>T: server capabilities & info
    T->>F: notifications/initialized
    F->>T: acknowledgment
    
    Note over T,API: Phase 2: Discover Weather Tools  
    T->>F: tools/list
    F->>T: 9 weather tools available
    
    Note over T,API: Phase 3: Test Current Weather
    T->>F: tools/call get_current_weather
    F->>TC: Location: "Seattle, WA"
    TC->>API: Realtime weather request
    API->>TC: Current conditions data
    TC->>F: Formatted weather response
    F->>T: Temperature, humidity, conditions
    
    Note over T,API: Phase 4: Test Forecast
    T->>F: tools/call get_weather_forecast
    F->>TC: Location + 5-day forecast
    TC->>API: Forecast data request
    API->>TC: Forecast timeline data
    TC->>F: Formatted forecast response
    F->>T: Daily forecasts with details
    
    Note over T,API: Phase 5: Error Testing
    T->>F: tools/call invalid_tool
    F->>T: Tool not found error
    T->>F: invalid JSON-RPC
    F->>T: Parse error -32700
```

#### Test Categories

```mermaid
graph TD
    A[MCP Testing] --> B[Protocol Tests]
    A --> C[Tool Tests]
    A --> D[Error Tests]
    A --> E[Performance Tests]
    
    B --> B1[initialize]
    B --> B2[tools/list]
    B --> B3[ping]
    B --> B4[logging/setLevel]
    
    C --> C1[Current Weather]
    C --> C2[Forecast]
    C --> C3[Alerts]
    C --> C4[Air Quality]
    C --> C5[Marine]
    C --> C6[Aviation]
    C --> C7[Route]
    C --> C8[Historical]
    C --> C9[Climate Normals]
    
    D --> D1[Invalid Method]
    D --> D2[Invalid Tool]
    D --> D3[Invalid Parameters]
    D --> D4[API Failures]
    D --> D5[Authentication Errors]
    
    E --> E1[Response Time]
    E --> E2[Concurrent Requests]
    E --> E3[Large Data Sets]
    E --> E4[Rate Limiting]
    
    style A fill:#e3f2fd
    style B fill:#fff3e0
    style C fill:#e8f5e8
    style D fill:#ffebee
    style E fill:#f3e5f5
```

**⚠️ Replace `[YOUR_FLOW_HTTP_URL]` with your actual flow URL**

#### Test Weather Forecast
```http
POST [YOUR_FLOW_HTTP_URL]
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "id": "forecast-1",
  "method": "tools/call", 
  "params": {
    "name": "get_weather_forecast",
    "arguments": {
      "location": "40.7128,-74.0060",
      "timesteps": ["1h", "1d"],
      "units": "metric"
    }
  }
}
```

#### Test Real-time Weather
```http
POST [YOUR_FLOW_HTTP_URL]
Content-Type: application/json

{
  "jsonrpc": "2.0", 
  "id": "realtime-1",
  "method": "tools/call",
  "params": {
    "name": "get_realtime_weather",
    "arguments": {
      "location": "Boston, MA",
      "units": "imperial" 
    }
  }
}
```

## Additional Tomorrow.io Flow Examples

### 1. Basic Weather Forecast Flow

**Description**: Gets weather forecast for a specified location and sends summary via email.

**⚠️ Setup Required**: Configure your own Tomorrow.io connector and email connector connections.

**Steps**:
1. Manual/Scheduled Trigger
2. Get Weather Forecast (Tomorrow.io connector) - **Use your own connection**
3. Compose weather summary
4. Send email notification - **Configure your own email connection**

---

### 2. Weather Alert Monitoring Flow

**Description**: Monitors for weather alerts and sends notifications when severe weather is detected.

**⚠️ Setup Required**: Configure Tomorrow.io connector and notification connectors.

**Steps**:
1. Recurrence trigger (every 30 minutes)
2. Get Weather Alerts (Tomorrow.io connector) - **Use your own connection**
3. Condition: Check if alerts exist
4. Apply to each alert
5. Send Teams/Email notification - **Configure your own notification connections**

---

### 3. Air Quality Monitoring Flow

**Description**: Checks air quality conditions and sends health recommendations.

**⚠️ Setup Required**: Configure Tomorrow.io connector and notification connectors.

**Steps**:
1. Scheduled trigger (daily at 7 AM)
2. Get Real-time Air Quality (Tomorrow.io connector) - **Use your own connection**
3. Condition: Check AQI levels
4. Compose health recommendations
5. Send notification - **Configure your own notification connection**

---

### 4. Marine Weather for Shipping Flow

**Description**: Provides marine weather conditions for shipping route planning.

**⚠️ Setup Required**: Configure Tomorrow.io connector and data storage connectors.

**Steps**:
1. Manual trigger with location parameter
2. Get Marine Weather Forecast (Tomorrow.io connector) - **Use your own connection**
3. Parse wave height and wind conditions
4. Generate shipping recommendation
5. Update SharePoint list or send email - **Configure your own storage/email connections**

---

## Tomorrow.io Connector Actions Reference

**⚠️ Important**: You must configure your own Tomorrow.io connector with your API key

| Action Name | Description | Common Use Cases |
|-------------|-------------|------------------|
| **Get Weather Forecast** | Retrieves comprehensive weather forecast | Daily briefings, event planning |
| **Get Realtime Weather** | Current weather conditions | Real-time dashboards, alerts |
| **Get Weather Alerts** | Active weather warnings | Emergency notifications, safety alerts |
| **Get Air Quality** | Air pollution and quality data | Health monitoring, environmental compliance |
| **Get Marine Weather** | Ocean and coastal conditions | Shipping, offshore operations |
| **Get Aviation Weather** | Flight-specific weather data | Airport operations, flight planning |
| **Get Historical Weather** | Past weather data | Analysis, reporting, model training |

## Security and Configuration Best Practices

### 1. Connection Security
- **Never share connection IDs or API keys publicly**
- Use environment variables for sensitive configuration
- Regularly rotate API keys
- Limit connector permissions to minimum required

### 2. Authentication Configuration
- Set appropriate trigger authentication (Tenant vs Anyone)
- Implement proper error handling for authentication failures
- Monitor and log authentication attempts

### 3. Data Privacy
- Remove sensitive location data from logs
- Implement data retention policies
- Consider GDPR/privacy compliance for location tracking

### 4. Performance Optimization
- **Caching**: Store frequently accessed data to reduce API calls
- **Rate Limiting**: Respect Tomorrow.io API rate limits
- **Parallel Processing**: Use parallel branches for multiple locations
- **Selective Fields**: Only request needed weather parameters
- **Appropriate Intervals**: Don't over-poll the API

---

## Deployment Checklist

### Before Deployment:
- [ ] Replace all `[PLACEHOLDER]` values with actual configuration
- [ ] Create your own Tomorrow.io connector connection
- [ ] Configure API key securely (not in flow definition)
- [ ] Test all MCP endpoints with actual requests
- [ ] Set up proper error handling and logging
- [ ] Configure appropriate trigger authentication
- [ ] Test with valid and invalid inputs
- [ ] Verify connector permissions and quotas

### Environment Configuration:
- **Development**: Use test locations and limited recipients
- **Production**: Configure proper error notifications and monitoring
- **API Keys**: Use secure storage (Azure Key Vault recommended)

### Monitoring:
- Set up flow analytics and monitoring
- Configure failure notifications
- Track API usage and costs
- Monitor performance metrics

---

## Troubleshooting Common Issues

### 1. Connection Issues
- **Problem**: Cannot connect to Tomorrow.io API
- **Solution**: Verify API key is correctly configured in connector settings
- **Check**: Ensure API key has proper permissions and quotas

### 2. MCP Protocol Errors
- **Problem**: Invalid JSON-RPC responses
- **Solution**: Verify response format matches MCP specification
- **Check**: Ensure all required fields (jsonrpc, id, result/error) are included

### 3. Tool Execution Failures
- **Problem**: Tools return errors or empty responses
- **Solution**: Check tool argument parsing and API parameter mapping
- **Check**: Verify location format and API endpoint availability

### 4. Performance Issues
- **Problem**: Slow response times or timeouts
- **Solution**: Implement caching and optimize API calls
- **Check**: Monitor API quota usage and response times

### 5. Copilot Studio Compatibility Issues
- **Problem**: Declared capabilities not recognized by Copilot Studio
- **Solution**: Ensure you only declare `tools` and `logging` capabilities for current compatibility
- **Check**: Verify your server info and capability declarations match supported features
- **Future**: Monitor Microsoft updates for expanded MCP capability support

---

## Blog Post Template

### Suggested Blog Structure:

1. **Introduction to MCP and Power Automate Integration**
   - What is Model Context Protocol
   - Benefits of implementing MCP servers in Power Automate
   - Overview of weather data integration scenarios

2. **Part 1: MCP Template Implementation**
   - Setting up HTTP Request trigger with JSON-RPC schema
   - Implementing method routing with Switch actions
   - Error handling and response formatting
   - Security considerations and best practices

3. **Part 2: Tomorrow.io Weather Connector Example**
   - Adapting the template for weather data
   - Implementing weather-specific tools
   - Testing MCP endpoints with weather requests
   - Real-world use cases and scenarios

4. **Advanced Topics**
   - Performance optimization strategies
   - Error handling and retry logic
   - Monitoring and analytics setup
   - Security best practices

5. **Conclusion and Next Steps**
   - Benefits achieved with MCP implementation
   - Ideas for extending to other connectors
   - Community resources and further reading

---

## Appendix: Authentication in Power Platform Connectors

### Supported Authentication Types

Power Platform connectors support four main authentication types that can be configured when creating custom connectors:

```mermaid
graph TD
    A[Power Platform Connector Authentication] --> B[No Authentication]
    A --> C[Basic Authentication]
    A --> D[API Key Authentication]
    A --> E[OAuth 2.0]
    
    B --> B1[Anonymous Access]
    B --> B2[Public APIs]
    B --> B3[Internal Services]
    
    C --> C1[Username + Password]
    C --> C2[Simple Implementation]
    C --> C3[Legacy Systems]
    
    D --> D1[API Key in Header]
    D --> D2[API Key in Query String]
    D --> D3[Single Secret Token]
    
    E --> E1[Microsoft Entra ID]
    E --> E2[Generic OAuth 2.0]
    E --> E3[Third-party Providers]
    E --> E4[Facebook, GitHub, Google]
    
    style A fill:#e3f2fd
    style B fill:#e8f5e8
    style C fill:#fff3e0
    style D fill:#e1f5fe
    style E fill:#f3e5f5
```

### Authentication Configuration Details

#### 1. No Authentication
- **Use Case**: Public APIs, internal services, anonymous access
- **Security Level**: None
- **Implementation**: No authentication parameters required
- **Best For**: Testing, public data APIs, internal-only services

#### 2. Basic Authentication
- **Use Case**: Simple username/password authentication
- **Security Level**: Low to Medium (requires HTTPS)
- **Configuration**:
  - Username field label (customizable)
  - Password field label (customizable)
- **Best For**: Legacy systems, simple authentication requirements

#### 3. API Key Authentication
- **Use Case**: Token-based authentication with a single secret
- **Security Level**: Medium to High
- **Configuration Options**:
  - **Parameter Label**: Display name for the API key field
  - **Parameter Name**: Actual header/query parameter name
  - **Parameter Location**: Header or Query String
- **Example Configuration**:
  ```json
  {
    "parameterLabel": "API Key",
    "parameterName": "Ocp-Apim-Subscription-Key",
    "parameterLocation": "Header"
  }
  ```
- **Best For**: REST APIs, modern services, Azure Cognitive Services

#### 4. OAuth 2.0 Authentication
- **Use Case**: Secure delegated access with token refresh
- **Security Level**: High
- **Identity Providers Supported**:
  - **Generic OAuth 2.0**: Any OAuth 2.0 compliant service
  - **Microsoft Entra ID**: Azure Active Directory
  - **Specific Providers**: Facebook, GitHub, Google, Dropbox, Salesforce
- **Required Configuration**:
  - **Client ID**: Application identifier
  - **Client Secret**: Application secret
  - **Authorization URL**: OAuth authorization endpoint
  - **Token URL**: Token exchange endpoint
  - **Refresh URL**: Token refresh endpoint (optional)

#### OAuth 2.0 Flow Diagram

```mermaid
sequenceDiagram
    participant U as User
    participant PP as Power Platform
    participant AS as Authorization Server
    participant API as Protected API
    
    Note over U,API: OAuth 2.0 Authorization Code Flow
    U->>PP: Create Connection
    PP->>AS: Redirect to Authorization URL
    AS->>U: Show Login/Consent Page
    U->>AS: Provide Credentials & Consent
    AS->>PP: Return Authorization Code
    PP->>AS: Exchange Code for Access Token
    AS->>PP: Return Access Token & Refresh Token
    PP->>PP: Store Tokens Securely
    
    Note over U,API: API Request Flow
    U->>PP: Trigger Flow/App Action
    PP->>API: API Request with Access Token
    API->>PP: Return Data
    PP->>U: Return Results
    
    Note over U,API: Token Refresh (when expired)
    PP->>AS: Use Refresh Token
    AS->>PP: Return New Access Token
    PP->>API: Retry Request with New Token
```

### Tomorrow.io Authentication Implementation

The Tomorrow.io connector uses **API Key Authentication** with the following configuration:

```json
{
  "connectionParameters": {
    "api_key": {
      "type": "securestring",
      "uiDefinition": {
        "displayName": "API Key",
        "description": "The API Key for this api",
        "tooltip": "Provide your Tomorrow.io API Key",
        "constraints": {
          "required": "true"
        }
      }
    }
  }
}
```

#### Security Considerations

1. **API Key Storage**: 
   - Keys are encrypted and stored in Microsoft's secure token store
   - Only accessible by Power Platform services
   - Never exposed in logs or error messages

2. **HTTPS Requirement**: 
   - All API calls must use HTTPS
   - API keys transmitted securely

3. **Rate Limiting**: 
   - Tomorrow.io enforces rate limits per API key
   - Implement proper retry logic with exponential backoff

4. **Key Rotation**: 
   - Users can update API keys in connection settings
   - No need to recreate flows when rotating keys

### Authentication Best Practices

#### For Connector Developers
1. **Choose Appropriate Authentication**:
   - Use OAuth 2.0 for user-delegated access
   - Use API Key for service-to-service communication
   - Use Basic Auth only for legacy systems over HTTPS

2. **Security Implementation**:
   - Always use HTTPS for API endpoints
   - Implement proper error handling for auth failures
   - Don't expose sensitive information in error messages
   - Support token refresh for OAuth implementations

3. **User Experience**:
   - Provide clear parameter labels
   - Include helpful descriptions and tooltips
   - Test connection creation flow thoroughly

#### For Flow Developers
1. **Connection Management**:
   - Use shared connections where appropriate
   - Regularly rotate API keys and credentials
   - Monitor connection health and expiration

2. **Error Handling**:
   - Implement retry logic for authentication failures
   - Provide fallback mechanisms where possible
   - Log authentication errors appropriately

3. **Security**:
   - Don't hardcode credentials in flows
   - Use environment-specific connections
   - Follow principle of least privilege

### Authentication Troubleshooting

Common authentication issues and solutions:

| Issue | Possible Causes | Solutions |
|-------|----------------|-----------|
| Invalid API Key | Wrong key, expired key, insufficient permissions | Verify key in service portal, check permissions, regenerate if needed |
| OAuth Token Expired | Token expired, refresh failed | Check refresh token, re-authorize connection, verify endpoints |
| Unauthorized Access | Insufficient permissions, wrong endpoint | Verify API permissions, check endpoint URLs, validate scopes |
| Rate Limiting | Too many requests, quota exceeded | Implement backoff strategy, check rate limits, upgrade plan if needed |
| SSL/TLS Errors | Certificate issues, protocol mismatch | Verify HTTPS endpoints, check certificate validity, update TLS version |

## HTTP Request Trigger Authentication

The "When an HTTP request is received" trigger has its own authentication layer that works **in addition to** connector authentication. This provides a dual-layer security model for MCP servers.

### HTTP Trigger Authentication Types

```mermaid
graph TD
    A[HTTP Request Trigger] --> B[Authentication Options]
    
    B --> C[Anyone - Legacy]
    B --> D[Any User in Tenant - Default]
    B --> E[Specific Users in Tenant]
    
    C --> C1[❌ No Authentication Required]
    C --> C2[⚠️ Security Risk]
    C --> C3[Only for Testing]
    
    D --> D1[✅ Microsoft Entra ID Token Required]
    D --> D2[🏢 Tenant-scoped Access]
    D --> D3[Default for New Flows]
    
    E --> E1[✅ Microsoft Entra ID Token Required]
    E --> E2[👤 User-specific Access]
    E --> E3[Email/Object ID List]
    E --> E4[Service Principal Support]
    
    style A fill:#e3f2fd
    style C fill:#ffebee
    style D fill:#fff3e0
    style E fill:#e8f5e8
```

### Authentication Flow for MCP Servers

```mermaid
sequenceDiagram
    participant C as Copilot Studio/Client
    participant AD as Microsoft Entra ID
    participant PA as Power Automate HTTP Trigger
    participant MC as MCP Connector Logic
    participant API as External API
    
    Note over C,API: Phase 1: Client Authentication
    C->>AD: Request Access Token
    AD->>C: Return Bearer Token with Claims
    
    Note over C,API: Phase 2: HTTP Trigger Authentication  
    C->>PA: MCP Request + Bearer Token
    PA->>PA: Validate Token Claims
    PA->>PA: Check Tenant/User Authorization
    
    Note over C,API: Phase 3: MCP Processing
    PA->>MC: Process MCP Request
    MC->>MC: Parse JSON-RPC
    
    Note over C,API: Phase 4: Connector Authentication
    MC->>API: API Call + Connector Auth (API Key)
    API->>MC: Return Data
    MC->>PA: Format MCP Response
    PA->>C: Return MCP Result
    
    Note over C,API: Error Handling
    PA-->>C: 401 Unauthorized (Invalid Token)
    PA-->>C: 403 Forbidden (User Not Allowed)
```

### Required JWT Claims

When using authenticated HTTP triggers, the request must include a valid JWT token with these claims:

| Claim | Description | Required | Example |
|-------|-------------|----------|---------|
| `aud` | Audience - Power Platform service endpoint | ✅ Always | `https://service.flow.microsoft.com/` |
| `iss` | Issuer - Token issuer authority | ✅ Always | `https://sts.windows.net/{tenant-id}/` |
| `tid` | Tenant ID - Azure AD tenant identifier | ✅ Always | `12345678-1234-1234-1234-123456789012` |
| `oid` | Object ID - User/Service Principal identifier | ⚠️ If specific users | `87654321-4321-4321-4321-210987654321` |
| `sub` | Subject - Token subject | ✅ Always | User or application identifier |
| `exp` | Expiration - Token expiry timestamp | ✅ Always | Unix timestamp |

### Cloud-Specific Audience Values

```mermaid
graph LR
    A[Power Platform Clouds] --> B[Public Cloud]
    A --> C[GCC]
    A --> D[GCCH]
    A --> E[China]
    A --> F[DoD]
    
    B --> B1[https://service.flow.microsoft.com/]
    C --> C1[https://gov.service.flow.microsoft.us/]
    D --> D1[https://high.service.flow.microsoft.us/]
    E --> E1[https://service.powerautomate.cn/]
    F --> F1[https://service.flow.appsplatform.us/]
    
    style A fill:#e3f2fd
    style B fill:#e8f5e8
    style C fill:#fff3e0
    style D fill:#ffcdd2
    style E fill:#f3e5f5
    style F fill:#e1f5fe
```

### Dual Authentication Architecture

For MCP servers, authentication works at two distinct layers:

#### Layer 1: HTTP Trigger Authentication (Power Platform)
- **Purpose**: Controls who can invoke the Power Automate flow
- **Scope**: Microsoft Entra ID authentication
- **Configuration**: Set in the HTTP trigger settings
- **Token**: Bearer token in Authorization header

#### Layer 2: Connector Authentication (External API)
- **Purpose**: Authenticates with the external service (Tomorrow.io)
- **Scope**: Service-specific authentication (API Key, OAuth, etc.)
- **Configuration**: Set in connector connection
- **Credentials**: Stored securely in Power Platform

### Security Configuration Examples

#### Example 1: Public MCP Server (Testing Only)
```json
{
  "triggerAuthentication": "Anyone",
  "connectorAuthentication": "API Key",
  "securityLevel": "Low",
  "useCase": "Development/Testing"
}
```

#### Example 2: Enterprise MCP Server (Recommended)
```json
{
  "triggerAuthentication": "Any user in my tenant",
  "connectorAuthentication": "API Key",
  "securityLevel": "High",
  "useCase": "Production deployment"
}
```

#### Example 3: Restricted MCP Server (High Security)
```json
{
  "triggerAuthentication": "Specific users in my tenant",
  "allowedUsers": [
    "copilot-service@company.com",
    "mcp-integration@company.com"
  ],
  "connectorAuthentication": "OAuth 2.0",
  "securityLevel": "Very High",
  "useCase": "Sensitive data access"
}
```

### Implementation for Tomorrow.io MCP Server

For the Tomorrow.io weather connector, the recommended configuration is:

1. **HTTP Trigger Authentication**: "Any user in my tenant"
   - Allows any authenticated user in your organization
   - Requires valid Microsoft Entra ID token
   - Prevents external unauthorized access

2. **Connector Authentication**: "API Key"
   - Uses Tomorrow.io API key for weather service access
   - Stored securely in Power Platform
   - Separate from HTTP trigger authentication

### Authentication Headers

#### Client Request Headers
```http
POST https://your-flow-endpoint.com/triggers/manual/run
Authorization: Bearer eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsIng1dCI6...
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "id": "1",
  "params": {
    "name": "get_current_weather",
    "arguments": {
      "location": "Seattle, WA"
    }
  }
}
```

#### Flow to Connector Headers
```http
POST https://api.tomorrow.io/v4/weather/realtime
Ocp-Apim-Subscription-Key: your-tomorrow-io-api-key
Content-Type: application/json

{
  "location": "Seattle, WA",
  "fields": ["temperature", "humidity", "weatherCode"]
}
```

### Security Best Practices

#### For HTTP Trigger Security
1. **Always use authentication**: Never use "Anyone" in production
2. **Principle of least privilege**: Use "Specific users" when possible
3. **Token validation**: Verify all required claims are present
4. **Monitor access**: Review flow run history for unauthorized attempts
5. **IP restrictions**: Use Power Platform IP firewall when available

#### For Connector Security
1. **Secure credential storage**: Use Power Platform's secure storage
2. **Regular key rotation**: Rotate API keys periodically
3. **Environment separation**: Use different keys for dev/test/prod
4. **Monitor usage**: Track API usage and rate limits
5. **Error handling**: Don't expose credentials in error messages

### Troubleshooting Authentication Issues

| Issue | HTTP Trigger | Connector | Solution |
|-------|--------------|-----------|----------|
| 401 Unauthorized | Invalid/missing bearer token | Invalid API key | Check token claims, verify API key |
| 403 Forbidden | User not in allowed list | Insufficient API permissions | Update allowed users, check API scope |
| Token Expired | Bearer token expired | API key expired | Refresh token, update API key |
| Wrong Audience | Incorrect cloud endpoint | Wrong API endpoint | Verify audience claim, check API URL |
| CORS Issues | Browser restrictions | API CORS policy | Use proper headers, check CORS settings |

This dual-authentication model provides robust security for MCP servers while maintaining flexibility for different deployment scenarios.

---

*⚠️ **Important Disclaimer**: This documentation contains templates and examples only. All connection IDs, API keys, and sensitive configuration have been removed or replaced with placeholders. You must create your own connections and replace all placeholder values with your actual configuration before using these templates.*

---

*Last Updated: September 4, 2025*
*Author: Troy Taylor (troy@troystaylor.com)*
