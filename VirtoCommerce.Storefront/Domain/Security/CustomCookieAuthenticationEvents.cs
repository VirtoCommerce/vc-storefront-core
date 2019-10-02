using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using VirtoCommerce.Storefront.Extensions;
using VirtoCommerce.Storefront.Model.Common;

namespace VirtoCommerce.Storefront.Domain.Security
{
    public class CustomCookieAuthenticationEvents : CookieAuthenticationEvents
    {
        private readonly IStorefrontUrlBuilder _storefrontUrlBuilder;
        public CustomCookieAuthenticationEvents(IStorefrontUrlBuilder storefrontUrlBuilder)
        {
            _storefrontUrlBuilder = storefrontUrlBuilder;
        }
        public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
        {
            if (context.Request.Path.IsApi())
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                return Task.CompletedTask;
            }
            context.RedirectUri = AdjustRedirectUri(context.RedirectUri);
            return base.RedirectToLogin(context);
        }

        public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
        {
            if (context.Request.Path.IsApi())
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                return Task.CompletedTask;
            }
            context.RedirectUri = AdjustRedirectUri(context.RedirectUri);
            return base.RedirectToAccessDenied(context);
        }

        /// <summary>
        /// add store and lng segments of current store to uri
        /// </summary>
        /// <param name="uri"></param>
        /// <returns></returns>
        private string AdjustRedirectUri(string uri)
        {
            var redirectUri = new Uri(uri);
            var adjustedAbsolutePath = _storefrontUrlBuilder.ToAppAbsolute(redirectUri.AbsolutePath);
            var builder = new UriBuilder(redirectUri);
            builder.Path = adjustedAbsolutePath;
            string result = builder.Uri.ToString();
            return result;
        }

    }
}
