using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace DataSync.Core.Configuration
{
    /// <summary>
    /// The font size of one text style, as text for editing. <see cref="Key"/> is both the App.config key and the WPF
    /// resource key the styles use.
    /// </summary>
    public class FontSizeSetting : INotifyPropertyChanged
    {
        private string _size;

        public FontSizeSetting(string key, string name, int defaultSize)
        {
            Key = key;
            Name = name;
            DefaultSize = defaultSize;
            _size = defaultSize.ToString(CultureInfo.InvariantCulture);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Key { get; }

        public string Name { get; }

        public int DefaultSize { get; }

        public string Size
        {
            get { return _size; }
            set
            {
                if (_size == value)
                {
                    return;
                }
                _size = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Size)));
            }
        }
    }

    /// <summary>
    /// Font sizes of the UI text styles, edited in the Font settings dialog. Unlike the other settings they apply at once.
    /// </summary>
    public class FontSettings
    {
        public const int MinimumSize = 8;
        public const int MaximumSize = 36;

        private FontSettings()
        {
            Items = new List<FontSizeSetting>
            {
                new FontSizeSetting(SettingKeys.FontSizeBody,         "Body text",      12),
                new FontSizeSetting(SettingKeys.FontSizeSectionTitle, "Section titles", 13),
                new FontSizeSetting(SettingKeys.FontSizeFieldLabel,   "Field labels",   13),
                new FontSizeSetting(SettingKeys.FontSizeFieldValue,   "Field values",   14),
                new FontSizeSetting(SettingKeys.FontSizeTabHeader,    "Tab headers",    13),
                new FontSizeSetting(SettingKeys.FontSizeRigName,      "Rig name",       16),
                new FontSizeSetting(SettingKeys.FontSizeClock,        "Clock",          14),
                new FontSizeSetting(SettingKeys.FontSizeDialogTitle,  "Dialog titles",  16)
            };
        }

        public IList<FontSizeSetting> Items { get; }

        public static FontSettings Defaults()
        {
            return new FontSettings();
        }

        /// <summary>
        /// The saved sizes; missing or invalid values give the defaults.
        /// </summary>
        public static FontSettings Load()
        {
            var settings = new FontSettings();
            foreach (var item in settings.Items)
            {
                if (TryParseSize(AppSettings.GetString(item.Key, null), out var size))
                {
                    item.Size = size.ToString(CultureInfo.InvariantCulture);
                }
            }
            return settings;
        }

        /// <summary>
        /// A whole number between <see cref="MinimumSize"/> and <see cref="MaximumSize"/>.
        /// </summary>
        public static bool TryParseSize(string value, out int size)
        {
            return int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out size)
                   && size >= MinimumSize && size <= MaximumSize;
        }

        /// <summary>
        /// A message describing the first invalid size, or null when all sizes are valid.
        /// </summary>
        public string Validate()
        {
            var invalid = Items.FirstOrDefault(i => !TryParseSize(i.Size, out _));
            return invalid == null
                ? null
                : invalid.Name + " size must be a whole number from " + MinimumSize + " to " + MaximumSize + ".";
        }

        /// <summary>
        /// The values to write to App.config, normalized. Call only when <see cref="Validate"/> returns null.
        /// </summary>
        public IDictionary<string, string> ToValues()
        {
            return Items.ToDictionary(i => i.Key, i =>
            {
                TryParseSize(i.Size, out var size);
                return size.ToString(CultureInfo.InvariantCulture);
            });
        }

        /// <summary>
        /// Validates and writes the sizes to the application's config file.
        /// </summary>
        public void Save()
        {
            var error = Validate();
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            ConfigFile.SaveAppSettings(ToValues());
        }
    }
}
