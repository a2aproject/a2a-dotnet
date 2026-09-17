namespace A2A.Grpc;

using System.Text.Json;
using Google.Protobuf.WellKnownTypes;

/// <summary>
/// Conversions for the <see cref="AgentCard"/> object graph returned by <c>GetExtendedAgentCard</c>
/// (capabilities, skills, provider, security schemes, OAuth flows and signatures).
/// </summary>
internal static partial class ProtoMap
{
    // ---- AgentCard ----------------------------------------------------------------------------

    public static Protos.AgentCard ToProto(AgentCard card)
    {
        var result = new Protos.AgentCard
        {
            Name = card.Name,
            Description = card.Description,
            Version = card.Version,
            Capabilities = ToProto(card.Capabilities),
        };

        if (card.DocumentationUrl is not null)
        {
            result.DocumentationUrl = card.DocumentationUrl;
        }

        if (card.IconUrl is not null)
        {
            result.IconUrl = card.IconUrl;
        }

        if (card.Provider is not null)
        {
            result.Provider = ToProto(card.Provider);
        }

        foreach (var agentInterface in card.SupportedInterfaces)
        {
            result.SupportedInterfaces.Add(ToProto(agentInterface));
        }

        foreach (var skill in card.Skills)
        {
            result.Skills.Add(ToProto(skill));
        }

        result.DefaultInputModes.AddRange(card.DefaultInputModes);
        result.DefaultOutputModes.AddRange(card.DefaultOutputModes);

        if (card.SecuritySchemes is not null)
        {
            foreach (var pair in card.SecuritySchemes)
            {
                result.SecuritySchemes[pair.Key] = ToProto(pair.Value);
            }
        }

        if (card.SecurityRequirements is not null)
        {
            foreach (var requirement in card.SecurityRequirements)
            {
                result.SecurityRequirements.Add(ToProto(requirement));
            }
        }

        if (card.Signatures is not null)
        {
            foreach (var signature in card.Signatures)
            {
                result.Signatures.Add(ToProto(signature));
            }
        }

        return result;
    }

    public static AgentCard ToDomain(Protos.AgentCard card)
    {
        var result = new AgentCard
        {
            Name = card.Name,
            Description = card.Description,
            Version = card.Version,
            DocumentationUrl = card.HasDocumentationUrl ? card.DocumentationUrl : null,
            IconUrl = card.HasIconUrl ? card.IconUrl : null,
            Capabilities = ToDomain(card.Capabilities),
            Provider = card.Provider is null ? null : ToDomain(card.Provider),
            DefaultInputModes = [.. card.DefaultInputModes],
            DefaultOutputModes = [.. card.DefaultOutputModes],
        };

        foreach (var agentInterface in card.SupportedInterfaces)
        {
            result.SupportedInterfaces.Add(ToDomain(agentInterface));
        }

        foreach (var skill in card.Skills)
        {
            result.Skills.Add(ToDomain(skill));
        }

        if (card.SecuritySchemes.Count > 0)
        {
            result.SecuritySchemes = [];
            foreach (var pair in card.SecuritySchemes)
            {
                result.SecuritySchemes[pair.Key] = ToDomain(pair.Value);
            }
        }

        if (card.SecurityRequirements.Count > 0)
        {
            result.SecurityRequirements = [];
            foreach (var requirement in card.SecurityRequirements)
            {
                result.SecurityRequirements.Add(ToDomain(requirement));
            }
        }

        if (card.Signatures.Count > 0)
        {
            result.Signatures = [];
            foreach (var signature in card.Signatures)
            {
                result.Signatures.Add(ToDomain(signature));
            }
        }

        return result;
    }

    // ---- AgentInterface / AgentProvider -------------------------------------------------------

    public static Protos.AgentInterface ToProto(AgentInterface agentInterface)
    {
        var result = new Protos.AgentInterface
        {
            Url = agentInterface.Url,
            ProtocolBinding = agentInterface.ProtocolBinding,
            ProtocolVersion = agentInterface.ProtocolVersion,
        };

        if (agentInterface.Tenant is not null)
        {
            result.Tenant = agentInterface.Tenant;
        }

        return result;
    }

    public static AgentInterface ToDomain(Protos.AgentInterface agentInterface) => new()
    {
        Url = agentInterface.Url,
        ProtocolBinding = agentInterface.ProtocolBinding,
        ProtocolVersion = agentInterface.ProtocolVersion,
        Tenant = NullIfEmpty(agentInterface.Tenant),
    };

    public static Protos.AgentProvider ToProto(AgentProvider provider) => new()
    {
        Organization = provider.Organization,
        Url = provider.Url,
    };

