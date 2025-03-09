
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using Newtonsoft.Json;
using UnityEngine;

namespace NODiscordChatBridge;

public class Bot
{
    // TODO: Config file (chat channel ids, bot token)
    // TODO: Make bot appear as online
    // TODO: Presence (match duration, playercount vs playercount, score vs score
    
    private static string _chatChannelId = "";
    
    private static ManualLogSource _logger;
    private static string _botToken = ""; // Replace with your bot token
    private const string ApiUrl = "https://discord.com/api/v10/";

    private static readonly HttpClient Client = new HttpClient();


    
    private static DiscordGatewayClient _discordClient;
    public static void Main(ManualLogSource log)
    {
        var config = BotConfig.LoadConfig();
        _botToken = config.BotToken;
        _chatChannelId = config.ChatChannelId;
        
        
        _logger = log;
        // Set the authorization header with the bot token
        Client.DefaultRequestHeaders.Add("Authorization", $"Bot {_botToken}");
        // await UpdatePresence("Playing Match: Player 1 vs Player 2", "playing");
        
        
        _discordClient = new DiscordGatewayClient(_botToken, _logger, _chatChannelId);
        NODiscordChatBridge._singleton.StartCoroutine(ConnectAndListenCoroutine());

        
    }
    private static IEnumerator ConnectAndListenCoroutine()
    {
        // Start the async connection
        var connectTask = _discordClient.ConnectAsync();
        while (!connectTask.IsCompleted)
        {
            yield return null;
        }
        if (connectTask.IsFaulted)
        {
            _logger.LogError("Failed to connect to Discord Gateway.");
            yield break;
        }
        SetRichPresence( details: "Flying an Ifrit",
            state: "Dogfighting over Boscali",
            startTimestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            largeImageKey: "kr_67",
            largeImageText: "KR-67 Ifrit",
            smallImageKey: "pala_logo",
            smallImageText: "PALA");
        // Now start listening for events on a separate Task.
        // Note: If you need to interact with Unity objects, you'll have to marshal those calls back to the main thread.
        Task listenTask = _discordClient.ListenAsync();

        // Optionally, you can wait for listenTask to complete or let it run indefinitely.
        // Here we simply yield while it’s running.
        while (!listenTask.IsCompleted)
        {
            yield return null;
        }
    }

    public static void SetRichPresence(
        string details,
        string state,
        long? startTimestamp = null,
        long? endTimestamp = null,
        string largeImageKey = null,
        string largeImageText = null,
        string smallImageKey = null,
        string smallImageText = null,
        string status = "online"
    )
    {
        NODiscordChatBridge._singleton.StartCoroutine(UpdateRichPresenceCoroutine(details, state, startTimestamp,
            endTimestamp, largeImageKey, largeImageText, smallImageKey, smallImageText, status));
    }

    public static IEnumerator UpdateRichPresenceCoroutine(string details,
        string state,
        long? startTimestamp,
        long? endTimestamp,
        string largeImageKey,
        string largeImageText,
        string smallImageKey,
        string smallImageText,
        string status = "online"
    )
    {
        var task = _discordClient.UpdateRichPresenceAsync(details, state, startTimestamp, endTimestamp, largeImageKey,
            largeImageText, smallImageKey, smallImageText, status);
        while (!task.IsCompleted)
        {
            yield return null;
        }
    }
    public static void SetStatus(string status, int type = 0)
    {
        NODiscordChatBridge._singleton.StartCoroutine(UpdateStatusCoroutine(status, type));
    }

    private static IEnumerator UpdateStatusCoroutine(string status, int type)
    {
        var task = _discordClient.UpdatePresenceAsync("Nuclear Option");
        while (!task.IsCompleted)
        {
            yield return null;
        }
    }
    public static void ChatToDiscord(string chat)
    {
        // Start a coroutine to handle the async call
        NODiscordChatBridge._singleton.StartCoroutine(SendMessageCoroutine(_chatChannelId, chat));
    }

    private static IEnumerator SendMessageCoroutine(string channelId, string message)
    {
        var task = SendMessage(channelId, message); // This is still async
        while (!task.IsCompleted)
        {
            yield return null;  // Yield until the task is completed
        }
        if (task.IsFaulted)
        {
            _logger.LogError("Message failed to send: " + task.Exception);
        }
        else
        {
            _logger.LogInfo("Message sent successfully.");
        }
    }

    public static async Task SendMessage(string channelId, string message)
    {
        var payload = new
        {
            content = message
        };

        // Convert the payload to JSON
        string jsonPayload = JsonConvert.SerializeObject(payload);
    
        try
        {
            // Await the async method directly instead of blocking with .Result
            var response = await SendMessageAsync(channelId, jsonPayload);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInfo("Message sent successfully!");
            }
            else
            {
                _logger.LogError($"Failed to send message: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"{ex.Message}");
        }
    }

    public static async Task<HttpResponseMessage> SendMessageAsync(string channelId, string jsonPayload)
    {
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        // Send a POST request to send the message
        var response = await Client.PostAsync($"{ApiUrl}channels/{channelId}/messages", content);

        return response;
    }
    

    public static async Task DisconnectAsync()
    {
        await _discordClient.DisconnectAsync();
    }
}
public static class HttpClientExtensions
{
    public static async Task<HttpResponseMessage> PatchAsync(this HttpClient client, string requestUri, HttpContent content)
    {
        var method = new HttpMethod("PATCH");
        var request = new HttpRequestMessage(method, requestUri)
        {
            Content = content
        };
        return await client.SendAsync(request);
    }
}