using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotLiquid.ViewEngine.Exceptions;
using LibSassHost;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using VirtoCommerce.LiquidThemeEngine.Scriban;
using VirtoCommerce.Storefront.Model;
using VirtoCommerce.Storefront.Model.Caching;
using VirtoCommerce.Storefront.Model.Common;
using VirtoCommerce.Storefront.Model.Common.Caching;
using VirtoCommerce.Storefront.Model.Common.Exceptions;
using VirtoCommerce.Storefront.Model.Features;
using VirtoCommerce.Storefront.Model.StaticContent;

namespace VirtoCommerce.LiquidThemeEngine
{
    /// <summary>
    /// Shopify compliant theme folder structure and all methods for rendering
    /// assets - storages for css, images and other assets
    /// config - contains theme configuration
    /// layout - master pages and layouts
    /// locales - localization resources
    /// snippets - snippets - partial views
    /// templates - view templates
    /// </summary>
    public class ShopifyLiquidThemeEngine : ILiquidThemeEngine, ITemplateLoader
    {
        private readonly LiquidThemeEngineOptions _options;
        private static readonly Regex _isLiquid = new Regex("[{}|]", RegexOptions.Compiled);
        private const string _liquidTemplateFormat = "{0}.liquid";
        private readonly IWorkContextAccessor _workContextAccessor;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IStorefrontMemoryCache _memoryCache;
        private readonly IContentBlobProvider _themeBlobProvider;
        private readonly ISassFileManager _sassFileManager;
        private readonly IFeaturesAgent _featuresAgent;

        public ShopifyLiquidThemeEngine(IStorefrontMemoryCache memoryCache, IWorkContextAccessor workContextAccessor, IHttpContextAccessor httpContextAccessor,
                                        IStorefrontUrlBuilder storeFrontUrlBuilder, IContentBlobProvider contentBlobProvider, ISassFileManager sassFileManager, IOptions<LiquidThemeEngineOptions> options, IFeaturesAgent featuresAgent)
        {
            _workContextAccessor = workContextAccessor;
            _httpContextAccessor = httpContextAccessor;
            UrlBuilder = storeFrontUrlBuilder;
            _options = options.Value;
            _memoryCache = memoryCache;
            _themeBlobProvider = contentBlobProvider;
            _sassFileManager = sassFileManager;
            _featuresAgent = featuresAgent;
            SassCompiler.FileManager = sassFileManager;
        }

        /// <summary>
        /// Main work context
        /// </summary>
        public WorkContext WorkContext => _workContextAccessor.WorkContext;

        /// <summary>
        /// Current HttpContext
        /// </summary>
        public HttpContext HttpContext => _httpContextAccessor.HttpContext;

        /// <summary>
        /// Store url builder
        /// </summary>
        public IStorefrontUrlBuilder UrlBuilder { get; }

        /// <summary>
        /// Default master view name
        /// </summary>
        public string MasterViewName => _options.DefaultLayout;

        /// <summary>
        /// Current theme name
        /// </summary>
        public string CurrentThemeName => !string.IsNullOrEmpty(WorkContext.CurrentStore.ThemeName) ? WorkContext.CurrentStore.ThemeName : "default";

        public string CurrentThemeSettingPath => Path.Combine(CurrentThemePath, "config", GetSettingsFilePath());
        public string CurrentThemeLocalePath => Path.Combine(CurrentThemePath, "locales");
        /// <summary>
        /// The path for current theme 
        /// </summary>
        private string CurrentThemePath => Path.Combine("Themes", WorkContext.CurrentStore.Id, CurrentThemeName);

        //Relative path to the discovery of theme resources that weren't found by the current path.
        private string BaseThemePath =>
            !string.IsNullOrEmpty(_options.BaseThemePath) ? Path.Combine("Themes", _options.BaseThemePath) :
#pragma warning disable 618
            // We need to use obsolete value here for backward compatibility.
            !string.IsNullOrEmpty(_options.BaseThemeName) ? Path.Combine("Themes", _options.BaseThemeName, "default") : null;
#pragma warning restore 618
        private string BaseThemeSettingPath => BaseThemePath != null ? Path.Combine(BaseThemePath, "config", "settings_data.json") : null;
        public string BaseThemeLocalePath => BaseThemePath != null ? Path.Combine(BaseThemePath, "locales") : null;


