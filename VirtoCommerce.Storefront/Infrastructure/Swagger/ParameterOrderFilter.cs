using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace VirtoCommerce.Storefront.Infrastructure.Swagger
{
    public class ParameterOrderFilter: IParameterFilter
    {
        private readonly SwaggerOptions _swaggerOptions;

        public ParameterOrderFilter(IOptions<SwaggerOptions> swaggerOptions)
        {
            _swaggerOptions = swaggerOptions.Value;
        }

        public void Apply(OpenApiParameter parameter, ParameterFilterContext context)
        {
            if (_swaggerOptions.Schema.NameAndOrderRequestBody)
            {
                // Explicitly specify parameters position
                // Position for store and language is unknown and they should be last, so use int.MaxValue relative values
                if (context.ParameterInfo != null)
                {
                    parameter.Extensions.Add("x-position", new OpenApiInteger(context.ParameterInfo.Position));
                }
                else
                {
                    switch (parameter.Name)
                    {
                        case "store":
                            parameter.Extensions.Add("x-position", new OpenApiInteger(int.MaxValue - 1));
                            break;
                        case "language":
                            parameter.Extensions.Add("x-position", new OpenApiInteger(int.MaxValue));
                            break;
                    }
                }
            }
        }
    }
}
