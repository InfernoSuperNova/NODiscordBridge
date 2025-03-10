using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NODiscordChatBridge;

public class BotConfig
{
    public static BotConfig I;
    public string BotToken { get; set; }
    public string ChatChannelId { get; set; }
    public string KillLogChannelId { get; set; }
    public int KillLoggingLevel { get; set; }

    private static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "discord_config.json");
    
    public static BotConfig LoadConfig()
    {
        NODiscordChatBridge.Logger.LogInfo("Loading config...");
        if (!File.Exists(ConfigPath))
        {
            NODiscordChatBridge.Logger.LogInfo("Config file does not exist, generating default file...");
            var defaultConfig = new BotConfig
            {
                BotToken = "YOUR_BOT_TOKEN_HERE",
                ChatChannelId = "CHAT_CHANNEL_ID_HERE",
                KillLogChannelId = "KILL_LOG_CHANNEL_ID_HERE",
                KillLoggingLevel = 1,
            };
            WriteConfigWithComments(defaultConfig);
            throw new Exception("Config file not found. A default one has been created. Please edit 'discord_config.json' and restart the bot.");
        }

        var config = JsonConvert.DeserializeObject<BotConfig>(File.ReadAllText(ConfigPath));
        WriteConfigWithComments(config);
        BotConfig.I = config;
        return config;
    }

    private static void WriteConfigWithComments(BotConfig config)
    {
        var configData = new Dictionary<string, (string Comment, object Value)>
        {
            { "BotToken", ("Discord bot token (replace with your bot's token)", config.BotToken) },
            { "ChatChannelId", ("ID of the chat channel where messages are relayed", config.ChatChannelId) },
            { "KillLogChannelId", ("ID of the channel where the killfeed is sent", config.KillLogChannelId) },
            { "KillLoggingLevel", ("Level of killfeed (0 = off, 1 = player related only, 2 = all)", config.KillLoggingLevel) }
        };

        var sb = new StringBuilder();
        sb.AppendLine("{");
        bool first = true;
        foreach (var entry in configData)
        {
            if (!first) sb.AppendLine(",");
            first = false;
            sb.AppendLine($"    // {entry.Value.Comment}");
            sb.AppendLine($"    \"{entry.Key}\": {JsonConvert.SerializeObject(entry.Value.Value)}");
        }
        sb.AppendLine("}");
        
        File.WriteAllText(ConfigPath, sb.ToString());
    }
}