    public static AgentProvider ToDomain(Protos.AgentProvider provider) => new()
    {
        Organization = provider.Organization,
        Url = provider.Url,
    };

    // ---- AgentCapabilities / AgentExtension ---------------------------------------------------

    public static Protos.AgentCapabilities ToProto(AgentCapabilities capabilities)
    {
        var result = new Protos.AgentCapabilities();

        if (capabilities.Streaming.HasValue)
        {
            result.Streaming = capabilities.Streaming.Value;
        }

        if (capabilities.PushNotifications.HasValue)
        {
            result.PushNotifications = capabilities.PushNotifications.Value;
        }

        if (capabilities.ExtendedAgentCard.HasValue)
        {
            result.ExtendedAgentCard = capabilities.ExtendedAgentCard.Value;
        }

        if (capabilities.Extensions is not null)
        {
            foreach (var extension in capabilities.Extensions)
            {
                result.Extensions.Add(ToProto(extension));
            }
        }

        return result;
    }

    public static AgentCapabilities ToDomain(Protos.AgentCapabilities capabilities)
    {
        var result = new AgentCapabilities
        {
            Streaming = capabilities.HasStreaming ? capabilities.Streaming : null,
            PushNotifications = capabilities.HasPushNotifications ? capabilities.PushNotifications : null,
            ExtendedAgentCard = capabilities.HasExtendedAgentCard ? capabilities.ExtendedAgentCard : null,
        };

        if (capabilities.Extensions.Count > 0)
        {
            result.Extensions = [];
            foreach (var extension in capabilities.Extensions)
            {
                result.Extensions.Add(ToDomain(extension));
            }
        }

        return result;
    }

    public static Protos.AgentExtension ToProto(AgentExtension extension)
    {
        var result = new Protos.AgentExtension
        {
            Uri = extension.Uri,
            Required = extension.Required ?? false,
        };

        if (extension.Description is not null)
        {
            result.Description = extension.Description;
        }

        if (extension.Params is { ValueKind: JsonValueKind.Object } element)
        {
            result.Params = ToProtoValue(element).StructValue;
        }

        return result;
    }

    public static AgentExtension ToDomain(Protos.AgentExtension extension) => new()
    {
        Uri = extension.Uri,
        Description = NullIfEmpty(extension.Description),
        Required = extension.Required,
        Params = extension.Params is null ? null : ToJsonElement(Value.ForStruct(extension.Params)),
    };

    // ---- AgentSkill ---------------------------------------------------------------------------

    public static Protos.AgentSkill ToProto(AgentSkill skill)
    {
        var result = new Protos.AgentSkill
        {
            Id = skill.Id,
            Name = skill.Name,
            Description = skill.Description,
        };

        result.Tags.AddRange(skill.Tags);

        if (skill.Examples is not null)
        {
            result.Examples.AddRange(skill.Examples);
        }

        if (skill.InputModes is not null)
        {
            result.InputModes.AddRange(skill.InputModes);
        }

        if (skill.OutputModes is not null)
        {
            result.OutputModes.AddRange(skill.OutputModes);
        }

        if (skill.SecurityRequirements is not null)
        {
            foreach (var requirement in skill.SecurityRequirements)
            {
                result.SecurityRequirements.Add(ToProto(requirement));
            }
        }

        return result;
    }

    public static AgentSkill ToDomain(Protos.AgentSkill skill)
    {
        var result = new AgentSkill
        {
            Id = skill.Id,
            Name = skill.Name,
            Description = skill.Description,
            Tags = [.. skill.Tags],
        };

        if (skill.Examples.Count > 0)
        {
            result.Examples = [.. skill.Examples];
        }

        if (skill.InputModes.Count > 0)
        {
            result.InputModes = [.. skill.InputModes];
        }

        if (skill.OutputModes.Count > 0)
        {
            result.OutputModes = [.. skill.OutputModes];
        }

        if (skill.SecurityRequirements.Count > 0)
        {
            result.SecurityRequirements = [];
            foreach (var requirement in skill.SecurityRequirements)
            {
                result.SecurityRequirements.Add(ToDomain(requirement));
            }
        }

        return result;
    }

    // ---- AgentCardSignature -------------------------------------------------------------------

    public static Protos.AgentCardSignature ToProto(AgentCardSignature signature)
    {
        var result = new Protos.AgentCardSignature
        {
            Protected = signature.Protected,
            Signature = signature.Signature,
        };

        var header = ToProtoStruct(signature.Header);
        if (header is not null)
        {
            result.Header = header;
        }

        return result;
    }

