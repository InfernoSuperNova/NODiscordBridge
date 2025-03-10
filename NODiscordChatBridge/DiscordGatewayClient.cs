using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace NODiscordChatBridge;

public class DiscordGatewayClient
{
    private ManualLogSource _logger;
    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private string _chatChannelId;
    private string _userId;
    private readonly string _token;
    private int _heartbeatInterval;
    private Task _heartbeatTask;

    public DiscordGatewayClient(string token, ManualLogSource logger, string chatChannelId)
    {
        _ws = new ClientWebSocket();
        _cts = new CancellationTokenSource();
        _token = token;
        _logger = logger;
        _chatChannelId = chatChannelId;
    }

    public async Task ConnectAsync()
    {
        // Connect to Discord Gateway
        await _ws.ConnectAsync(new Uri("wss://gateway.discord.gg/?v=10&encoding=json"), CancellationToken.None);
        
        // Wait for the Hello message to get heartbeat interval.
        var helloMessage = await ReceiveMessageAsync();
        JObject helloObj = JObject.Parse(helloMessage);

        if (helloObj["d"]?["heartbeat_interval"] != null)
        {
            _heartbeatInterval = helloObj["d"]["heartbeat_interval"].Value<int>();
            _logger.LogInfo("Heartbeat interval set to: " + _heartbeatInterval);
            
            // Start the heartbeat loop
            _heartbeatTask = Task.Run(() => HeartbeatLoop());
            
            // Send Identify payload to authenticate
            var identifyPayload = new
            {
                op = 2,
                d = new
                {
                    token = _token,
                    intents = (1 << 0) | (1 << 9) | (1 << 15), // GUILDS | GUILD_MESSAGES | MESSAGE_CONTENT
                    properties = new
                    {
                        os = Environment.OSVersion.ToString(),
                        browser = "CustomClient",
                        device = "CustomClient"
                    }
                }
            };
            
            await SendMessageAsync(JsonConvert.SerializeObject(identifyPayload));
        }
        else
        {
            _logger.LogError("Failed to parse heartbeat_interval from hello message: " + helloMessage);
        }
        
    }

    private async Task HeartbeatLoop()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            var heartbeatPayload = new { op = 1, d = (int?)null };
            _logger.LogInfo("Discord bot: Sending heartbeat...");
            await SendMessageAsync(JsonConvert.SerializeObject(heartbeatPayload));
            _logger.LogInfo("Discord bot: Heartbeat sent");
            await Task.Delay(_heartbeatInterval, _cts.Token);
        }
    }

    /// <summary>
    /// Sends a presence update using opcode 3.
    /// </summary>
    /// <param name="activityName">The activity to display.</param>
    /// <param name="activityType">
    /// The type of activity:
    /// 0 = Playing, 1 = Streaming, 2 = Listening, 3 = Watching, etc.
    /// </param>
    public async Task UpdatePresenceAsync(string activityName, int activityType = 0)
    {
        var payload = new
        {
            op = 3,
            d = new
            {
                since = (int?)null,
                activities = new[]
                {
                    new
                    {
                        name = activityName,
                        type = activityType
                    }
                },
                status = "online",
                afk = false
            }
        };

        await SendMessageAsync(JsonConvert.SerializeObject(payload));
    }

    private async Task SendMessageAsync(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task<string> ReceiveMessageAsync()
    {
        var buffer = new byte[4096];
        var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    public async Task DisconnectAsync()
    {
        if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.Connecting)
        {
            _cts.Cancel(); // Stop tasks
            _logger.LogInfo("Shutting down gracefully...");

            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while closing WebSocket: " + ex.Message);
            }
        }

        _ws.Dispose();
        _cts.Dispose();
    }
    
    public async Task ListenAsync()
    {
        var buffer = new byte[4096];
        while (_ws.State == WebSocketState.Open)
        {
            var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        
            // If the message is fragmented, accumulate until EndOfMessage is true
            var message = new StringBuilder();
            message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            while (!result.EndOfMessage)
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }

            try
            {
                HandleGatewayMessage(message.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogFatal(ex);
            }
            
        }
    }

    private void HandleGatewayMessage(string message)
    {
        JObject json = JObject.Parse(message);
        //_logger.LogInfo(json.ToString(Formatting.Indented));
        string type = json["t"]?.ToString() ?? "Heartbeat";
        if (type == "") type = "Heartbeat";
        _logger.LogInfo("Message type: " + type);
        switch (type)
        {
            case "MESSAGE_CREATE":
                var data = json["d"];
                if (data == null) break;
                string senderId = data["author"]?["id"]?.ToString() ?? "";
                if (senderId == _userId) break;

                string sender = data["author"]?["global_name"]?.ToString() ?? "Unknown";
                string content = data["content"]?.ToString() ?? "";
                string channelId = data["channel_id"]?.ToString() ?? "Unknown";

                if (channelId != _chatChannelId) break;
                string msg = $"[discord][{sender}] {content}";
                _logger.LogInfo(msg);

                NODiscordChatBridge.MessageJustSent = true;
                ChatManager.SendChatMessage($"[discord][{sender}] {content}", true);
                
                    
                    
                break;
            case "READY":
                var data2 = json["d"];
                if (data2 == null) break;
                string userId = data2["user"]?["id"]?.ToString() ?? "Unknown";
                _userId = userId;
                break;
        }
       
        
        
    }
    
    public async Task UpdateRichPresenceAsync(
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
        var payload = new
        {
            op = 3,
            d = new
            {
                since = (int?)null,
                activities = new[]
                {
                    new
                    {
                        name = "Nuclear Option",
                        type = 0, // Playing
                        details = details,
                        state = state, 
                        timestamps = startTimestamp.HasValue ? new { start = startTimestamp, end = endTimestamp } : null,
                        assets = new
                        {
                            large_image = largeImageKey, 
                            large_text = largeImageText, 
                            small_image = smallImageKey, 
                            small_text = smallImageText  
                        }
                    }
                },
                status = status, // "online", "idle", "dnd"
                afk = false
            }
        };

        _logger.LogInfo($"Updating Rich Presence: {details} - {state}");
        await SendMessageAsync(JsonConvert.SerializeObject(payload));
    }
}