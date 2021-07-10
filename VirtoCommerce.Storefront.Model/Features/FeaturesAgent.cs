namespace VirtoCommerce.Storefront.Model.Features
{
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json.Linq;
    using VirtoCommerce.Storefront.Model.Features.Exceptions;

    public class FeaturesAgent : IFeaturesAgent
    {
        private readonly string _featuresBranchToken;

        public FeaturesAgent(string featuresBranchToken = "features")
        {
            _featuresBranchToken = featuresBranchToken;
        }

        public bool IsActive(string featureName, IDictionary<string, object> settings)
        {
            var features = GetFeatures(settings);

            var feature = features.GetFeature(featureName);

            if (!feature.IsActive)
            {
                return false;
            }

            bool result;
            var replaced = features.AnyReplacedBy(feature.Name);
            var conflict = features.AnyConflictWith(feature.Name);

            if (replaced || conflict)
            {
                result = false;
            }
            else
            {
                var referenceFeatures = feature.GetReferenceFeatures(features);
                result = !referenceFeatures.AnyInactive();
            }

            return result;
        }

        private List<Feature> GetFeatures(IDictionary<string, object> settings)
        {
            List<Feature> result;

            try
            {
                var featuresJson = settings[_featuresBranchToken] as JArray;

                if (featuresJson == null)
                {
                    throw new FeaturesException($"Can't find \"{_featuresBranchToken}\" section ");
                }

                result = featuresJson.ToObject<List<Feature>>();
            }
            catch (Exception exception)
            {
                throw new FeaturesException(exception.Message, exception);
            }

            return result;
        }
    }
}
