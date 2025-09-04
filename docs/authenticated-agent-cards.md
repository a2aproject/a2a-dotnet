# Extended Authenticated Agent Cards

Extended Authenticated Agent Cards allow agents to provide additional capabilities and information to authenticated users that are not available to unauthenticated users. This feature implements [section 9.1 of the A2A protocol specification](https://a2a-protocol.org/dev/specification/#91-fetching-authenticated-extended-agent-card).

## Overview

When a user is authenticated, agents can provide:
- **Enhanced capabilities** with additional skills and features
- **Role-based access** to sensitive or administrative functionality
- **Personalized experiences** with user-specific information
- **Extended metadata** about the agent's capabilities

The agent indicates support for authenticated extended cards by setting `SupportsAuthenticatedExtendedCard = true` in its standard agent card.

## Implementation

### 1. Basic Setup

First, ensure your agent indicates support for authenticated extended cards:

```csharp
public Task<AgentCard> GetAgentCardAsync(string agentUrl, CancellationToken cancellationToken)
{
    return Task.FromResult(new AgentCard
    {
        Name = "My Agent",
        Description = "A sample agent",
        Url = agentUrl,
        Version = "1.0.0",
        Skills = [/* basic skills */],
        SupportsAuthenticatedExtendedCard = true  // Important!
    });
}
```

### 2. Implement Authenticated Handler

Add an authenticated agent card handler to your agent:

```csharp
public void Attach(ITaskManager taskManager)
{
    taskManager.OnAgentCardQuery = GetAgentCardAsync;
    taskManager.OnAuthenticatedAgentCardQuery = GetAuthenticatedAgentCardAsync;  // New!
}

private Task<AgentCard> GetAuthenticatedAgentCardAsync(
    string agentUrl, 
    AuthenticationContext? authContext, 
    CancellationToken cancellationToken)
{
    // Base skills for all users
    var skills = new List<AgentSkill>
    {
        new() { Id = "basic", Name = "Basic Functionality", Description = "Available to all users" }
    };

    // Add authenticated-only features
    if (authContext?.IsAuthenticated == true)
    {
        skills.Add(new AgentSkill
        {
            Id = "enhanced",
            Name = "Enhanced Features",
            Description = "Advanced capabilities for authenticated users",
            Examples = ["advanced command", "user-specific action"]
        });

        // Role-based features
        if (authContext.HasClaim("role", "admin"))
        {
            skills.Add(new AgentSkill
            {
                Id = "admin",
                Name = "Administrative Functions",
                Description = "Admin-only capabilities",
                Examples = ["system status", "user management"]
            });
        }
    }

    return Task.FromResult(new AgentCard
    {
        Name = "My Agent (Extended)",
        Description = authContext?.IsAuthenticated == true 
            ? $"Enhanced agent for user: {authContext.UserName}"
            : "A sample agent",
        Url = agentUrl,
        Version = "1.0.0",
        Skills = skills,
        SupportsAuthenticatedExtendedCard = true
    });
}
```

### 3. Authentication Context

The `AuthenticationContext` provides information about the authenticated user:

```csharp
public sealed class AuthenticationContext
{
    public ClaimsPrincipal? User { get; set; }
    public string? Scheme { get; set; }
    public IDictionary<string, object>? Properties { get; set; }
    
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;
    public string? UserName => User?.Identity?.Name;
    
    public bool HasClaim(string claimType, string? claimValue = null);
    public IEnumerable<string> GetClaimValues(string claimType);
}
```

#### Common Usage Patterns

```csharp
// Check if user is authenticated
if (authContext?.IsAuthenticated == true)
{
    // Add authenticated features
}

// Check specific claims
if (authContext.HasClaim(ClaimTypes.Role, "admin"))
{
    // Add admin features
}

// Get all roles
var userRoles = authContext.GetClaimValues(ClaimTypes.Role);

// Access user information
var userId = authContext.GetClaimValues("sub").FirstOrDefault();
var email = authContext.GetClaimValues(ClaimTypes.Email).FirstOrDefault();
```

## API Endpoints

### Agent Card Discovery
- **GET** `/v1/card` - Returns agent card based on authentication context
- **GET** `/.well-known/agent-card.json` - Returns the standard agent card

The `/v1/card` endpoint:
- Returns the extended card if the user is authenticated and an authenticated handler is configured
- Falls back to the standard card if the user is not authenticated or no authenticated handler exists
- Automatically detects authentication information from the request

### JSON-RPC Method
- **`agent/getAuthenticatedExtendedCard`** - JSON-RPC method for retrieving authenticated agent cards

Parameters:
```json
{
  "agentUrl": "https://example.com/agent"
}
```

The JSON-RPC method:
- Uses the same shared implementation as the HTTP endpoint
- Automatically extracts authentication context from the request
- Falls back to the standard agent card for unauthenticated requests

## Example: Role-Based Features

```csharp
private Task<AgentCard> GetAuthenticatedAgentCardAsync(
    string agentUrl, 
    AuthenticationContext? authContext, 
    CancellationToken cancellationToken)
{
    var skills = new List<AgentSkill>();
    
    // Base functionality (always available)
    skills.Add(new AgentSkill
    {
        Id = "search",
        Name = "Search",
        Description = "Search public information"
    });

    if (authContext?.IsAuthenticated == true)
    {
        // Authenticated user features
        skills.Add(new AgentSkill
        {
            Id = "personal-data",
            Name = "Personal Data Access",
            Description = "Access your personal information"
        });

        // Role-based features
        var userRoles = authContext.GetClaimValues("role");
        
        if (userRoles.Contains("premium"))
        {
            skills.Add(new AgentSkill
            {
                Id = "premium-features",
                Name = "Premium Features",
                Description = "Advanced analytics and reporting"
            });
        }

        if (userRoles.Contains("admin"))
        {
            skills.Add(new AgentSkill
            {
                Id = "user-management",
                Name = "User Management",
                Description = "Manage users and permissions"
            });
            
            skills.Add(new AgentSkill
            {
                Id = "system-monitoring",
                Name = "System Monitoring",
                Description = "View system health and metrics"
            });
        }
    }

    return Task.FromResult(new AgentCard
    {
        Name = "Business Agent",
        Description = GetDescription(authContext),
        Url = agentUrl,
        Skills = skills,
        SupportsAuthenticatedExtendedCard = true
    });
}

private string GetDescription(AuthenticationContext? authContext)
{
    if (authContext?.IsAuthenticated != true)
        return "Business agent with public features";
        
    var roles = authContext.GetClaimValues("role").ToList();
    if (roles.Contains("admin"))
        return $"Business agent with full administrative access for {authContext.UserName}";
    if (roles.Contains("premium"))
        return $"Business agent with premium features for {authContext.UserName}";
        
    return $"Business agent with authenticated features for {authContext.UserName}";
}
```

## Security Considerations

1. **Validate Authentication**: Always check `authContext?.IsAuthenticated` before providing enhanced features
2. **Verify Claims**: Use `HasClaim()` to verify specific permissions before exposing sensitive capabilities
3. **Principle of Least Privilege**: Only expose the minimum capabilities required for each user role
4. **Sensitive Information**: Avoid exposing sensitive system information in agent cards, even for admin users

## Testing

The implementation includes comprehensive unit tests:

```csharp
[Fact]
public async Task GetAuthenticatedAgentCardAsync_WithAuthenticatedUser_ReturnsExtendedCard()
{
    // Test that authenticated users receive extended capabilities
}

[Fact] 
public async Task GetAuthenticatedAgentCardAsync_WithoutAuthenticatedUser_ReturnsStandardCard()
{
    // Test fallback to standard card for unauthenticated users
}
```

## Sample Implementation

See the updated `EchoAgent` in `/samples/AgentServer/EchoAgent.cs` for a complete working example that demonstrates:
- Basic echo functionality for all users
- Enhanced admin echo for authenticated users
- System information capabilities for admin role users
- Dynamic descriptions based on authentication status

## Best Practices

1. **Graceful Degradation**: Always provide meaningful functionality for unauthenticated users
2. **Clear Documentation**: Use descriptive skill names and examples to help users understand available features
3. **Consistent Behavior**: Ensure the extended card capabilities match what the agent actually provides
4. **Performance**: Keep authentication checks lightweight since agent cards may be requested frequently
5. **Backward Compatibility**: Continue to support clients that only request standard agent cards