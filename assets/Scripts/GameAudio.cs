using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Music and sound effects.
/// - Free roam plays the original theme.
/// - Every job level has its own track: a clip dropped in Assets/audio/levels
///   (m1_l1.ogg … m4_l3.ogg) or, when none is given, a loop synthesised here
///   with its own tempo, key and instruments, getting busier each level.
/// - Effects: motor hum that follows the throttle, checkpoint ding,
///   mission passed / failed stings and an ocean splash.
/// </summary>
public sealed class GameAudio : MonoBehaviour
{
    [SerializeField] public MissionSystem missions;
    [SerializeField] public DroneController drone;
    [SerializeField] public AudioClip freeRoamTheme;
    [Tooltip("Optional tracks, index = mission * 3 + (level - 1). Empty slots are synthesised.")]
    [SerializeField] public AudioClip[] levelTracks = new AudioClip[0];
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.25f;

    private const int SampleRate = 44100;

    private AudioSource musicA, musicB, effects, motor;
    private AudioSource current;
    private int playing = -2; // -1 = free roam, n = level track index
    private float fade = 1f;
    private readonly Dictionary<int, AudioClip> synthesised = new Dictionary<int, AudioClip>();
    private AudioClip ding, passed, failed, splash;
    private Rigidbody droneBody;

    private void Start()
    {
        // The scene's original "Music" source becomes the free roam channel.
        GameObject old = GameObject.Find("Music");
        if (old != null && old != gameObject)
        {
            AudioSource oldSource = old.GetComponent<AudioSource>();
            if (freeRoamTheme == null && oldSource != null) freeRoamTheme = oldSource.clip;
            Destroy(old);
        }

        musicA = NewSource(true);
        musicB = NewSource(true);
        effects = NewSource(false);
        effects.ignoreListenerPause = true;

        ding = Chime(new[] { 880f, 1320f }, 0.09f, 0.35f);
        passed = Chime(new[] { 523f, 659f, 784f, 1047f, 1319f }, 0.11f, 0.8f);
        failed = Sting();
        splash = Splash();

        if (missions != null)
        {
            missions.Banner += OnBanner;
            missions.TargetReached += () => effects.PlayOneShot(ding, 0.7f);
        }
        if (drone != null)
        {
            droneBody = drone.GetComponent<Rigidbody>();
            motor = drone.gameObject.AddComponent<AudioSource>();
            motor.clip = MotorLoop();
            motor.loop = true;
            motor.spatialBlend = 0.6f;
            motor.volume = 0f;
            motor.Play();
            drone.Crashed += reason =>
            {
                if (reason.Contains("ocean")) effects.PlayOneShot(splash, 0.9f);
                if (missions == null || !missions.Active) effects.PlayOneShot(failed, 0.7f); // free roam WASTED
            };
        }
    }