    public static AgentCardSignature ToDomain(Protos.AgentCardSignature signature) => new()
    {
        Protected = signature.Protected,
        Signature = signature.Signature,
        Header = ToMetadata(signature.Header),
    };

    // ---- SecurityRequirement / StringList -----------------------------------------------------

    public static Protos.SecurityRequirement ToProto(SecurityRequirement requirement)
    {
        var result = new Protos.SecurityRequirement();
        if (requirement.Schemes is not null)
        {
            foreach (var pair in requirement.Schemes)
            {
                result.Schemes[pair.Key] = ToProto(pair.Value);
            }
        }

        return result;
    }

    public static SecurityRequirement ToDomain(Protos.SecurityRequirement requirement)
    {
        var result = new SecurityRequirement();
        if (requirement.Schemes.Count > 0)
        {
            result.Schemes = [];
            foreach (var pair in requirement.Schemes)
            {
                result.Schemes[pair.Key] = ToDomain(pair.Value);
            }
        }

        return result;
    }

    public static Protos.StringList ToProto(StringList list)
    {
        var result = new Protos.StringList();
        result.List.AddRange(list.List);
        return result;
    }

    public static StringList ToDomain(Protos.StringList list) => new()
    {
        List = [.. list.List],
    };

    // ---- SecurityScheme (oneof) ---------------------------------------------------------------

    public static Protos.SecurityScheme ToProto(SecurityScheme scheme)
    {
        var result = new Protos.SecurityScheme();
        switch (scheme.SchemeCase)
        {
            case SecuritySchemeCase.ApiKey:
                result.ApiKeySecurityScheme = ToProto(scheme.ApiKeySecurityScheme!);
                break;
            case SecuritySchemeCase.HttpAuth:
                result.HttpAuthSecurityScheme = ToProto(scheme.HttpAuthSecurityScheme!);
                break;
            case SecuritySchemeCase.OAuth2:
                result.Oauth2SecurityScheme = ToProto(scheme.OAuth2SecurityScheme!);
                break;
            case SecuritySchemeCase.OpenIdConnect:
                result.OpenIdConnectSecurityScheme = ToProto(scheme.OpenIdConnectSecurityScheme!);
                break;
            case SecuritySchemeCase.Mtls:
                result.MtlsSecurityScheme = ToProto(scheme.MtlsSecurityScheme!);
                break;
            case SecuritySchemeCase.None:
            default:
                break;
        }

        return result;
    }

    public static SecurityScheme ToDomain(Protos.SecurityScheme scheme) => scheme.SchemeCase switch
    {
        Protos.SecurityScheme.SchemeOneofCase.ApiKeySecurityScheme => new SecurityScheme { ApiKeySecurityScheme = ToDomain(scheme.ApiKeySecurityScheme) },
        Protos.SecurityScheme.SchemeOneofCase.HttpAuthSecurityScheme => new SecurityScheme { HttpAuthSecurityScheme = ToDomain(scheme.HttpAuthSecurityScheme) },
        Protos.SecurityScheme.SchemeOneofCase.Oauth2SecurityScheme => new SecurityScheme { OAuth2SecurityScheme = ToDomain(scheme.Oauth2SecurityScheme) },
        Protos.SecurityScheme.SchemeOneofCase.OpenIdConnectSecurityScheme => new SecurityScheme { OpenIdConnectSecurityScheme = ToDomain(scheme.OpenIdConnectSecurityScheme) },
        Protos.SecurityScheme.SchemeOneofCase.MtlsSecurityScheme => new SecurityScheme { MtlsSecurityScheme = ToDomain(scheme.MtlsSecurityScheme) },
        _ => new SecurityScheme(),
    };

    public static Protos.APIKeySecurityScheme ToProto(ApiKeySecurityScheme scheme)
    {
        var result = new Protos.APIKeySecurityScheme
        {
            Name = scheme.Name,
            Location = scheme.Location,
        };

        if (scheme.Description is not null)
        {
            result.Description = scheme.Description;
        }

        return result;
    }

    public static ApiKeySecurityScheme ToDomain(Protos.APIKeySecurityScheme scheme) => new()
    {
        Name = scheme.Name,
        Location = scheme.Location,
        Description = NullIfEmpty(scheme.Description),
    };

    public static Protos.HTTPAuthSecurityScheme ToProto(HttpAuthSecurityScheme scheme)
    {
        var result = new Protos.HTTPAuthSecurityScheme
        {
            Scheme = scheme.Scheme,
        };

        if (scheme.Description is not null)
        {
            result.Description = scheme.Description;
        }

        if (scheme.BearerFormat is not null)
        {
            result.BearerFormat = scheme.BearerFormat;
        }

        return result;
    }