        #region ITemplateLoader members
        public string GetPath(TemplateContext context, SourceSpan callerSpan, string templateName)
        {
            return ResolveTemplatePath(templateName);
        }

        public string Load(TemplateContext context, SourceSpan callerSpan, string templatePath)
        {
            var content = ReadTemplateByPath(templatePath);
            return content;
        }

        public ValueTask<string> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
        {
            return new ValueTask<string>(Load(context, callerSpan, templatePath));
        }

        #endregion

        #region ILiquidThemeEngine Members
        public IEnumerable<string> DiscoveryPaths
        {
            get
            {
                var retVal = Enumerable.Empty<string>();
                if (WorkContext.CurrentStore != null)
                {
                    retVal = _options.TemplatesDiscoveryFolders.Select(x => Path.Combine(CurrentThemePath, x));
                    if (BaseThemePath != null)
                    {
                        retVal = retVal.Concat(_options.TemplatesDiscoveryFolders.Select(x => Path.Combine(BaseThemePath, x)));
                    }
                }
                return retVal;
            }
        }

        /// <summary>
        /// Return stream for requested  asset file  (used for search current and base themes assets)
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public async Task<Stream> GetAssetStreamAsync(string filePath)
        {
            Stream retVal = null;
            var filePathWithoutExtension = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath));
            //file.ext => file.ext || file.liquid || file.ext.liquid || file     
            var searchPatterns = new[] { filePath, string.Format(_liquidTemplateFormat, filePathWithoutExtension), string.Format(_liquidTemplateFormat, filePath), filePathWithoutExtension };


            string currentThemeFilePath = null;
            //try to search in current store theme 
            if (_themeBlobProvider.PathExists(Path.Combine(CurrentThemePath, "assets")))
            {
                currentThemeFilePath = searchPatterns.SelectMany(x => _themeBlobProvider.Search(Path.Combine(CurrentThemePath, "assets"), x, true)).FirstOrDefault();
            }
            //If not found by current theme path try find them by base path if it is defined
            if (currentThemeFilePath == null && BaseThemePath != null)
            {
                currentThemeFilePath = searchPatterns.SelectMany(x => _themeBlobProvider.Search(Path.Combine(BaseThemePath, "assets"), x, true)).FirstOrDefault();
            }

            if (currentThemeFilePath != null)
            {
                retVal = _themeBlobProvider.OpenRead(currentThemeFilePath);
                filePath = currentThemeFilePath;
            }

            if (retVal != null && filePath.EndsWith(".liquid"))
            {
                var context = WorkContext.Clone() as WorkContext;
                context.Settings = GetSettings("''");
                var templateContent = retVal.ReadToString();
                retVal.Dispose();

                var template = await RenderTemplateAsync(templateContent, filePath, context.ToScriptObject());
                retVal = new MemoryStream(Encoding.UTF8.GetBytes(template));
            }

            if (retVal != null && (filePath.Contains(".scss.") && filePath.EndsWith(".liquid") || filePath.EndsWith(".scss")))
            {
                var content = retVal.ReadToString();
                retVal.Dispose();

                try
                {
                    //handle scss resources
                    _sassFileManager.CurrentDirectory = Path.GetDirectoryName(filePath);
                    var result = SassCompiler.Compile(content);
                    content = result.CompiledContent;

                    retVal = new MemoryStream(Encoding.UTF8.GetBytes(content));
                }
                catch (Exception ex)
                {
                    throw new SaasCompileException(filePath, content, ex);
                }
            }

