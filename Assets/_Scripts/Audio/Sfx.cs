using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

// One sound: a clip, or a part of one. Several of the audio files hold more than one sound (a row of footsteps, a
// handful of snarls), so Start and Length pick the part that is played. Length 0 plays from Start to the end.
// A sound with no clip is silent: every hook works without one, so a clip can be dropped in later.
[Serializable]
public class Sfx
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Pitch: random between these two values.")]
    public Vector2 pitch = Vector2.one;
    [Tooltip("Seconds into the clip where the sound starts.")]
    public float start;
    [Tooltip("Seconds played from Start. 0 = to the end of the clip.")]
    public float length;
    [Tooltip("Seconds of fade at the end of a part, so it does not click.")]
    public float fadeOut = 0.04f;

    public bool HasClip => clip != null;

    // Seconds the sound lasts at pitch 1.
    public float Duration
    {
        get
        {
            if (clip == null) return 0f;
            float from = Mathf.Clamp(start, 0f, clip.length);
            return length > 0f ? Mathf.Min(length, clip.length - from) : clip.length - from;
        }
    }

    // A random sound from a set that has a clip, or null. Avoid: the index not to pick again when there is a choice.
    public static Sfx Pick(Sfx[] set, ref int last)
    {
        if (set == null || set.Length == 0) return null;
        int index = UnityEngine.Random.Range(0, set.Length);
        if (set.Length > 1 && index == last) index = (index + 1) % set.Length;
        for (int i = 0; i < set.Length; i++)
        {
            var s = set[(index + i) % set.Length];
            if (s == null || s.clip == null) continue;
            last = (index + i) % set.Length;
            return s;
        }
        return null;
    }

    public static Sfx Pick(Sfx[] set)
    {
        int last = -1;
        return Pick(set, ref last);
    }
}

// A looping sound. With a Length, only that part of the clip loops: two sources take turns and cross-fade over
// Overlap seconds, which also turns a short swell (the portal hum) into a steady pulse.
[Serializable]
public class SfxLoop
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    public float pitch = 1f;
    [Tooltip("Seconds into the clip where the looped part starts.")]
    public float start;
    [Tooltip("Seconds of the looped part. 0 = the whole clip, looped by the AudioSource.")]
    public float length;
    [Tooltip("Seconds the end of one pass overlaps the start of the next.")]
    public float overlap = 0.5f;
}

// Plays an Sfx on one AudioSource and stops it at the end of its part. Tick it every frame.
public class SfxVoice
{
    public readonly AudioSource source;
    // Multiplier on top of the sound's own volume (occlusion, sprint).
    public float gain = 1f;

    public bool Active { get; private set; }
    // On its way out (FadeOut was called).
    public bool FadingOut => Active && fadeOutRate > 0f;
    public Sfx Current { get; private set; }
    public float StartedAt { get; private set; }
    // Whatever the caller wants to remember about what is playing (a role name, a kind).
    public int tag;

    float baseVolume;
    float end;
    bool part;
    float fade;
    float fadeOutRate;
    float fadeOutLevel = 1f;

    public SfxVoice(AudioSource source)
    {
        this.source = source;
        source.playOnAwake = false;
        source.loop = false;
    }

    public bool Play(Sfx sfx, float volumeScale = 1f, float pitchScale = 1f)
    {
        if (sfx == null || sfx.clip == null || source == null || !source.isActiveAndEnabled) return false;

        source.Stop();
        source.loop = false;
        source.clip = sfx.clip;
        Vector2 range = sfx.pitch;
        if (range.x <= 0f) range.x = 1f;
        if (range.y <= 0f) range.y = range.x;
        float pitch = Mathf.Max(0.05f, UnityEngine.Random.Range(range.x, range.y) * pitchScale);
        source.pitch = pitch;

        float clipLength = sfx.clip.length;
        float from = Mathf.Clamp(sfx.start, 0f, Mathf.Max(0f, clipLength - 0.01f));
        end = sfx.length > 0f ? Mathf.Min(from + sfx.length, clipLength) : clipLength;
        part = end < clipLength - 0.005f;
        fade = Mathf.Max(0f, sfx.fadeOut);

        baseVolume = sfx.volume * volumeScale;
        fadeOutRate = 0f;
        fadeOutLevel = 1f;
        source.volume = baseVolume * gain;
        source.time = from;
        source.Play();
        if (part) source.SetScheduledEndTime(AudioSettings.dspTime + (end - from) / pitch + 0.02);

        Current = sfx;
        StartedAt = Time.unscaledTime;
        Active = true;
        return true;
    }

