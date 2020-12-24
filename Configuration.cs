using System;
using System.Reflection;
using System.Configuration;
using System.Globalization;
using System.IO;

namespace MSCognitiveTextToSpeech
{
    /// <summary>
    /// Handles getting settings from the dll.config file
    /// </summary>
    public class Configuration
    {
        private AppSettingsSection appSettings;
        private NumberFormatInfo numberFormats;

        public Configuration()
        {
            UriBuilder uri = new UriBuilder(Assembly.GetExecutingAssembly().CodeBase);
            Path = Uri.UnescapeDataString(uri.Path);

            System.Configuration.Configuration configFile = ConfigurationManager.OpenExeConfiguration(Path);

            appSettings = configFile.AppSettings;

            SettingCount = appSettings.Settings.Count;

            numberFormats = new NumberFormatInfo()
            {
                NumberGroupSeparator = "",
                CurrencyDecimalSeparator = "."
            };
        }

        public string Path { get; }
        public int SettingCount { get; }
        public bool Exists()
        {
            return File.Exists(Path);
        }
        public T Setting<T>(string name)
        {
            try
            {
                return (T)Convert.ChangeType(appSettings.Settings[name].Value, typeof(T), numberFormats);
            }
            catch (Exception)
            {
                return default(T);
            }
        }
    }
}
