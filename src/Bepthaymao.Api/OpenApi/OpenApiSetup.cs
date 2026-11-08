using System.Text.Json.Serialization;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace Bepthaymao.Api.OpenApi;

internal static class OpenApiSetup
{
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
                document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
                if (!document.Components.Schemas.ContainsKey("ProblemDetails"))
                {
                    // The versioning adapter does not expose a keyed schema service for an empty document.
                    OpenApiSchema problem = new()
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
                        {
                            ["type"] = new OpenApiSchema { Type = JsonSchemaType.String },
                            ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
                            ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                            ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String },
                            ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String },
                            ["correlationId"] = new OpenApiSchema { Type = JsonSchemaType.String },
                            ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String },
                        },
                        Required = new HashSet<string>(["title", "status", "correlationId"], StringComparer.Ordinal),
                    };
                    document.Components.Schemas.TryAdd("ProblemDetails", problem);
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
                    schema.Required.Add("title");
                    schema.Required.Add("status");
                    if (schema.Properties.TryGetValue("title", out IOpenApiSchema? title) && title is OpenApiSchema titleSchema)
                    {
                        titleSchema.Type = JsonSchemaType.String;
                    }
                    if (schema.Properties.TryGetValue("status", out IOpenApiSchema? status) && status is OpenApiSchema statusSchema)
                    {
                        statusSchema.Type = JsonSchemaType.Integer;
                    }
                }

                return Task.CompletedTask;
            });
        });
        return services;
    }
}
