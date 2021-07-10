using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace VirtoCommerce.Storefront.Infrastructure.Swagger
{
    public class NameAndOrderRequestBodyFilter: IRequestBodyFilter
    {
        private readonly SwaggerOptions _swaggerOptions;

        public NameAndOrderRequestBodyFilter(IOptions<SwaggerOptions> swaggerOptions)
        {
            _swaggerOptions = swaggerOptions.Value;
        }

        public void Apply(OpenApiRequestBody requestBody, RequestBodyFilterContext context)
        {
            // Even if it's v2 or request body shouldn't be named
            // we want name it because of bugs and different approaches in generation tools
            var bodyName = _swaggerOptions.Schema.NameAndOrderRequestBody
                ? context.BodyParameterDescription.Name
                : "body";
            requestBody.Extensions.Add("x-name", new OpenApiString(bodyName));
            requestBody.Extensions.Add("x-codegen-request-body-name", new OpenApiString(bodyName));
            requestBody.Extensions.Add("x-ms-requestBody-name", new OpenApiString(bodyName));

            if (_swaggerOptions.Schema.NameAndOrderRequestBody)
            {
                var bodyPosition = context.BodyParameterDescription.ParameterInfo().Position;
                requestBody.Extensions.Add("x-position", new OpenApiInteger(bodyPosition));
                requestBody.Extensions.Add("x-ms-requestBody-index", new OpenApiInteger(bodyPosition));
            }
        }
    }
}
