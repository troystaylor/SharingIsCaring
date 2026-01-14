# GitHub Copilot Instructions for SharingIsCaring

## Repository Overview
This repository contains a collection of custom connectors, code transformations, and integrations for Microsoft Power Platform, Microsoft 365 services, and third-party APIs. It serves as a resource library for building low-code/no-code solutions with custom API integrations.

## Repository Structure
- **Individual connector folders**: Each folder contains a complete custom connector configuration including:
  - `apiDefinition.swagger.json` - OpenAPI/Swagger definition
  - `apiProperties.json` - Connector properties and metadata
  - `script.csx` - C# transformation scripts for request/response handling
  - `readme.md` - Documentation and usage instructions

- **Connector-Code folder**: Reusable code snippets and patterns for common transformation scenarios

## Code Style and Conventions

### C# Script Files (*.csx)
- Use C# scripting syntax compatible with Power Platform custom connectors
- Follow Azure Functions C# script conventions
- Include XML documentation comments for public methods
- Use async/await patterns for asynchronous operations
- Handle null values gracefully
- Implement proper error handling and logging

### OpenAPI Definitions (apiDefinition.swagger.json)
- Follow OpenAPI 3.0 or Swagger 2.0 specifications
- Use clear, descriptive operation IDs
- Include comprehensive parameter descriptions
- Define all request/response schemas
- Use `x-ms-` extensions for Power Platform-specific features

### API Properties (apiProperties.json)
- Define proper connector metadata
- Configure authentication settings
- Set appropriate API capabilities
- Include privacy policy and terms of service URLs

### Documentation (readme.md)
- Start with a clear description of the connector's purpose
- Include prerequisites and setup instructions
- Provide configuration steps with screenshots where helpful
- Document any custom code transformations
- Include example use cases

## Common Patterns

### Authentication
- OAuth 2.0 with PKCE for enhanced security
- Bearer token authentication
- JWT token handling
- Custom authentication flows for non-standard implementations

### Data Transformation
- Handle mixed-type array responses
- Convert data types (e.g., Long to String)
- Escape HTML in responses
- Transform XML to JSON
- Hash string values for privacy

### Integration Patterns
- Microsoft Graph API integrations (Calendar, Mail, Users)
- Dataverse Custom APIs
- Microsoft 365 Copilot extensions
- Third-party API connectors (Crunchbase, Snowflake, etc.)

## Best Practices

1. **Security First**
   - Never commit API keys, secrets, or credentials
   - Use connection parameters for sensitive data
   - Implement proper OAuth flows
   - Validate and sanitize all inputs

2. **Error Handling**
   - Provide meaningful error messages
   - Log errors appropriately (see Application Insights Logging)
   - Handle edge cases and null values
   - Return proper HTTP status codes

3. **Performance**
   - Minimize unnecessary API calls
   - Implement caching where appropriate
   - Use async operations for I/O-bound work
   - Optimize JSON serialization/deserialization

4. **Maintainability**
   - Keep transformation scripts focused and modular
   - Reuse common patterns from Connector-Code folder
   - Document complex logic with comments
   - Version API definitions properly

5. **Testing**
   - Test all authentication flows
   - Validate request/response transformations
   - Test error scenarios
   - Verify compatibility with Power Platform

## Power Platform Specific

### Custom Connector Extensions
- Use `x-ms-visibility` to control field visibility
- Implement `x-ms-dynamic-values` for dynamic dropdowns
- Use `x-ms-summary` for user-friendly field names
- Configure `x-ms-trigger` for webhook-based triggers

### Copilot for Microsoft 365
- Follow Copilot plugin best practices
- Implement semantic descriptions for AI understanding
- Use appropriate response formats for Copilot consumption
- Configure retrieval augmentation where applicable

## Dependencies and Tools
- Power Platform CLI for connector deployment
- Postman or similar for API testing
- Visual Studio Code with Power Platform extensions
- Azure subscription for hosting and logging (optional)

## Contributing Guidelines
When adding new connectors or code samples:
1. Create a descriptive folder name
2. Include all required files (API definition, properties, script, readme)
3. Test thoroughly in Power Platform environment
4. Document all configuration steps
5. Include example scenarios and screenshots
6. Follow existing naming conventions
7. Add Application Insights logging where appropriate

## Support and Resources
- Power Platform Connectors Documentation
- Microsoft Graph API Documentation
- OpenAPI Specification
- Azure Functions C# Script Reference