    public static HttpAuthSecurityScheme ToDomain(Protos.HTTPAuthSecurityScheme scheme) => new()
    {
        Scheme = scheme.Scheme,
        Description = NullIfEmpty(scheme.Description),
        BearerFormat = NullIfEmpty(scheme.BearerFormat),
    };

    public static Protos.OAuth2SecurityScheme ToProto(OAuth2SecurityScheme scheme)
    {
        var result = new Protos.OAuth2SecurityScheme
        {
            Flows = ToProto(scheme.Flows),
        };

        if (scheme.Description is not null)
        {
            result.Description = scheme.Description;
        }

        if (scheme.OAuth2MetadataUrl is not null)
        {
            result.Oauth2MetadataUrl = scheme.OAuth2MetadataUrl;
        }

        return result;
    }

    public static OAuth2SecurityScheme ToDomain(Protos.OAuth2SecurityScheme scheme) => new()
    {
        Flows = ToDomain(scheme.Flows),
        Description = NullIfEmpty(scheme.Description),
        OAuth2MetadataUrl = NullIfEmpty(scheme.Oauth2MetadataUrl),
    };

    public static Protos.OpenIdConnectSecurityScheme ToProto(OpenIdConnectSecurityScheme scheme)
    {
        var result = new Protos.OpenIdConnectSecurityScheme
        {
            OpenIdConnectUrl = scheme.OpenIdConnectUrl,
        };

        if (scheme.Description is not null)
        {
            result.Description = scheme.Description;
        }

        return result;
    }

    public static OpenIdConnectSecurityScheme ToDomain(Protos.OpenIdConnectSecurityScheme scheme) => new()
    {
        OpenIdConnectUrl = scheme.OpenIdConnectUrl,
        Description = NullIfEmpty(scheme.Description),
    };

    public static Protos.MutualTlsSecurityScheme ToProto(MutualTlsSecurityScheme scheme)
    {
        var result = new Protos.MutualTlsSecurityScheme();
        if (scheme.Description is not null)
        {
            result.Description = scheme.Description;
        }

        return result;
    }

    public static MutualTlsSecurityScheme ToDomain(Protos.MutualTlsSecurityScheme scheme) => new()
    {
        Description = NullIfEmpty(scheme.Description),
    };

    // ---- OAuthFlows (oneof) -------------------------------------------------------------------

    public static Protos.OAuthFlows ToProto(OAuthFlows flows)
    {
        var result = new Protos.OAuthFlows();
#pragma warning disable CS0612, CS0618 // Deprecated flows are still mapped for wire fidelity.
        switch (flows.FlowCase)
        {
            case OAuthFlowCase.AuthorizationCode:
                result.AuthorizationCode = ToProto(flows.AuthorizationCode!);
                break;
            case OAuthFlowCase.ClientCredentials:
                result.ClientCredentials = ToProto(flows.ClientCredentials!);
                break;
            case OAuthFlowCase.Implicit:
                result.Implicit = ToProto(flows.Implicit!);
                break;
            case OAuthFlowCase.Password:
                result.Password = ToProto(flows.Password!);
                break;
            case OAuthFlowCase.DeviceCode:
                result.DeviceCode = ToProto(flows.DeviceCode!);
                break;
            case OAuthFlowCase.None:
            default:
                break;
        }
#pragma warning restore CS0612, CS0618

        return result;
    }

    public static OAuthFlows ToDomain(Protos.OAuthFlows flows)
    {
#pragma warning disable CS0612, CS0618 // Deprecated flows are still mapped for wire fidelity.
        return flows.FlowCase switch
        {
            Protos.OAuthFlows.FlowOneofCase.AuthorizationCode => new OAuthFlows { AuthorizationCode = ToDomain(flows.AuthorizationCode) },
            Protos.OAuthFlows.FlowOneofCase.ClientCredentials => new OAuthFlows { ClientCredentials = ToDomain(flows.ClientCredentials) },
            Protos.OAuthFlows.FlowOneofCase.Implicit => new OAuthFlows { Implicit = ToDomain(flows.Implicit) },
            Protos.OAuthFlows.FlowOneofCase.Password => new OAuthFlows { Password = ToDomain(flows.Password) },
            Protos.OAuthFlows.FlowOneofCase.DeviceCode => new OAuthFlows { DeviceCode = ToDomain(flows.DeviceCode) },
            _ => new OAuthFlows(),
        };
#pragma warning restore CS0612, CS0618
    }

