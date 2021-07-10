namespace VirtoCommerce.Storefront.Tests.Features
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    using Microsoft.Extensions.DependencyInjection;

    using Newtonsoft.Json.Linq;

    using VirtoCommerce.Storefront.Model.Features;

    using Xunit;

    public class FeaturesAgentTests
    {
        [Theory]
        [InlineData("OrdersBrowsing", true)]
        [InlineData("InvoiceBrowsing", true)]
        [InlineData("PaymentBrowsing", true)]
        [InlineData("ManageUsers", true)]
        [InlineData("ManageRoles", true)]
        [InlineData("ContractsInfoBrowsing", true)]
        [InlineData("RetrieveReordering", false)]
        [InlineData("OrderApproval", false)]
        [InlineData("ProductTextSearch", true)]
        [InlineData("ProductsFiltering", true)]
        [InlineData("ProductDetailsBrowsing", true)]
        [InlineData("ProductPriceBrowsing", true)]
        [InlineData("ProductRecommendation", true)]
        [InlineData("OrderDraft", true)]
        [InlineData("WishList", true)]
        [InlineData("SubmitOrder", false)]
        [InlineData("ProductInventoryBrowsing", false)]
        [InlineData("ManageShipmentDetails", false)]
        [InlineData("ManagePaymentDetails", false)]
        public void IsActive_FeatureStatus_ShouldBeExpected(string featureName, bool expectedResult)
        {
            // arrange
            var services = new CustomServiceCollection();
            var settingsJObject = ReadSettingsFile("full_data.json");

            using (var serviceProvider = services.BuildServiceProvider())
            {
                // act
                var featuresAgent = serviceProvider.GetService<IFeaturesAgent>();
                var result = featuresAgent.IsActive(featureName, settingsJObject);

                // assert
                Assert.Equal(expectedResult, result);
            }
        }

        [Theory]
        [InlineData("RetrieveReordering", true)]
        [InlineData("OrderApproval", true)]
        public void IsActive_FeatureWithoutReplaces_ShouldBeActive(string featureName, bool expectedResult)
        {
            // arrange
            var services = new CustomServiceCollection();

            // in this sample were removed services from 'replaces' sections
            var settingsJObject = ReadSettingsFile("full_data_without_replaces.json");

            using (var serviceProvider = services.BuildServiceProvider())
            {
                // act
                var featuresAgent = serviceProvider.GetService<IFeaturesAgent>();
                var result = featuresAgent.IsActive(featureName, settingsJObject);

                // assert
                Assert.Equal(expectedResult, result);
            }
        }

        [Theory]
        [InlineData("RetrieveReordering", false)]
        [InlineData("OrderApproval", false)]
        public void IsActive_FeatureWithConflicts_ShouldBeInactive(string featureName, bool expectedResult)
        {
            // arrange
            var services = new CustomServiceCollection();

            // in this sample were removed services from 'replaces' sections
            var settingsJObject = ReadSettingsFile("full_data_with_conflicts.json");

            using (var serviceProvider = services.BuildServiceProvider())
            {
                // act
                var featuresAgent = serviceProvider.GetService<IFeaturesAgent>();
                var result = featuresAgent.IsActive(featureName, settingsJObject);

                // assert
                Assert.Equal(expectedResult, result);
            }
        }

        [Fact]
        public void IsActive_OrdersBrowsing_ShouldBeInactive()
        {
            // arrange
            var services = new CustomServiceCollection();

            // in this sample were removed services from 'replaces' sections
            var settingsJObject = ReadSettingsFile("full_data_with_disabled_feature.json");

            using (var serviceProvider = services.BuildServiceProvider())
            {
                // act
                var featuresAgent = serviceProvider.GetService<IFeaturesAgent>();
                var result = featuresAgent.IsActive("OrdersBrowsing", settingsJObject);

                // assert
                Assert.False(result);
            }
        }

        private static IDictionary<string, object> ReadSettingsFile(string defaultFileName = "test_data.json")
        {
            var currentDirectory = Environment.CurrentDirectory;
            var path = Path.Combine(currentDirectory, "Features", "Samples", defaultFileName);
            var fileInfo = new FileInfo(path);

            try
            {
                var text = File.ReadAllText(fileInfo.FullName);
                return JObject.Parse(text).ToObject<Dictionary<string, object>>();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}
