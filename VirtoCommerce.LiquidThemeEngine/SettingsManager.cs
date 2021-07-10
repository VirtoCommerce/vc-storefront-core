using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VirtoCommerce.Storefront.Model.Common.Exceptions;

namespace VirtoCommerce.LiquidThemeEngine
{
    public static class SettingsManager
    {
        public class Settings
        {
            public Preset CurrentPreset { get; set; }

            public IList<Preset> Presets { get; set; } = new List<Preset>();
        }

        public class Preset
        {
            public string Name { get; set; }

            public JObject Json { get; set; }
        }

        public static JObject Merge(JObject baseJson, JObject currentJson)
        {
            if (baseJson == null)
            {
                throw new ArgumentNullException(nameof(baseJson));
            }
            if (currentJson == null)
            {
                throw new ArgumentNullException(nameof(currentJson));
            }

            var baseSettings = ReadSettings(baseJson);
            var currentSettings = ReadSettings(currentJson);
            //Change the current preset for base doc according to head preset value if it specified
            if (!string.IsNullOrEmpty(currentSettings.CurrentPreset.Name))
            {
                baseSettings.CurrentPreset = baseSettings.Presets.FirstOrDefault(x => x.Name == currentSettings.CurrentPreset.Name);
            }
            var result = baseSettings.CurrentPreset?.Json ?? new JObject();
            result.Merge(currentSettings.CurrentPreset.Json, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Merge });
            return result;
        }

        public static Settings ReadSettings(JObject json)
        {
            var result = new Settings
            {
                CurrentPreset = new Preset
                {
                    Json = json
                }
            };

            if (json.GetValue("presets") is JObject presetsJson)
            {
                var allPresetsJsonProperties = presetsJson.Children().Cast<JProperty>().ToList();
                foreach (var presetJsonProperty in allPresetsJsonProperties)
                {
                    var preset = new Preset
                    {
                        Name = presetJsonProperty.Name,
                        Json = presetJsonProperty.Value as JObject
                    };
                    result.Presets.Add(preset);
                }
            }

            var currentPresetJsonToken = json.GetValue("current");
            switch (currentPresetJsonToken)
            {
                case JValue currentPresetJsonValue:
                {
                    var presetName = currentPresetJsonValue.Value.ToString();
                    var currentPresetJson = result.Presets.FirstOrDefault(x => x.Name == presetName)?.Json;
                    if (currentPresetJson == null && result.Presets.Any())
                    {
                        throw new StorefrontException($"Setting preset with name '{presetName}' not found");
                    }
                    result.CurrentPreset.Name = presetName;
                    result.CurrentPreset.Json = currentPresetJson ?? json;
                    break;
                }
                case JObject currentPresetJson:
                    result.CurrentPreset.Json = currentPresetJson;
                    break;
            }

            if (result.Presets.Any())
            {
                var globalSettings = json.Properties().Where(x => x.Name != "presets" && x.Name != "current");
                foreach (var globalSetting in globalSettings)
                {
                    foreach (var preset in result.Presets)
                    {
                        preset.Json.Add(globalSetting.Name, globalSetting.Value);
                    }
                }
            }

            return result;
        }
    }
}