    public static Protos.AuthorizationCodeOAuthFlow ToProto(AuthorizationCodeOAuthFlow flow)
    {
        var result = new Protos.AuthorizationCodeOAuthFlow
        {
            AuthorizationUrl = flow.AuthorizationUrl,
            TokenUrl = flow.TokenUrl,
            PkceRequired = flow.PkceRequired ?? false,
        };

        if (flow.RefreshUrl is not null)
        {
            result.RefreshUrl = flow.RefreshUrl;
        }

        AddScopes(result.Scopes, flow.Scopes);
        return result;
    }

    public static AuthorizationCodeOAuthFlow ToDomain(Protos.AuthorizationCodeOAuthFlow flow) => new()
    {
        AuthorizationUrl = flow.AuthorizationUrl,
        TokenUrl = flow.TokenUrl,
        RefreshUrl = NullIfEmpty(flow.RefreshUrl),
        PkceRequired = flow.PkceRequired,
        Scopes = new Dictionary<string, string>(flow.Scopes),
    };

    public static Protos.ClientCredentialsOAuthFlow ToProto(ClientCredentialsOAuthFlow flow)
    {
        var result = new Protos.ClientCredentialsOAuthFlow
        {
            TokenUrl = flow.TokenUrl,
        };

        if (flow.RefreshUrl is not null)
        {
            result.RefreshUrl = flow.RefreshUrl;
        }

        AddScopes(result.Scopes, flow.Scopes);
        return result;
    }

    public static ClientCredentialsOAuthFlow ToDomain(Protos.ClientCredentialsOAuthFlow flow) => new()
    {
        TokenUrl = flow.TokenUrl,
        RefreshUrl = NullIfEmpty(flow.RefreshUrl),
        Scopes = new Dictionary<string, string>(flow.Scopes),
    };

    [Obsolete("Implicit flow is deprecated; mapped for wire fidelity.")]
    public static Protos.ImplicitOAuthFlow ToProto(ImplicitOAuthFlow flow)
    {
        var result = new Protos.ImplicitOAuthFlow
        {
            AuthorizationUrl = flow.AuthorizationUrl,
        };

        if (flow.RefreshUrl is not null)
        {
            result.RefreshUrl = flow.RefreshUrl;
        }

        AddScopes(result.Scopes, flow.Scopes);
        return result;
    }

    [Obsolete("Implicit flow is deprecated; mapped for wire fidelity.")]
    public static ImplicitOAuthFlow ToDomain(Protos.ImplicitOAuthFlow flow) => new()
    {
        AuthorizationUrl = flow.AuthorizationUrl,
        RefreshUrl = NullIfEmpty(flow.RefreshUrl),
        Scopes = new Dictionary<string, string>(flow.Scopes),
    };

    [Obsolete("Password flow is deprecated; mapped for wire fidelity.")]
    public static Protos.PasswordOAuthFlow ToProto(PasswordOAuthFlow flow)
    {
        var result = new Protos.PasswordOAuthFlow
        {
            TokenUrl = flow.TokenUrl,
        };

        if (flow.RefreshUrl is not null)
        {
            result.RefreshUrl = flow.RefreshUrl;
        }

        AddScopes(result.Scopes, flow.Scopes);
        return result;
    }

    [Obsolete("Password flow is deprecated; mapped for wire fidelity.")]
    public static PasswordOAuthFlow ToDomain(Protos.PasswordOAuthFlow flow) => new()
    {
        TokenUrl = flow.TokenUrl,
        RefreshUrl = NullIfEmpty(flow.RefreshUrl),
        Scopes = new Dictionary<string, string>(flow.Scopes),
    };

    public static Protos.DeviceCodeOAuthFlow ToProto(DeviceCodeOAuthFlow flow)
    {
        var result = new Protos.DeviceCodeOAuthFlow
        {
            DeviceAuthorizationUrl = flow.DeviceAuthorizationUrl,
            TokenUrl = flow.TokenUrl,
        };

        if (flow.RefreshUrl is not null)
        {
            result.RefreshUrl = flow.RefreshUrl;
        }

        AddScopes(result.Scopes, flow.Scopes);
        return result;
    }

    public static DeviceCodeOAuthFlow ToDomain(Protos.DeviceCodeOAuthFlow flow) => new()
    {
        DeviceAuthorizationUrl = flow.DeviceAuthorizationUrl,
        TokenUrl = flow.TokenUrl,
        RefreshUrl = NullIfEmpty(flow.RefreshUrl),
        Scopes = new Dictionary<string, string>(flow.Scopes),
    };

    private static void AddScopes(Google.Protobuf.Collections.MapField<string, string> target, Dictionary<string, string> source)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }
}
