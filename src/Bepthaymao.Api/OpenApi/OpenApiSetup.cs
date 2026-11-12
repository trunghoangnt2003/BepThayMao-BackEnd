using System.Text.Json.Serialization;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace Bepthaymao.Api.OpenApi;

internal static class OpenApiSetup
{
    private const string _bearerScheme = "bearerAuth";

    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        }).AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        }).AddOpenApi(options =>
        {
            options.Document.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Bepthaymao Ordering API",
                    Description = "API công khai nhận đơn đa cơ sở. Phiên bản nằm trong đường dẫn (/api/v1).",
                    Version = "1.0.0",
                    Contact = new OpenApiContact
                    {
                        Name = "Đội phát triển Bếp Thầy Mão",
                        Url = new Uri("https://github.com/thanglm240723/BepThayMao-Spec"),
                    },
                };
                document.Servers = [new OpenApiServer { Url = "/" }];
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
                document.Components.SecuritySchemes[_bearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "JWT access token lấy từ /api/v1/auth/login.",
                };
                return Task.CompletedTask;
            });
            options.Document.AddOperationTransformer((operation, context, _) =>
            {
                IList<object> metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(_bearerScheme, context.Document)] = [],
                    });
                }

                return Task.CompletedTask;
            });
            options.Document.AddSchemaTransformer((schema, context, _) =>
            {
                if (context.JsonTypeInfo.Type == typeof(ProblemDetails)
                    || context.JsonTypeInfo.Type == typeof(HttpValidationProblemDetails))
                {
                    schema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
                    schema.Properties["correlationId"] = new OpenApiSchema { Type = JsonSchemaType.String };
                    schema.Properties["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String };
                    schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
                    schema.Required.Add("correlationId");
                }

                return Task.CompletedTask;
            });
        });
        return services;
    }
}
