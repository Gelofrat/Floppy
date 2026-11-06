using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Horse.Floppy;

// CoinflipColin posts Play_Coinflip through this same native Wwise API.
// Floppy owns its event IDs, PCM media, attenuation and SFX bus routing.
internal static class PcAudio
{
    private static byte[] bank;
    private static uint bankId;
    private static bool loaded;
    private static bool warned;
    internal static void Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Floppy.AudioBank")
            ?? throw new InvalidDataException("Embedded Floppy soundbank missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        bank = buffer.ToArray();
    }
    private static bool EnsureLoaded()
    {
        if (loaded) return true;
        if (bank == null || !AkUnitySoundEngine.IsInitialized()) return false;
        IntPtr allocation = Marshal.AllocHGlobal(bank.Length + 15);
        try
        {
            var aligned = new IntPtr((allocation.ToInt64() + 15) & ~15L);
            Marshal.Copy(bank, 0, aligned, bank.Length);
            var result = AkUnitySoundEngine.LoadBankMemoryCopy(aligned, (uint)bank.Length, out bankId);
            loaded = result == AKRESULT.AK_Success;
            if (!loaded && !warned)
            {
                warned = true;
                FloppyPlugin.Log.LogError("Wwise rejected Floppy soundbank: " + result);
            }
            return loaded;
        }
        finally { Marshal.FreeHGlobal(allocation); }
    }
    internal static uint Post(string phase, GameObject emitter, AkCallbackManager.EventCallback callback, object cookie)
    {
        if (!EnsureLoaded()) return 0;
        uint id = AkUnitySoundEngine.PostEvent("Play_Floppy_" + phase, emitter,
            (uint)AkCallbackType.AK_EndOfEvent, callback, cookie);
        if (id == 0 && !warned)
        {
            warned = true;
            FloppyPlugin.Log.LogError("Wwise could not post Floppy event: " + phase);
        }
        if (id != 0) SetVolume(id);
        return id;
    }
    internal static void SetVolume(uint id)
    {
        if (id != 0 && FloppyPlugin.Instance != null)
            AkUnitySoundEngine.SetRTPCValueByPlayingID("User_Volume", FloppyPlugin.Instance.Volume.Value, id);
    }
    internal static void Stop(PcSpeaker speaker) => speaker.StopAll();
    internal static void Dispose()
    {
        if (loaded && AkUnitySoundEngine.IsInitialized()) AkUnitySoundEngine.UnloadBank(bankId, IntPtr.Zero);
        loaded = false;
        bank = null;
    }
}

internal sealed class PcSpeaker : MonoBehaviour
{
    private sealed class Playback
    {
        internal volatile uint Id;
        internal volatile bool Ended;
    }
    private readonly ConcurrentDictionary<uint, Playback> playing = new ConcurrentDictionary<uint, Playback>();
    private GameObject emitter;
    private uint idle;
    private float nextVolumeUpdate;
    private AkCallbackManager.EventCallback ended;
    private void EnsureEmitter()
    {
        if (emitter != null) return;
        emitter = new GameObject("Floppy_Wwise_Emitter");
        emitter.transform.SetParent(transform, false);
        emitter.transform.localPosition = Vector3.up * ModelMetrics.EmitterHeight;
        emitter.AddComponent<AkGameObj>();
        ended = OnEventEnded;
    }
    private uint PlayInternal(string phase)
    {
        EnsureEmitter();
        var playback = new Playback();
        uint id = PcAudio.Post(phase, emitter, ended, playback);
        playback.Id = id;
        if (id != 0 && !playback.Ended) playing.TryAdd(id, playback);
        return id;
    }
    internal void Play(string phase) => PlayInternal(phase);
    internal void StartIdle() { StopIdle(); idle = PlayInternal("idle"); }
    internal void StopIdle() { Stop(idle); idle = 0; }
    private void Stop(uint id)
    {
        if (id != 0 && playing.TryRemove(id, out _)) AkUnitySoundEngine.StopPlayingID(id, 40);
    }
    private void OnEventEnded(object cookie, AkCallbackType type, AkCallbackInfo info)
    {
        if (type != AkCallbackType.AK_EndOfEvent || !(cookie is Playback playback)) return;
        playback.Ended = true;
        if (playback.Id != 0) playing.TryRemove(playback.Id, out _);
    }
    internal void StopAll()
    {
        foreach (uint id in playing.Keys) Stop(id);
        idle = 0;
    }
    private void Update()
    {
        if (Time.unscaledTime < nextVolumeUpdate) return;
        nextVolumeUpdate = Time.unscaledTime + .25f;
        foreach (uint id in playing.Keys) PcAudio.SetVolume(id);
    }
    private void OnDisable() => StopAll();
    private void OnDestroy()
    {
        StopAll();
        if (emitter != null) Destroy(emitter);
    }
}
