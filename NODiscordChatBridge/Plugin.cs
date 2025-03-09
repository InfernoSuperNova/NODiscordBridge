﻿using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using System.Threading.Tasks;
using Mirage;


namespace NODiscordChatBridge;

public enum NOMessageType
{
    Chat,
    Killfeed,
    System
}




[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class NODiscordChatBridge : BaseUnityPlugin
{
    public static bool MessageJustSent = false;
    public static NODiscordChatBridge _singleton;
    public static new ManualLogSource Logger;

    NODiscordChatBridge()
    {
        if (!_singleton)
        {
            _singleton = this;
        }
    }
    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo("Hello World");
        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        
        Bot.Main(Logger);
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }
    
    private async void OnApplicationQuit()
    {
        Logger.LogInfo("Application is quitting, shutting down bot...");
        await Bot.DisconnectAsync();
    }

    public void StartAnyCoroutine(IEnumerator routine)
    {
        StartCoroutine(routine);
    }
    public void ForwardMessage(string message)
    {
        // Your custom logic to forward the message
        // Logger.LogInfo("Forwarded Message: " + message);
    }


    public void MessageToDiscord(string message, NOMessageType type, object data)
    {
        if (MessageJustSent)
        {
            MessageJustSent = false;
            return;
        }
        switch (type)
        {
            case NOMessageType.Chat:
                if (data is Player player)
                {
                    var tag = player.HQ.faction.factionTag;
                    Bot.ChatToDiscord("[" + tag + "][" + player.PlayerName + "] " + message);
                }
                else
                {
                    MessageToDiscord(message, NOMessageType.System, data);
                }
                break;
            case NOMessageType.Killfeed:
                break;
            case NOMessageType.System:
                break;
        }
    }
}

[HarmonyPatch(typeof(MessageUI), "GameMessage")]
public static class Patch_GameMessage
{
    
    // Prefix to intercept and forward the message before the original logic runs
    [HarmonyPrefix]
    public static bool Prefix(string message)
    {
        NODiscordChatBridge._singleton.ForwardMessage(message);
        return true; // Proceed with the original GameMessage method
    }
}

[HarmonyPatch(typeof(MessageUI), "DelayedGameMessage")]
public static class Patch_DelayedGameMessage
{

    // Prefix to intercept and forward the message before the original logic runs
    [HarmonyPrefix]
    public static bool Prefix(string message, float secondsBetweenLine)
    {
        // Should we respect the delay provided? Maybe
        NODiscordChatBridge._singleton.ForwardMessage(message);
        return true; // Proceed with the original DelayedGameMessage method
    }
}

[HarmonyPatch(typeof(ChatManager), "UserCode_TargetReceiveMessage_329197076")]
public static class Patch_TargetReceiveMessage
{
    // Prefix to intercept and forward the message
    [HarmonyPrefix]
    public static bool Prefix(INetworkPlayer _, string message, Player player, bool allChat)
    {
        NODiscordChatBridge._singleton.MessageToDiscord(message, NOMessageType.Chat, player);
        // Return 'true' to allow the original method to proceed
        return true; 
    }
}