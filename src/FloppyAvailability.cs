using System;
using Unity.Collections;
using Unity.Netcode;

namespace Horse.Floppy;

// The host's mission-selection choice applies to every player. Client config
// preferences are never overwritten by a host's temporary lobby setting.
internal static class FloppyAvailability
{
    private const string StateMessage = FloppyPlugin.Guid + ".availability.v1";
    private const string RequestMessage = FloppyPlugin.Guid + ".availability-request.v1";
    private static NetworkManager manager;
    private static CustomMessagingManager messages;
    private static bool remoteKnown;
    private static bool remoteEnabled;
    private static bool lastPreference;
    private static float nextRequest;

    internal static bool CanEdit
    {
        get
        {
            var current = NetworkManager.Singleton;
            return current == null || !current.IsListening || current.IsServer;
        }
    }
    internal static bool IsSynchronized => CanEdit ||
        (manager == NetworkManager.Singleton && messages != null && remoteKnown);
    internal static bool Enabled => CanEdit ? FloppyPlugin.Instance?.Enabled.Value == true
        : IsSynchronized && remoteEnabled;

    internal static void SetEnabled(bool enabled)
    {
        if (!CanEdit || FloppyPlugin.Instance == null) return;
        FloppyPlugin.Instance.Enabled.Value = enabled;
        FloppyPlugin.Instance.Config.Save();
        lastPreference = enabled;
        if (manager != null && manager.IsServer) Broadcast();
    }

    internal static void Tick(float now)
    {
        var current = NetworkManager.Singleton;
        var currentMessages = current != null && current.IsListening ? current.CustomMessagingManager : null;
        if (current == null || !current.IsListening || currentMessages == null)
        {
            if (messages != null || manager != null) Dispose();
            return;
        }
        if (manager != current || !ReferenceEquals(messages, currentMessages))
        {
            Dispose();
            manager = current;
            messages = currentMessages;
            remoteKnown = false;
            nextRequest = now;
            lastPreference = FloppyPlugin.Instance?.Enabled.Value == true;
            messages.RegisterNamedMessageHandler(StateMessage, OnState);
            messages.RegisterNamedMessageHandler(RequestMessage, OnRequest);
            manager.OnClientConnectedCallback += OnConnected;
            manager.OnClientDisconnectCallback += OnDisconnected;
            if (manager.IsServer) Broadcast();
        }
        bool preference = FloppyPlugin.Instance?.Enabled.Value == true;
        if (manager.IsServer && preference != lastPreference)
        {
            lastPreference = preference;
            Broadcast();
        }
        if (!manager.IsServer && manager.IsConnectedClient && !remoteKnown && now >= nextRequest)
        {
            nextRequest = now + .75f;
            using var writer = new FastBufferWriter(1, Allocator.Temp);
            writer.WriteByteSafe(1);
            messages.SendNamedMessage(RequestMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }
    private static void OnConnected(ulong clientId)
    {
        if (manager == null) return;
        if (manager.IsServer) SendState(clientId);
        else if (clientId == manager.LocalClientId) { remoteKnown = false; nextRequest = 0; }
    }
    private static void OnDisconnected(ulong clientId)
    {
        if (manager != null && !manager.IsServer && clientId == manager.LocalClientId)
            remoteKnown = false;
    }
    private static void OnRequest(ulong sender, FastBufferReader reader)
    {
        if (manager == null || !manager.IsServer || !reader.TryBeginRead(1)) return;
        reader.ReadByteSafe(out byte version);
        if (version == 1) SendState(sender);
    }
    private static void OnState(ulong sender, FastBufferReader reader)
    {
        if (manager == null || manager.IsServer || sender != NetworkManager.ServerClientId || !reader.TryBeginRead(2)) return;
        reader.ReadByteSafe(out byte version);
        reader.ReadByteSafe(out byte value);
        if (version != 1 || value > 1) return;
        remoteEnabled = value == 1;
        remoteKnown = true;
    }
    private static void SendState(ulong clientId)
    {
        if (messages == null || manager == null || !manager.IsServer) return;
        using var writer = new FastBufferWriter(2, Allocator.Temp);
        writer.WriteByteSafe(1);
        writer.WriteByteSafe(Enabled ? (byte)1 : (byte)0);
        messages.SendNamedMessage(StateMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
    }
    private static void Broadcast()
    {
        if (messages == null || manager == null || !manager.IsServer) return;
        using var writer = new FastBufferWriter(2, Allocator.Temp);
        writer.WriteByteSafe(1);
        writer.WriteByteSafe(Enabled ? (byte)1 : (byte)0);
        messages.SendNamedMessageToAll(StateMessage, writer, NetworkDelivery.ReliableSequenced);
    }
    internal static void Dispose()
    {
        if (manager != null)
        {
            manager.OnClientConnectedCallback -= OnConnected;
            manager.OnClientDisconnectCallback -= OnDisconnected;
        }
        if (messages != null)
        {
            messages.UnregisterNamedMessageHandler(StateMessage);
            messages.UnregisterNamedMessageHandler(RequestMessage);
        }
        manager = null;
        messages = null;
        remoteKnown = false;
        remoteEnabled = false;
        nextRequest = 0;
    }
}