    public void Tick()
    {
        if (!Active) return;
        if (source == null)
        {
            Active = false;
            return;
        }

        if (!source.isPlaying)
        {
            // held by the paused listener: it carries on when the game does
            if (AudioListener.pause && !source.ignoreListenerPause) return;
            Active = false;
            return;
        }

        float time = source.time;
        if (part && time >= end)
        {
            Stop();
            return;
        }

        float level = 1f;
        if (part && fade > 0f) level = Mathf.Clamp01((end - time) / fade);
        if (fadeOutRate > 0f)
        {
            fadeOutLevel -= fadeOutRate * Time.unscaledDeltaTime;
            if (fadeOutLevel <= 0f)
            {
                Stop();
                return;
            }
            level *= fadeOutLevel;
        }
        source.volume = baseVolume * gain * level;
    }

    // Fades the sound out over a number of seconds and stops it.
    public void FadeOut(float seconds)
    {
        if (!Active) return;
        if (seconds <= 0f) Stop();
        else fadeOutRate = 1f / seconds;
    }

    public void Stop()
    {
        if (source != null) source.Stop();
        Active = false;
    }
}

// Settings shared by every 3D source.
public static class Sfx3D
{
    static readonly Dictionary<long, AnimationCurve> curves = new Dictionary<long, AnimationCurve>();

    // Full 3D with a logarithmic rolloff (volume = Min Distance / distance) that is faded to silence over the last
    // quarter of Max Distance. Unity's own Logarithmic mode never reaches zero: past Max Distance the sound just stops
    // getting quieter, so every zombie on the map would be faintly audible.
    public static void Apply(AudioSource source, float minDistance, float maxDistance, AudioMixerGroup group)
    {
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.spread = 0f;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.rolloffMode = AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff(minDistance, maxDistance));
        source.outputAudioMixerGroup = group;
    }

    // Partly 2D close up, fully 3D from Near Distance outwards: a sound that must cut through small speakers when
    // its source is next to the listener, and still comes from the right direction.
    public static void ApplyNearBlend(AudioSource source, float nearBlend, float nearDistance)
    {
        float max = Mathf.Max(0.1f, source.maxDistance);
        float near = Mathf.Clamp(nearDistance, 0.1f, max) / max;
        nearBlend = Mathf.Clamp01(nearBlend);
        source.SetCustomCurve(AudioSourceCurveType.SpatialBlend, new AnimationCurve(
            new Keyframe(0f, nearBlend, 0f, 0f),
            new Keyframe(near * 0.35f, nearBlend, 0f, 0f),
            new Keyframe(near, 1f, 0f, 0f),
            new Keyframe(1f, 1f, 0f, 0f)));
    }

    public static void Apply2D(AudioSource source, AudioMixerGroup group)
    {
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.outputAudioMixerGroup = group;
    }

    public static AnimationCurve Rolloff(float minDistance, float maxDistance)
    {
        minDistance = Mathf.Max(0.1f, minDistance);
        maxDistance = Mathf.Max(minDistance + 0.1f, maxDistance);
        long key = ((long)Mathf.RoundToInt(minDistance * 100f) << 32) | (uint)Mathf.RoundToInt(maxDistance * 100f);
        if (curves.TryGetValue(key, out AnimationCurve curve)) return curve;

        const int steps = 24;
        var keys = new Keyframe[steps + 2];
        keys[0] = new Keyframe(0f, 1f);
        float ratio = maxDistance / minDistance;
        for (int i = 0; i <= steps; i++)
        {
            float distance = minDistance * Mathf.Pow(ratio, i / (float)steps);
            float value = minDistance / distance;
            float taper = Mathf.InverseLerp(maxDistance, maxDistance * 0.75f, distance);
            keys[i + 1] = new Keyframe(distance / maxDistance, value * Mathf.SmoothStep(0f, 1f, taper));
        }
        curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
        curves[key] = curve;
        return curve;
    }

    // The volume the rolloff gives at a distance (for tests and for skipping sounds nobody can hear).
    public static float VolumeAt(float distance, float minDistance, float maxDistance)
    {
        return Mathf.Clamp01(Rolloff(minDistance, maxDistance).Evaluate(Mathf.Clamp01(distance / Mathf.Max(0.1f, maxDistance))));
    }
}
