using SPACS.Utilities;

#if UNITY_WEBGL && !UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using UnityEngine;


public class WebSocketMessagesHandler : Singleton<WebSocketMessagesHandler>
{
    public const string WEB_SOCKET_HANDLER_OBJ_NAME = "WebSocketMessagesHandler";

    [DllImport("__Internal")]
    private static extern void WebSocketInit(string handler);

    [DllImport("__Internal")]
    private static extern int WebSocketOpen(string url);

    [DllImport("__Internal")]
    private static extern void WebSocketSendMessage(int channel, string message);

    [DllImport("__Internal")]
    private static extern void WebSocketSendBuffer(int channel, byte[] buffer, int length);

    [DllImport("__Internal")]
    private static extern void WebSocketClose(int channel);

    private Dictionary<int, WebGLWebSocketHandler> handlers = new Dictionary<int, WebGLWebSocketHandler>();

    private Dictionary<int, WebGLWebSocketHandler> openingChannels = new Dictionary<int, WebGLWebSocketHandler>();

    private List<int> closingChannels = new List<int>();

    protected override void Awake()
    {
        base.Awake();
        gameObject.name = WEB_SOCKET_HANDLER_OBJ_NAME;
        WebSocketInit(WEB_SOCKET_HANDLER_OBJ_NAME);
    }

    public async Task Connect(string url, WebGLWebSocketHandler handler)
    {
        int channel = WebSocketOpen(url);

        openingChannels.Add(channel, handler);

        while (openingChannels.ContainsKey(channel))
        {
            string channels = "";
            foreach (var item in openingChannels)
            {
                channels += item.Key + ", ";
            }
            await Task.Yield();
        }
    }

    public void SocketOpenEventHandler(int channelId)
    {
        Debug.Log($"{nameof(SocketOpenEventHandler)}: open channel {channelId}");
        handlers.TryAdd(channelId, openingChannels[channelId]);
        openingChannels[channelId].ConnectionState = IWebSocketHandler.EWebSocketState.Open;
        openingChannels.Remove(channelId);
    }

    public void SocketCloseEventHandler(int channelId)
    {
        Debug.Log($"{nameof(SocketCloseEventHandler)}: received message");
        if (closingChannels.Contains(channelId))
        {
            closingChannels.Remove(channelId);
            if (handlers.ContainsKey(channelId))
            {
                handlers.Remove(channelId);
            }
        }
        else
        {
            if (handlers.ContainsKey(channelId))
            {
                handlers[channelId].OnSocketClose();
                handlers.Remove(channelId);
            }
        }
    }

    public async void SocketMessageEventHandler(string channelAndMessage)
    {
        Debug.Log($"{nameof(SocketMessageEventHandler)}: received message");

        var (channel, message) = await ParseChannelAndMessage(channelAndMessage);
        if (channel != -1)
        {
            handlers[channel].OnMessageReceived(message);
        }
    }
    public async void SocketBinaryMessageEventHandler(string channelAndMessage)
    {
        Debug.Log($"{nameof(SocketBinaryMessageEventHandler)}: received message");

        var (channel, message) = await ParseChannelAndMessage(channelAndMessage);
        if (channel != -1)
        {
            handlers[channel].OnBinaryMessageReceived(Convert.FromBase64String(message));
        }
    }
    private async Task<(int, string)> ParseChannelAndMessage(string channelAndMessage)
    {
        if (channelAndMessage.Contains("|"))
        {
            int pipeIndex = channelAndMessage.IndexOf('|');

            if (!(int.TryParse(channelAndMessage.Substring(0, pipeIndex), out int channel)))
            {
                Debug.LogError("Received message in wrong format: " + channelAndMessage + ". Expecting the message format to be [(int)channelId]|[(string)message]");
                return (-1, null);
            }

            string message = channelAndMessage.Substring(pipeIndex + 1);

            //If the channel is still opening, wait
            while (openingChannels.ContainsKey(channel))
            {
                await Task.Yield();
            }

            if (handlers.ContainsKey(channel))
            {
                // Success return!
                return (channel, message);
            }
            else
            {
                Debug.LogError($"Channel {channel} not found!");
            }
        }
        else
        {
            Debug.LogError("Received message in wrong format: " + channelAndMessage + ". Expecting the message format to be [(int)channelId]|[(string)message]");
        }

        // Default return
        return (-1, null);
    }

    public void SocketErrorEventHandler(int channel)
    {
        Debug.Log($"{nameof(SocketErrorEventHandler)}: received message");
        handlers[channel].OnSocketError();
    }

    public void SendMessage(WebGLWebSocketHandler handler, string message)
    {
        foreach (var channel in handlers)
        {
            if (channel.Value == handler)
            {
                WebSocketSendMessage(channel.Key, message);
                return;
            }
        }
    }

    public void SendBuffer(WebGLWebSocketHandler handler, byte[] buffer)
    {
        foreach (var channel in handlers)
        {
            if (channel.Value == handler)
            {
                WebSocketSendBuffer(channel.Key, buffer, buffer.Length);
                return;
            }
        }
    }


    public async Task CloseSocket(WebGLWebSocketHandler handler)
    {
        int channelId = -1;
        foreach (var channel in handlers)
        {
            if (channel.Value == handler)
            {
                WebSocketClose(channel.Key);
                channelId = channel.Key;
                break;
            }
        }
        if (channelId >= 0)
        {
            closingChannels.Add(channelId);
            while (closingChannels.Contains(channelId))
            {
                await Task.Yield();
            }
        }
    }
}
#endif