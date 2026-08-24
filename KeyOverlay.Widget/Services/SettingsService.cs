using System;
using System.Collections.Generic;
using KeyOverlay.Widget.Models;
using Windows.Data.Json;
using Windows.Storage;
using Windows.System;

namespace KeyOverlay.Widget.Services
{
    internal sealed class OverlaySettings
    {
        public int Columns { get; set; } = 5;
        public bool IsCollapsed { get; set; }
        public List<KeyButtonDefinition> Keys { get; } = new List<KeyButtonDefinition>();
    }

    internal static class SettingsService
    {
        private const string SettingsKey = "overlaySettings.v1";

        public static OverlaySettings Load()
        {
            var settings = new OverlaySettings();
            var loadedFromStorage = false;
            try
            {
                if (ApplicationData.Current.LocalSettings.Values[SettingsKey] is string raw
                    && JsonObject.TryParse(raw, out var root))
                {
                    loadedFromStorage = true;
                    settings.Columns = Math.Max(2, Math.Min(8, (int)root.GetNamedNumber("columns", 5)));
                    settings.IsCollapsed = root.GetNamedBoolean("collapsed", false);

                    foreach (var item in root.GetNamedArray("keys", new JsonArray()))
                    {
                        var key = item.GetObject();
                        settings.Keys.Add(new KeyButtonDefinition
                        {
                            Id = key.GetNamedString("id", Guid.NewGuid().ToString("N")),
                            VirtualKey = (int)key.GetNamedNumber("virtualKey", 0),
                            Modifiers = (VirtualKeyModifiers)(int)key.GetNamedNumber("modifiers", 0),
                            DisplayName = key.GetNamedString("displayName", "KEY")
                        });
                    }
                }
            }
            catch
            {
                settings = new OverlaySettings();
                loadedFromStorage = false;
            }

            if (!loadedFromStorage)
            {
                settings.Keys.Add(KeyButtonDefinition.Create(0x31, "1"));
                settings.Keys.Add(KeyButtonDefinition.Create(0x32, "2"));
                settings.Keys.Add(KeyButtonDefinition.Create(0x33, "3"));
                settings.Keys.Add(KeyButtonDefinition.Create(0x34, "4"));
                settings.Keys.Add(KeyButtonDefinition.Create(0x35, "5"));
                settings.Keys.Add(KeyButtonDefinition.Create(0x73, "F4"));
                settings.Keys.Add(KeyButtonDefinition.Create(0x79, "F10"));
            }

            return settings;
        }

        public static void Save(int columns, bool isCollapsed, IEnumerable<KeyButtonDefinition> keys)
        {
            var keyArray = new JsonArray();
            foreach (var key in keys)
            {
                var item = new JsonObject
                {
                    ["id"] = JsonValue.CreateStringValue(key.Id),
                    ["virtualKey"] = JsonValue.CreateNumberValue(key.VirtualKey),
                    ["modifiers"] = JsonValue.CreateNumberValue((int)key.Modifiers),
                    ["displayName"] = JsonValue.CreateStringValue(key.DisplayName)
                };
                keyArray.Add(item);
            }

            var root = new JsonObject
            {
                ["columns"] = JsonValue.CreateNumberValue(columns),
                ["collapsed"] = JsonValue.CreateBooleanValue(isCollapsed),
                ["keys"] = keyArray
            };
            ApplicationData.Current.LocalSettings.Values[SettingsKey] = root.Stringify();
        }
    }
}
