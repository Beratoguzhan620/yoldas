using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Yoldas.Api.Common;

/// <summary>The landing text of the API reference.</summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Yoldas",
            Version = "1.0",
            Description = """
                Ride sharing between cities: drivers offer the empty seats on a trip they are making anyway, and
                passengers join anywhere along the route. Trips passing near a place are found with PostGIS; the
                part of the route a passenger rides decides what they pay.

                **Getting started**

                1. Create an account at `POST /api/auth/register` and paste the access token into *Authentication*.
                2. Add your car at `PUT /api/me/vehicle`, then offer a trip at `POST /api/trips` with its stops.
                3. Search at `GET /api/trips` with where you are and where you are going, which needs no account.

                **Errors** follow RFC 9457 problem details with a stable `code`, for example `validation_failed`
                or `invalid_state`. Every response carries an `X-Correlation-Id`.
                """,
            License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://github.com/Beratoguzhan620/yoldas/blob/main/LICENSE") },
        };
        return Task.CompletedTask;
    }
}