    private AudioSource NewSource(bool loop)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.loop = loop;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = 0f;
        return source;
    }

    private void Update()
    {
        // Level music stays on from the job start until you pick free roam.
        int wanted = missions != null && missions.CurrentMission >= 0
            ? missions.CurrentMission * 3 + missions.CurrentLevel - 1
            : -1;
        if (wanted != playing) SwitchTrack(wanted);

        // Crossfade (unscaled so it keeps going on menus).
        fade = Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / 1.5f);
        AudioSource other = current == musicA ? musicB : musicA;
        if (current != null) current.volume = musicVolume * fade;
        other.volume = musicVolume * (1f - fade);
        if (fade >= 1f && other.isPlaying) other.Stop();

        UpdateMotor();
    }

    private void SwitchTrack(int track)
    {
        playing = track;
        AudioClip clip = track < 0 ? freeRoamTheme : TrackFor(track);
        AudioSource next = current == musicA ? musicB : musicA;
        next.clip = clip;
        next.time = 0f;
        if (clip != null) next.Play();
        current = next;
        fade = 0f;
    }

    private AudioClip TrackFor(int index)
    {
        if (index < levelTracks.Length && levelTracks[index] != null) return levelTracks[index];
        if (!synthesised.TryGetValue(index, out AudioClip clip))
        {
            clip = Compose(index / 3, index % 3 + 1);
            synthesised[index] = clip;
        }
        return clip;
    }

    private void UpdateMotor()
    {
        if (motor == null) return;
        bool paused = Time.timeScale == 0f;
        float speed = droneBody != null ? droneBody.linearVelocity.magnitude : 0f;
        float target = paused || drone.Grounded ? 0f : 0.22f + Mathf.Clamp01(speed / 25f) * 0.18f;
        motor.volume = Mathf.MoveTowards(motor.volume, target, Time.unscaledDeltaTime * 0.8f);
        motor.pitch = Mathf.Lerp(motor.pitch, 0.85f + Mathf.Clamp01(speed / 25f) * 0.6f, Time.unscaledDeltaTime * 3f);
    }

    private void OnBanner(string headline, string detail, bool success)
    {
        if (headline == "MISSION PASSED") effects.PlayOneShot(passed, 0.8f);
        else if (!success) effects.PlayOneShot(failed, 0.7f);
    }

    // ------------------------------------------------------------- music

    private sealed class Style
    {
        public float Bpm;
        public int Root;        // MIDI note of the key
        public int[] Scale;     // semitone steps
        public int[] Chords;    // scale degrees, one per 2 bars
        public int BassWave, LeadWave;
    }

    private static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
    private static readonly int[] Minor = { 0, 2, 3, 5, 7, 8, 10 };
    private static readonly int[] Dorian = { 0, 2, 3, 5, 7, 9, 10 };

    // One mood per mission; levels speed it up and add instruments.
    private static readonly Style[] Styles =
    {
        new Style { Bpm = 96f, Root = 60, Scale = Major, Chords = new[] { 0, 4, 5, 3 }, BassWave = 2, LeadWave = 0 },  // First Flight: bright, calm
        new Style { Bpm = 112f, Root = 62, Scale = Dorian, Chords = new[] { 0, 3, 0, 4 }, BassWave = 1, LeadWave = 2 }, // Express Delivery: funky
        new Style { Bpm = 128f, Root = 57, Scale = Minor, Chords = new[] { 0, 5, 2, 6 }, BassWave = 1, LeadWave = 1 },  // Skyline Race: driving
        new Style { Bpm = 138f, Root = 55, Scale = Minor, Chords = new[] { 0, 0, 5, 4 }, BassWave = 3, LeadWave = 1 },  // Medical Emergency: urgent
    };

    /// <summary>Synthesises an 8-bar loop for one mission level.</summary>
    private static AudioClip Compose(int mission, int level)
    {
        Style style = Styles[Mathf.Clamp(mission, 0, Styles.Length - 1)];
        var random = new System.Random(mission * 31 + level * 7);
        float bpm = style.Bpm + (level - 1) * 6f;
        int stepLength = Mathf.RoundToInt(SampleRate * 60f / bpm / 4f); // one 16th note
        const int bars = 8;
        int steps = bars * 16;
        var data = new float[steps * stepLength];

        int Note(int degree, int octave)
        {
            int d = ((degree % 7) + 7) % 7;
            return style.Root + style.Scale[d] + 12 * (octave + Mathf.FloorToInt(degree / 7f));
        }

        // A short melody motif, repeated with variations.
        var motif = new int[16];
        for (int i = 0; i < motif.Length; i++) motif[i] = random.Next(0, 8) < 3 ? -99 : random.Next(0, 8);

        for (int s = 0; s < steps; s++)
        {
            int bar = s / 16, inBar = s % 16;
            int chord = style.Chords[(bar / 2) % style.Chords.Length];
            int start = s * stepLength;

            // Drums: kick on the beat, snare on 2 and 4, hats on eighths (sixteenths later).
            if (inBar % 4 == 0 && (mission != 0 || inBar % 8 == 0)) Kick(data, start);
            if (inBar == 4 || inBar == 12) Noise(data, start, 0.12f, level >= 2 ? 0.22f : 0.14f, 0.35f);
            if (inBar % (level >= 3 ? 1 : 2) == 0) Noise(data, start, 0.03f, 0.05f, 0.9f);

            // Bass on a groove pattern.
            if (inBar == 0 || inBar == 6 || inBar == 8 || (mission >= 1 && inBar == 14) || (mission >= 2 && inBar % 4 == 2))
                Tone(data, start, stepLength * 2, Midi(Note(chord, -2)), style.BassWave, 0.32f, 6f);

            // Pad: the chord held for two bars.
            if (s % 32 == 0)
                for (int k = 0; k < 3; k++)
                    Tone(data, start, stepLength * 32, Midi(Note(chord + k * 2, 0)), 0, 0.05f, 0.4f);

            // Arpeggio from level 2.
            if (level >= 2 && inBar % 2 == 0)
                Tone(data, start, stepLength, Midi(Note(chord + (inBar / 2 % 3) * 2, 1)), 2, 0.07f, 12f);

            // Lead melody from level 3 (and from the start on racing missions).
            if ((level >= 3 || mission >= 2) && inBar % 2 == 0)
            {
                int m = motif[(inBar / 2 + bar * 3) % motif.Length];
                if (m != -99) Tone(data, start, stepLength * 2, Midi(Note(chord + m, 1)), style.LeadWave, 0.08f, 5f);
            }
        }

        // Gentle limiter so layers never clip.
        float peak = 0.001f;
        foreach (float v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
        float gain = 0.9f / peak;
        for (int i = 0; i < data.Length; i++) data[i] = (float)System.Math.Tanh(data[i] * gain * 1.2f) * 0.85f;

        AudioClip clip = AudioClip.Create($"Level_M{mission + 1}_L{level}", data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

    /// <summary>Adds a note with a fast attack and exponential decay; wraps so loops stay seamless.</summary>
    private static void Tone(float[] data, int start, int length, float freq, int wave, float amp, float decay)
    {
        double phase = 0, step = freq / SampleRate;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            float env = Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-t * decay) * Mathf.Min(1f, (length - i) / 400f);
            float p = (float)(phase - System.Math.Floor(phase));
            float v = wave switch
            {
                1 => p < 0.5f ? 0.6f : -0.6f,          // square
                2 => (2f * p - 1f) * 0.7f,              // saw
                3 => 1f - 4f * Mathf.Abs(p - 0.5f),     // triangle
                _ => Mathf.Sin(p * Mathf.PI * 2f),      // sine
            };
            data[(start + i) % data.Length] += v * env * amp;
            phase += step;
        }
    }

    private static void Kick(float[] data, int start)
    {
        int length = SampleRate / 5;
        double phase = 0;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            phase += (50f + 110f * Mathf.Exp(-t * 30f)) / SampleRate;
            data[(start + i) % data.Length] += Mathf.Sin((float)phase * Mathf.PI * 2f) * Mathf.Exp(-t * 14f) * 0.5f;
        }
    }

    private static readonly System.Random NoiseSource = new System.Random(5);

    private static void Noise(float[] data, int start, float seconds, float amp, float brightness)
    {
        int length = Mathf.RoundToInt(seconds * SampleRate);
        float last = 0f;
        for (int i = 0; i < length; i++)
        {
            float white = (float)NoiseSource.NextDouble() * 2f - 1f;
            float v = brightness > 0.5f ? white - last : Mathf.Lerp(last, white, brightness); // hats: high-pass, snare: low-pass
            last = white;
            data[(start + i) % data.Length] += v * amp * Mathf.Exp(-i / (float)length * 5f);
        }
    }

    // ----------------------------------------------------------- effects

    private static AudioClip Chime(float[] notes, float gap, float seconds)
    {
        var data = new float[Mathf.RoundToInt(seconds * SampleRate + gap * notes.Length * SampleRate)];
        for (int n = 0; n < notes.Length; n++)
            Tone(data, Mathf.RoundToInt(n * gap * SampleRate), Mathf.RoundToInt(seconds * SampleRate), notes[n], 0, 0.4f, 6f);
        return Clip("Chime", data);
    }

    private static AudioClip Sting()
    {
        var data = new float[SampleRate];
        float[] notes = { 392f, 370f, 349f, 262f };
        for (int n = 0; n < notes.Length; n++)
            Tone(data, n * SampleRate / 6, SampleRate / 3, notes[n], 2, 0.3f, 4f);
        return Clip("Wasted", data);
    }

    private static AudioClip Splash()
    {
        var data = new float[SampleRate];
        Noise(data, 0, 0.9f, 0.9f, 0.08f);
        return Clip("Splash", data);
    }

    private static AudioClip MotorLoop()
    {
        // Two slightly detuned rotor tones plus airflow noise; 1 s loops cleanly at whole-number frequencies.
        var data = new float[SampleRate];
        var random = new System.Random(9);
        float air = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)SampleRate;
            float saw1 = 2f * (t * 180f % 1f) - 1f;
            float saw2 = 2f * (t * 183f % 1f) - 1f;
            air = Mathf.Lerp(air, (float)random.NextDouble() * 2f - 1f, 0.15f);
            data[i] = saw1 * 0.25f + saw2 * 0.25f + Mathf.Sin(t * 360f * Mathf.PI * 2f) * 0.2f + air * 0.35f;
        }
        return Clip("Motor", data);
    }

    private static AudioClip Clip(string name, float[] data)
    {
        AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
