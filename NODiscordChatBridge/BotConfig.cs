using System;
using System.IO;
using Newtonsoft.Json;

namespace NODiscordChatBridge;

public class BotConfig
{
    public string BotToken { get; set; }
    public string ChatChannelId { get; set; }
    public string KillLogChannelId { get; set; }

    private static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "discord_config.json");
    
    public static BotConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaultConfig = new BotConfig
            {
                BotToken = "YOUR_BOT_TOKEN_HERE",
                ChatChannelId = "CHAT_CHANNEL_ID_HERE",
                KillLogChannelId = "KILL_LOG_CHANNEL_ID_HERE"
            };
            File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(defaultConfig, Formatting.Indented));
            throw new Exception("Config file not found. A default one has been created. Please edit 'config.json' and restart the bot.");
        }

        return JsonConvert.DeserializeObject<BotConfig>(File.ReadAllText(ConfigPath));
    }
}