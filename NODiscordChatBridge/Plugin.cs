using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using System.Threading.Tasks;
using JetBrains.Annotations;
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
    public static NODiscordChatBridge I;
    public static new ManualLogSource Logger;

    NODiscordChatBridge()
    {
        if (!I)
        {
            I = this;
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


    public void MessageToDiscord(string message, NOMessageType type, [CanBeNull] object data)
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
                    string factionTag = "";
                    if (player.HQ && player.HQ.faction && player.HQ.faction.factionTag != null)
                    {
                        factionTag = "[" + player.HQ.faction.factionTag + "]";
                    }
                    Bot.ChatToDiscord(factionTag + "[" + player.PlayerName + "] " + message);
                }
                else
                {
                    MessageToDiscord(message, NOMessageType.System, data);
                }
                break;
            case NOMessageType.Killfeed:
                if (data is int interactionType)
                {
                    // E0, no players involved                                      (EVE)
                    // E1, killed is a player                                       (PVE)
                    // E2, killer is a player,                                      (PVE)
                    // I3, killer is a player, killed is a player                   (PVP)
                    // E4, Killed crashed                                           (null case)
                    // I5, killed is a player, killed crashed                       (Other)
                    // I6, killer is a player, killed crashed                       (null case)
                    // I7, killed is a player, killer is a player, killed crashed   (null case)
                    // E8, Friendly Fire                                            (FF)
                    bool send = false;
                    switch (BotConfig.I.KillLoggingLevel)
                    {
                        
                        case 0: // None
                            break;
                        case 1: // PVP only
                            if ((interactionType & 1) != 0 && (interactionType & 2) != 0) send = true;
                            break;
                        case 2: // PVP/PVE
                            if ((interactionType & 1) != 0 || (interactionType & 2) != 0) send = true;
                            break;
                        case 3: // PVP/PVE/EVE
                            send = true;
                            break;
                    }

                    if (BotConfig.I.AlwaysLogFriendlyFire && (interactionType & 8) != 0) send = true;
                    if (send) Bot.KillfeedToDiscord(message);
                        
                }
                
                break;
            case NOMessageType.System:
                Bot.ChatToDiscord(message);
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
        NODiscordChatBridge.I.ForwardMessage(message);
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
        NODiscordChatBridge.I.ForwardMessage(message);
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
        try
        {
            NODiscordChatBridge.I.MessageToDiscord(message, NOMessageType.Chat, player);
        }
        catch (Exception ex)
        {
            NODiscordChatBridge.Logger.LogError(ex);
        }
        // Return 'true' to allow the original method to proceed
        return true; 
    }
}

[HarmonyPatch(typeof(MessageManager), "UserCode_RpcKillMessage_-1012857865")]
public static class Patch_RpcKillMessage
{

    [HarmonyPrefix]

    public static bool Prefix(MessageManager __instance, int killerID, int killedID, KillType killedType)
    {
        int interactionType = 0;
        PersistentUnit killer = UnitRegistry.GetPersistentUnit(killerID);
        PersistentUnit killed = UnitRegistry.GetPersistentUnit(killedID);
        if (killed == null) return true;
        
        killed.GetFaction();
        killer?.GetFaction();
        

        string killedFactionTag = "";
        if (killed.HQ != null) killedFactionTag = "[" + killed.HQ.faction.factionTag + "]";

        interactionType += killed.unitName.Contains("[") ? 1 : 0; // Increment flag by 1 if it's a player (square brackets are a dead giveaway)


        if (killer == null)
        {
            interactionType += interactionType != 0 ? 4 : 0; // Set the crash flag if the crashee was a player
            string cause;
            switch (killedType)
            {
                case KillType.Aircraft:
                    cause = " crashed";
                    break;
                case KillType.Vehicle:
                    cause = " was destroyed";
                    break;
                case KillType.Building:
                    cause = " collapsed";
                    break;
                case KillType.Ship:
                    cause = " sank";
                    break;
                default:
                    cause = "";
                    break;
            }
            string message = killedFactionTag + killed.unitName + cause;
            NODiscordChatBridge.I.MessageToDiscord(message, NOMessageType.Killfeed, interactionType);
            return true;
        }
        interactionType += killer.unitName.Contains("[") ? 2 : 0; // Increment flag by 1 if it's a player (square brackets are a dead giveaway)
        // This means that the flag will either be:
        // 0, no players involved
        // 1, killed is a player
        // 2, killer is a player,
        // 3, both are players,
        // -1 is a special flag which means a player crashed into the ground
        string killerFactionTag = "";
        if (killer.HQ != null) killerFactionTag = "[" + killer.HQ.faction.factionTag + "]";
        string action = "";
        switch (killedType)
        {
                
            case KillType.Aircraft:
                action = " shot down ";
                break;
            case KillType.Vehicle:
                action = " destroyed ";
                break;
            case KillType.Building:
                action = " demolished ";
                break;
            case KillType.Missile:
                action = " intercepted ";
                break;
            case KillType.Ship:
                action = " sank ";
                break;
        }

        string friendlyFire = "";
        if (killedFactionTag == killerFactionTag)
        {
            friendlyFire = " [ FRIENDLY FIRE! ]";
            interactionType += 8;
        }
        
        string message2 = killerFactionTag + killer.unitName + action + killedFactionTag + killed.unitName + friendlyFire;
        NODiscordChatBridge.I.MessageToDiscord(message2, NOMessageType.Killfeed, interactionType);
        return true;
        
        
    }
}

[HarmonyPatch(typeof(MessageManager), "JoinMessage")]

public static class Patch_JoinMessage
{
    [HarmonyPrefix]
    public static bool Prefix(MessageManager __instance, Player joinedPlayer)
    {
        try
        {
            string message = joinedPlayer.PlayerName + " joined the game";
            NODiscordChatBridge.I.MessageToDiscord(message, NOMessageType.System, joinedPlayer);
        }
        catch (Exception ex)
        {
            NODiscordChatBridge.Logger.LogError(ex);
        }
        return true;
    }
    
}

[HarmonyPatch(typeof(MessageManager), "DisconnectedMessage")]

public static class Patch_DisconnectedMessage
{
    [HarmonyPrefix]
    public static bool Prefix(MessageManager __instance, Player player)
    {
        try
        {
            string message = player.PlayerName + " disconnected";
            NODiscordChatBridge.I.MessageToDiscord(message, NOMessageType.System, player);
        }
        catch (Exception ex)
        {
            NODiscordChatBridge.Logger.LogError(ex);
        }
        return true;
    }
    
}


