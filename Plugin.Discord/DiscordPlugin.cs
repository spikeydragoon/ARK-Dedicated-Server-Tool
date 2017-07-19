﻿using ArkServerManager.Plugin.Common;
using ArkServerManager.Plugin.Discord.Windows;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ArkServerManager.Plugin.Discord
{
    public sealed class DiscordPlugin : IAlertPlugin
    {
        private const int REQUEST_TIMEOUT = 5000;
        private const int MAX_MESSAGE_LENGTH = 2000;

        private Object lockObject = new Object();

        public DiscordPlugin()
        {
            LoadConfig();
            CallHome().Wait();
        }

        private DiscordPluginConfig Config
        {
            get;
            set;
        }

        public bool Enabled => true;

        public string PluginCode => "E0CF2C1F-17B7-45E1-A2C9-2718493D0873";

        public string PluginName => "Discord Plugin";

        public Version PluginVersion
        {
            get
            {
                try
                {
                    return Assembly.GetExecutingAssembly().GetName().Version;
                }
                catch
                {
                    return new Version();
                }
            }
        }

        public bool HasConfigForm => true;

        private async Task CallHome()
        {
            try
            {
                var publicIP = await NetworkUtils.DiscoverPublicIPAsync();
                await NetworkUtils.PerformCallToAPIAsync(publicIP, PluginCode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed calling home {ex.Message}");
            }
        }

        public void HandleAlert(AlertType alertType, string profileName, string alertMessage)
        {
            if (string.IsNullOrWhiteSpace(alertMessage))
                return;

            lock (lockObject)
            {
                var configProfiles = Config.ConfigProfiles.Where(cp => cp.IsEnabled
                                                                    && cp.AlertTypes.Any(pn => pn.Value.Equals(alertType))
                                                                    && cp.ProfileNames.Any(pn => pn.Value.Equals(profileName, StringComparison.OrdinalIgnoreCase))
                                                                    && !string.IsNullOrWhiteSpace(cp.DiscordWebhookUrl));
                if (configProfiles == null || configProfiles.Count() == 0)
                {
#if DEBUG
                    var logFile = Path.Combine(PluginHelper.PluginFolder, "DiscordErrors.log");
                    File.AppendAllLines(logFile, new[] { $"{alertType}; {profileName} - {alertMessage.Replace(Environment.NewLine, " ")} (No config profiles found)" }, Encoding.Unicode);
#endif                
                    return;
                }

                foreach (var configProfile in configProfiles)
                {
                    var postData = string.Empty;

                    if (configProfile.DiscordUseTTS)
                        postData += $"&tts={configProfile.DiscordUseTTS}";
                    if (!string.IsNullOrWhiteSpace(configProfile.DiscordBotName))
                        postData += $"&username={configProfile.DiscordBotName.Replace("&", "_")}";
                    postData += $"&content=";
                    if (configProfile.PrefixMessageWithProfileName && !string.IsNullOrWhiteSpace(profileName))
                        postData += $"({profileName.Replace("&", "_")}) ";
                    postData += $"{alertMessage.Replace("&", "_")}";

                    if (postData.Length > MAX_MESSAGE_LENGTH)
                        postData = $"{postData.Substring(0, MAX_MESSAGE_LENGTH - 3)}...";

                    try
                    {
                        var data = Encoding.ASCII.GetBytes(postData);

                        var url = configProfile.DiscordWebhookUrl;
                        url = url.Trim();
                        if (url.EndsWith("/"))
                            url = url.Substring(0, url.Length - 1);

                        var httpRequest = WebRequest.Create($"{url}?wait=true");
                        httpRequest.Timeout = REQUEST_TIMEOUT;
                        httpRequest.Method = "POST";
                        httpRequest.ContentType = "application/x-www-form-urlencoded";
                        httpRequest.ContentLength = data.Length;

                        using (var stream = httpRequest.GetRequestStream())
                        {
                            stream.Write(data, 0, data.Length);
                        }

                        var httpResponse = (HttpWebResponse)httpRequest.GetResponse();
                        var responseString = new StreamReader(httpResponse.GetResponseStream()).ReadToEnd();
                        if (httpResponse.StatusCode == HttpStatusCode.OK)
                        {
                            Debug.WriteLine($"{nameof(HandleAlert)}\r\nResponse: {responseString}");
#if DEBUG
                            var logFile = Path.Combine(PluginHelper.PluginFolder, "DiscordSuccess.log");
                            File.AppendAllLines(logFile, new[] { $"{alertType}; {profileName} - {alertMessage.Replace(Environment.NewLine, " ")} ({responseString})" }, Encoding.Unicode);
#endif
                        }
                        else
                        {
                            Debug.WriteLine($"{nameof(HandleAlert)}\r\n{httpResponse.StatusCode}: {responseString}");
#if DEBUG
                            var logFile = Path.Combine(PluginHelper.PluginFolder, "DiscordErrors.log");
                            File.AppendAllLines(logFile, new[] { $"{alertType}; {profileName} - {alertMessage.Replace(Environment.NewLine, " ")} ({responseString})" }, Encoding.Unicode);
#endif
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ERROR: {nameof(HandleAlert)}\r\n{ex.Message}");
#if DEBUG
                        var logFile = Path.Combine(PluginHelper.PluginFolder, "DiscordExceptions.log");
                        File.AppendAllLines(logFile, new[] { $"{alertType}; {profileName} - {alertMessage.Replace(Environment.NewLine, " ")} ({ex.Message})" }, Encoding.Unicode);
#endif
                    }
                }
            }
        }

        private void LoadConfig()
        {
            try
            {
                Config = null;

                var configFile = Path.Combine(PluginHelper.PluginFolder, DiscordPluginConfig.CONFIG_FILENAME);
                Config = JsonUtils.DeserializeFromFile<DiscordPluginConfig>(configFile);

                if ((Config?.ConfigProfiles?.Count ?? 0) == 0)
                {
                    Config = new DiscordPluginConfig();

                    SaveConfig();
                }

                Config?.CommitChanges();
            }
            catch (Exception ex)
            {
                Config = new DiscordPluginConfig();
                Debug.WriteLine($"ERROR: {nameof(LoadConfig)}\r\n{ex.Message}");
            }
        }

        public void OpenConfigForm(Window owner)
        {
            var window = new ConfigWindow(this.Config);
            window.Owner = owner;

            var dialogResult = window.ShowDialog();
            if (dialogResult.HasValue && dialogResult.Value)
            {
                SaveConfig();
                LoadConfig();
            }
        }

        private void SaveConfig()
        {
            try
            {
                var configFile = Path.Combine(PluginHelper.PluginFolder, DiscordPluginConfig.CONFIG_FILENAME);
                JsonUtils.SerializeToFile(Config, configFile);
                Config?.CommitChanges();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR: {nameof(SaveConfig)}\r\n{ex.Message}");
            }
        }
    }
}