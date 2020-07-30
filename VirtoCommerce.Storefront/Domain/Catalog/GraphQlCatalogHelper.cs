using System.Linq;
using AutoRest.Core.Utilities;
using VirtoCommerce.Storefront.Model.Catalog;
using VirtoCommerce.Storefront.Model.Services;

namespace VirtoCommerce.Storefront.Domain.Catalog
{
    public static class GraphQlCatalogHelper
    {
        public static string GetAllProductFields
        => $@"
            id
            code
            category {{ {AllCategoryFields} parent {{ {AllCategoryFields} }}
            }}
            catalogId
            name
            metaTitle
            metaDescription
            metaKeywords
            brandName
            slug
            imgSrc
            productType
            descriptions {{
                content
                id
                languageCode
                reviewType
            }}
            masterVariation {{ { GetAllVariationFields } }}
            associations {{
                items {{
                    associatedObjectId
                    product {{ id }}
                    quantity
                }}
                totalCount
            }}
            variations {{ { GetAllVariationFields } }}
            ";

        public static string GetAllVariationFields
        => $@"id
            code
            images {{ id url name }}
            assets {{ id size url }}
            prices {{
                list {{ {MoneyFields} }}
                listWithTax {{ {MoneyFields} }}
                sale {{ {MoneyFields} }}
                saleWithTax {{ {MoneyFields} }}
                minQuantity
                pricelistId
                tierPrices {{
                    price {{ {MoneyFields} }}
                    priceWithTax {{ {MoneyFields} }}
                    quantity
                }}
                validFrom
                validUntil
                currency
                discounts {{
                    coupon
                    description
                    promotion {{
                        id
                        description
                        name
                        type
                    }}
                    promotionId
                    amount {{ {MoneyFields} }}
                    amountWithTax {{ {MoneyFields} }}
                }}
            }}
            availabilityData {{
                availableQuantity
                inventories {{ inStockQuantity fulfillmentCenterId fulfillmentCenterName allowPreorder allowBackorder }}
            }}
            properties {{ {PropertyFields} }}
            { OutlineFields }
            ";

        public const string MoneyFields = "amount decimalDigits formattedAmount formattedAmountWithoutPoint formattedAmountWithoutCurrency formattedAmountWithoutPointAndCurrency";
        public const string PropertyFields = "id name valueType value valueId label hidden";
        public static string AllCategoryFields = $"id code name hasParent slug {OutlineFields}";
        public const string OutlineFields = "outlines { items { id name seoObjectType }}";
        public static string AllFacets = $"{ FilterFacets } { RangeFacets } { TermFacets }";
        public const string FilterFacets = "filter_facets { count facetType name }";
        public const string TermFacets = "range_facets { facetType name ranges { count from fromStr includeFrom includeTo max min to toStr total } }";
        public const string RangeFacets = "term_facets { facetType name terms { count isSelected term } }";

        public static string GetProducts(this ICatalogService catalogService, string[] ids, string storeId, string userId, string selectedFields = null)
        => $@"
        {{
            products(
                productIds: [{string.Join(',', ids.Select(x => $"\"{x}\""))}]
                storeId: ""{storeId}""
                userId: ""{userId}""
            )
            {{
                items {{ { selectedFields ?? GetAllProductFields } }}
            }}
        }}
        ";

        public static string SearchProducts(
            this ICatalogService catalogService,
            ProductSearchCriteria criteria,
            string cultureName,
            string currencyCode,
            string customerId,
            string storeId,
            string catalogId
            )
            => $@"
            {{
                products(
                    query: ""{ criteria.Keyword }""
                    filter: ""{
                        ( string.IsNullOrEmpty(criteria.Outline) ? string.Empty : $"categories.subtree:{ criteria.Outline }" ) }{
                        ( criteria.Terms.IsNullOrEmpty() ? string.Empty : $" {string.Join(' ', criteria.Terms.Select(x => $"{x.Name}:{x.Value}"))}" ) }{
                        (string.IsNullOrEmpty(catalogId) ? string.Empty : $" catalog:{ catalogId }")
                    }""
                    fuzzy: { criteria.IsFuzzySearch.ToString().ToLowerInvariant() }
                    userId: ""{ customerId }""
                    currencyCode: ""{ currencyCode }""
                    cultureName: ""{ cultureName }""
                    sort: ""{ criteria.SortBy }""
                    storeId: ""{ storeId }""
                )
                {{
                    { AllFacets }
                    items {{ { GetAllProductFields } }}
                    totalCount
                }}
            }}
            ";

        public static string SearchCategories(
            this ICatalogService catalogService,
            CategorySearchCriteria criteria,
            string storeId,
            string cultureName,
            string currencyCode,
            string customerId,
            string catalogId
            )
            => $@"
            {{
                categories(
                    query: ""{ criteria.Keyword }""
                    filter: ""{
                        (string.IsNullOrEmpty(criteria.Outline) ? string.Empty : $"categories.subtree:{ criteria.Outline }") }{
                        (string.IsNullOrEmpty(catalogId) ? string.Empty : $" catalog:{catalogId}")
                    }""
                    fuzzy: { criteria.IsFuzzySearch.ToString().ToLowerInvariant() }
                    storeId: ""{ storeId }""
                    cultureName: ""{ cultureName }""
                    sort: ""{ criteria.SortBy }""
                    userId: ""{ customerId }""
                    currencyCode: ""{ currencyCode }""
                )
                {{
                    items {{
                        { AllCategoryFields }
                        parent {{
                            { AllCategoryFields }
                        }}
                    }}
                    { AllFacets }
                }}
            }}
            ";

        public static string GetCategoriesQuery(this ICatalogService catalogService, string[] ids, string storeId, string userId, string selectedFields = null)
        => $@"
        {{
            categories(
                categoryIds: [{string.Join(',', ids.Select(x => $"\"{x}\""))}]
                storeId: ""{storeId}""
                userId: ""{userId}""
            )
            {{
                items {{ { selectedFields ?? AllCategoryFields } parent {{ { AllCategoryFields } }} }}
            }}
        }}
        ";
    }
}