            return retVal;
        }


        /// <summary>
        /// Return hash of requested asset (used for file versioning)
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public string GetAssetHash(string filePath)
        {
            var cacheKey = CacheKey.With(GetType(), "GetAssetHash", filePath);
            return _memoryCache.GetOrCreateExclusive(cacheKey, (cacheEntry) =>
            {
                cacheEntry.AddExpirationToken(new CompositeChangeToken(new[] { ThemeEngineCacheRegion.CreateChangeToken(), _themeBlobProvider.Watch(filePath), _themeBlobProvider.Watch(CurrentThemeSettingPath) }));

                using (var stream = GetAssetStreamAsync(filePath).GetAwaiter().GetResult())
                {
                    if (stream == null)
                    {
                        throw new StorefrontException($"Theme resource for path '{filePath}' not found");
                    }
                    var hashAlgorithm = CryptoConfig.AllowOnlyFipsAlgorithms ? (SHA256)new SHA256CryptoServiceProvider() : new SHA256Managed();
                    return WebEncoders.Base64UrlEncode(hashAlgorithm.ComputeHash(stream));
                }
            });
        }

        /// <summary>
        /// resolve  template path by it name
        /// </summary>
        /// <param name="templateName"></param>
        /// <returns></returns>
        public string ResolveTemplatePath(string templateName)
        {
            if (WorkContext.CurrentStore == null)
            {
                return null;
            }

            var liquidTemplateFileName = string.Format(_liquidTemplateFormat, templateName);
            //If not found by current theme path try find them by base path if it is defined
            var curentThemeDiscoveryPaths = _options.TemplatesDiscoveryFolders.Select(x => Path.Combine(CurrentThemePath, x, liquidTemplateFileName));
            if (BaseThemePath != null)
            {
                curentThemeDiscoveryPaths = curentThemeDiscoveryPaths.Concat(_options.TemplatesDiscoveryFolders.Select(x => Path.Combine(BaseThemePath, x, liquidTemplateFileName)));
            }
            //Try to find template in current theme folder
            return curentThemeDiscoveryPaths.FirstOrDefault(x => _themeBlobProvider.PathExists(x));

        }

        /// <summary>
        /// Render template by name and with passed context (parameters)
        /// </summary>
        /// <param name="templateName"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public async ValueTask<string> RenderTemplateByNameAsync(string templateName, object context)
        {
            if (string.IsNullOrEmpty(templateName))
            {
                throw new ArgumentNullException(nameof(templateName));
            }

            var templatePath = ResolveTemplatePath(templateName);
            if (string.IsNullOrEmpty(templatePath))
            {
                throw new FileNotFoundException($"The template '{templateName}' was not found. The following locations were searched:<br/>{string.Join("<br/>", DiscoveryPaths)}");
            }
            var templateContent = ReadTemplateByPath(templatePath);
            var retVal = await RenderTemplateAsync(templateContent, templatePath, context);
            return retVal;
        }

        /// <summary>
        /// Render template by content and parameters
        /// </summary>
        /// <param name="templateContent"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public ValueTask<string> RenderTemplateAsync(string templateContent, string templatePath, object context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (!(context is IScriptObject scriptObject))
            {
                throw new StorefrontException($"{ nameof(context) } must implement IScriptObject");
            }

            if (string.IsNullOrEmpty(templateContent))
            {
                return new ValueTask<string>(templateContent);
            }

            var isLiquidTemplate = _isLiquid.Match(templateContent);
            if (!isLiquidTemplate.Success)
            {
                return new ValueTask<string>(templateContent);
            }

            //TODO: Handle _options.RethrowLiquidRenderErrors
            var cacheKey = CacheKey.With(GetType(), "ParseTemplate", templatePath ?? templateContent);
            var parsedTemplate = _memoryCache.GetOrCreate(cacheKey, (cacheItem) =>
            {
                if (!string.IsNullOrEmpty(templatePath))
                {
                    cacheItem.AddExpirationToken(new CompositeChangeToken(new[] { ThemeEngineCacheRegion.CreateChangeToken(), _themeBlobProvider.Watch(templatePath) }));
                }
                else
                {
                    cacheItem.AddExpirationToken(ThemeEngineCacheRegion.CreateChangeToken());
                }
                return Template.ParseLiquid(templateContent, templatePath);
            });

            if (parsedTemplate.HasErrors)
            {
                throw new InvalidOperationException(string.Join("\n", parsedTemplate.Messages));
            }


            var templateContext = new TemplateContext()
            {
                TemplateLoader = this,
                EnableRelaxedMemberAccess = true,
                NewLine = Environment.NewLine,
                TemplateLoaderLexerOptions = new LexerOptions
                {
                    Mode = ScriptMode.Liquid
                }
            };
            templateContext.PushGlobal(scriptObject);

            var result = parsedTemplate.Render(templateContext);
            return new ValueTask<string>(result);
        }

        /// <summary>
        /// Read shopify theme settings from 'config' folder
        /// </summary>
        /// <param name="defaultValue"></param>
        /// <returns></returns>
        public IDictionary<string, object> GetSettings(string defaultValue = null)
        {
            var cacheKey = CacheKey.With(GetType(), "GetSettings", CurrentThemeSettingPath, defaultValue);
            return _memoryCache.GetOrCreateExclusive(cacheKey, cacheItem =>
            {
                cacheItem.AddExpirationToken(new CompositeChangeToken(new[] { ThemeEngineCacheRegion.CreateChangeToken(), _themeBlobProvider.Watch(CurrentThemeSettingPath) }));

                JObject result;
                var baseThemeSettings = new JObject();
                var currentThemeSettings = result = InnerGetAllSettings(_themeBlobProvider, CurrentThemeSettingPath);

                //Try to load settings from base theme path and merge them with resources for local theme
                if ((_options.MergeBaseSettings || currentThemeSettings == null) && !string.IsNullOrEmpty(BaseThemeSettingPath))
                {
                    cacheItem.AddExpirationToken(new CompositeChangeToken(new[] { ThemeEngineCacheRegion.CreateChangeToken(), _themeBlobProvider.Watch(BaseThemeSettingPath) }));
                    baseThemeSettings = InnerGetAllSettings(_themeBlobProvider, BaseThemeSettingPath);
                }

                result = _options.MergeBaseSettings
                    ? SettingsManager.Merge(baseThemeSettings, currentThemeSettings ?? new JObject())
                    : SettingsManager.ReadSettings(currentThemeSettings ?? new JObject()).CurrentPreset.Json;

                return result.ToObject<Dictionary<string, object>>().ToDictionary(x => x.Key, x => x.Value).WithDefaultValue(defaultValue);
            });
        }


        /// <summary>
        /// Read localization resources 
        /// </summary>
        /// <returns></returns>
        public JObject ReadLocalization()
        {
            var cacheKey = CacheKey.With(GetType(), "ReadLocalization", CurrentThemeLocalePath, WorkContext.CurrentLanguage.CultureName);
            return _memoryCache.GetOrCreateExclusive(cacheKey, (cacheItem) =>
            {
                var result = new JObject();
                cacheItem.AddExpirationToken(new CompositeChangeToken(new[] { ThemeEngineCacheRegion.CreateChangeToken(), _themeBlobProvider.Watch(CurrentThemeLocalePath + "/*") }));
                //Try to load localization resources from base theme path and merge them with resources for local theme
                if (BaseThemeLocalePath != null)
                {
                    cacheItem.AddExpirationToken(new CompositeChangeToken(new[] { ThemeEngineCacheRegion.CreateChangeToken(), _themeBlobProvider.Watch(BaseThemeLocalePath + "/*") }));
                    result = InnerReadLocalization(_themeBlobProvider, BaseThemeLocalePath, WorkContext.CurrentLanguage) ?? new JObject();
                }
                result.Merge(InnerReadLocalization(_themeBlobProvider, CurrentThemeLocalePath, WorkContext.CurrentLanguage) ?? new JObject(), new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Merge });
                return result;

            });
        }

        /// <summary>
        /// Get relative url for assets (assets folder)
        /// </summary>
        /// <param name="assetName"></param>
        /// <returns></returns>
        public string GetAssetAbsoluteUrl(string assetName)
        {
            return UrlBuilder.ToAppAbsolute(_options.ThemesAssetsRelativeUrl.TrimEnd('/') + "/" + assetName.TrimStart('/'), WorkContext.CurrentStore, WorkContext.CurrentLanguage);
        }

        #endregion

        private static JObject InnerReadLocalization(IContentBlobProvider themeBlobProvider, string localePath, Language language)
        {
            JObject retVal = null;

            if (themeBlobProvider.PathExists(localePath))
            {
                JObject localeJson = null;
                JObject defaultJson = null;

                foreach (var languageName in new[] { language.CultureName, language.TwoLetterLanguageName })
                {
                    var currentLocalePath = Path.Combine(localePath, string.Concat(languageName, ".json"));

                    if (themeBlobProvider.PathExists(currentLocalePath))
                    {
                        using (var stream = themeBlobProvider.OpenRead(currentLocalePath))
                        {
                            localeJson = JsonConvert.DeserializeObject<dynamic>(stream.ReadToString());
                        }
                        break;
                    }
                }

                var localeDefaultPath = themeBlobProvider.Search(localePath, "*.default.json", false).FirstOrDefault();

                if (localeDefaultPath != null && themeBlobProvider.PathExists(localeDefaultPath))
                {
                    using (var stream = themeBlobProvider.OpenRead(localeDefaultPath))
                    {
                        defaultJson = JsonConvert.DeserializeObject<dynamic>(stream.ReadToString());
                    }
                }

                //Need merge default and requested localization json to resulting object
                retVal = defaultJson ?? localeJson;

                if (defaultJson != null && localeJson != null)
                {
                    retVal.Merge(localeJson, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Merge });
                }
            }
            return retVal;
        }

        private static JObject InnerGetAllSettings(IContentBlobProvider themeBlobProvider, string settingsPath)
        {
            if (settingsPath == null)
            {
                throw new ArgumentNullException(nameof(settingsPath));
            }

            var result = new JObject();

            if (themeBlobProvider.PathExists(settingsPath))
            {
                using (var stream = themeBlobProvider.OpenRead(settingsPath))
                {
                    result = JsonConvert.DeserializeObject<JObject>(stream.ReadToString());
                }
            }
            return result;
        }



        private string ReadTemplateByPath(string templatePath)
        {
            if (string.IsNullOrEmpty(templatePath))
            {
                throw new ArgumentNullException(nameof(templatePath));
            }

            var cacheKey = CacheKey.With(GetType(), "ReadTemplateByName", templatePath);
            return _memoryCache.GetOrCreateExclusive(cacheKey, (cacheItem) =>
            {
                cacheItem.AddExpirationToken(new CompositeChangeToken(new[] { ThemeEngineCacheRegion.CreateChangeToken(), _themeBlobProvider.Watch(templatePath) }));
                using (var stream = _themeBlobProvider.OpenRead(templatePath))
                {
                    return stream.ReadToString();
                }
            });
        }

        private string GetSettingsFilePath()
        {
            var prefix = _httpContextAccessor.HttpContext.Request.Query["preview_mode"];
            return prefix.ToString().IsNullOrEmpty() ? "settings_data.json" : $"drafts\\{prefix}_settings_data.json";
        }

        public bool IsFeatureActive(string featureName)
        {
            var cacheKey = CacheKey.With(GetType(), nameof(IsFeatureActive), featureName);

            return _memoryCache.GetOrCreateExclusive(cacheKey, cacheEntry =>
            {
                var changeToken = ThemeEngineCacheRegion.CreateChangeToken();
                var watchChangeToken = _themeBlobProvider.Watch(CurrentThemeSettingPath);
                var tokens = new[]
                           {
                               changeToken, watchChangeToken
                           };
                var compositeChangeToken = new CompositeChangeToken(tokens);
                cacheEntry.AddExpirationToken(compositeChangeToken);

                var settingJObject = GetSettings();
                var result = _featuresAgent.IsActive(featureName, settingJObject);
                return result;
            });
        }
    }
}
