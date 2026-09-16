using System.Windows;
using DataSync.Core.Configuration;

namespace DataSync.Infrastructure
{
    /// <summary>
    /// Puts font sizes into the application resources. The styles use them with DynamicResource, so open windows update.
    /// </summary>
    public static class FontSizes
    {
        /// <summary>
        /// Applies each valid size; invalid ones (while being typed) keep the current size.
        /// </summary>
        public static void Apply(FontSettings settings)
        {
            foreach (var item in settings.Items)
            {
                if (FontSettings.TryParseSize(item.Size, out var size))
                {
                    Application.Current.Resources[item.Key] = (double)size;
                }
            }
        }
    }
}
