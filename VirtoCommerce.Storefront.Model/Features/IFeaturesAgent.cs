using System.Collections.Generic;

namespace VirtoCommerce.Storefront.Model.Features
{
    public interface IFeaturesAgent
    {
        bool IsActive(string featureName, IDictionary<string, object> settings);
    }
}
