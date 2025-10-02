using System;
using System.IO;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TaskMate.Services {
	public static class SoundService {
		// ---- Files ----
		private const string HoverPath = @"Assets\Sounds\hoveringOverButton.wav";
		private const string CreatePath = @"Assets\Sounds\creatingTask.wav";
		private const string CompletePath = @"Assets\Sounds\completedTask.wav";
		private const string PendingPartnerPath = @"Assets\Sounds\pendingPartnerRequest.wav";
		private const string PartnerDisconnectedPath = @"Assets\Sounds\partnerDisconnected.wav";
		private const string SentPath = @"Assets\Sounds\sent.wav";

		// ---- Audio config ----
		private const int DesiredLatencyMs = 75;       // bump a touch to reduce crackle
		private const float VolumeHover = 0.5f;
		private const float VolumeCreate = 0.8f;
		private const float VolumeComplete = 0.8f;
		private const float VolumePending = 0.8f;
		private const float VolumePartnerDisconnected = 0.8f;
		private const float VolumeSent = 0.8f;

		// Hover pitch randomization
		private const float HoverMinSemi = -2.0f;
		private const float HoverMaxSemi = +2.0f;

		// Small flood guard for hover (too many plays per second = artifacts)
		private const int MinHoverIntervalMs = 35;
		private static long _lastHoverTicks;

		// ---- State: one output device + mixer (float 32) ----
		private static readonly object _initLock = new();
		private static WaveOutEvent? _output;
		private static MixingSampleProvider? _mixer; // 32-bit float, ReadFully = true

		// Cached sounds (predecoded into float arrays) for instant one-shots
		private static CachedSound? _hoverCached;
		private static CachedSound? _createCached;
		private static CachedSound? _completeCached;
		private static CachedSound? _pendingCached;
		private static CachedSound? _partnerDiscCached;
		private static CachedSound? _sentCached;

		private static readonly Random _rng = new();
		private static bool _enabled = true;

		public static void Initialize() {
			lock(_initLock) {
				if(_output != null) return;

				// Load/cascade in this order to ensure mixer format is consistent across files
				_hoverCached = TryLoadCached(HoverPath);
				_createCached = TryLoadCached(CreatePath);
				_completeCached = TryLoadCached(CompletePath);
				_pendingCached = TryLoadCached(PendingPartnerPath);
				_partnerDiscCached = TryLoadCached(PartnerDisconnectedPath);
				_sentCached = TryLoadCached(SentPath);

				// Pick a reference format (fallback to 44.1k/mono if none available)
				var fmt = (_hoverCached ?? _createCached ?? _completeCached ?? _pendingCached ?? _partnerDiscCached)?.WaveFormat
						  ?? WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);

				_mixer = new MixingSampleProvider(fmt) { ReadFully = true };
				_output = new WaveOutEvent { DesiredLatency = DesiredLatencyMs };
				_output.Init(_mixer);
				_output.Play(); // keep running; we’ll inject inputs
			}
		}

		public static void Enable(bool enabled) => _enabled = enabled;

		// === Public APIs (unchanged) ===
		public static void PlayHover() {
			if(!_enabled || _hoverCached == null) return;

			// Debounce hover a little to avoid floods and artifacts
			var now = Environment.TickCount64;
			var last = Interlocked.Read(ref _lastHoverTicks);
			if(now - last < MinHoverIntervalMs) return;
			Interlocked.Exchange(ref _lastHoverTicks, now);

			// true pitch shift (tempo preserved) using SMB
			float semi = NextFloat(HoverMinSemi, HoverMaxSemi);
			float factor = (float)Math.Pow(2.0, semi / 12.0);

			var src = new CachedSoundSampleProvider(_hoverCached);
			var smb = new SmbPitchShiftingSampleProvider(src) { PitchFactor = factor };
			var vol = new VolumeSampleProvider(smb) { Volume = VolumeHover };
			AddToMixer(vol);
		}

		public static void PlayTaskCreated() {
			if(!_enabled || _createCached == null) return;
			var vol = new VolumeSampleProvider(new CachedSoundSampleProvider(_createCached)) { Volume = VolumeCreate };
			AddToMixer(vol);
		}

		public static void PlayTaskCompleted() {
			if(!_enabled || _completeCached == null) return;
			var vol = new VolumeSampleProvider(new CachedSoundSampleProvider(_completeCached)) { Volume = VolumeComplete };
			AddToMixer(vol);
		}

		public static void PlayPendingPartnerRequest() {
			if(!_enabled || _pendingCached == null) return;
			var vol = new VolumeSampleProvider(new CachedSoundSampleProvider(_pendingCached)) { Volume = VolumePending };
			AddToMixer(vol);
		}

		public static void PlayPartnerDisconnected() {
			if(!_enabled || _partnerDiscCached == null) return;
			var vol = new VolumeSampleProvider(new CachedSoundSampleProvider(_partnerDiscCached)) { Volume = VolumePartnerDisconnected };
			AddToMixer(vol);
		}
		public static void PlaySent() {
			if(!_enabled || _sentCached == null) return;
			var vol = new VolumeSampleProvider(new CachedSoundSampleProvider(_sentCached)) { Volume = VolumeSent };
			AddToMixer(vol);
		}

		// === Internals ===
		private static void AddToMixer(ISampleProvider input) {
			if(_mixer == null) return;

			// Ensure channel/sample rate match mixer format
			var fmt = _mixer.WaveFormat;
			ISampleProvider source = input;
			if(input.WaveFormat.SampleRate != fmt.SampleRate || input.WaveFormat.Channels != fmt.Channels) {
				source = input
					.ToMonoOrStereo(fmt.Channels)        // helper extension below
					.ToSampleRate(fmt.SampleRate);       // helper extension below
			}

			// Auto-dispose wrapper: removes itself after it finishes playing
			_mixer.AddMixerInput(new AutoDisposeSampleProvider(source));
		}

		private static CachedSound? TryLoadCached(string relativePath) {
			var full = Path.Combine(AppContext.BaseDirectory, relativePath);
			if(!File.Exists(full)) return null;
			try {
				return new CachedSound(full);
			}
			catch {
				return null;
			}
		}

		private static float NextFloat(float min, float max)
			=> (float)(min + (max - min) * _rng.NextDouble());

		// --------- Support types (CachedSound, providers, helpers) ---------

		/// <summary>Holds decoded float samples in memory for instant one-shot playback.</summary>
		private sealed class CachedSound {
			public float[] AudioData { get; }
			public WaveFormat WaveFormat { get; }

			public CachedSound(string fileName) {
				// Read with AudioFileReader (handles WAV/MP3/etc), convert to float32
				using var afr = new AudioFileReader(fileName); // float provider
				WaveFormat = afr.WaveFormat;                   // usually 44.1kHz mono or stereo
				var wholeFile = new float[afr.Length / sizeof(float)];
				int samplesRead = afr.Read(wholeFile, 0, wholeFile.Length);

				if(samplesRead < wholeFile.Length) {
					Array.Resize(ref wholeFile, samplesRead);
				}

				// Optional: convert to mono to reduce clicks with UI SFX & reduce CPU
				if(WaveFormat.Channels > 1) {
					var mono = new float[samplesRead / WaveFormat.Channels];
					int m = 0;
					for(int i = 0; i < samplesRead; i += WaveFormat.Channels) {
						float sum = 0;
						for(int c = 0; c < WaveFormat.Channels; c++) sum += wholeFile[i + c];
						mono[m++] = sum / WaveFormat.Channels;
					}
					AudioData = mono;
					WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(WaveFormat.SampleRate, 1);
				}
				else {
					AudioData = wholeFile;
				}
			}
		}

		/// <summary>Plays from CachedSound (float32) once.</summary>
		private sealed class CachedSoundSampleProvider : ISampleProvider {
			private readonly CachedSound _cached;
			private long _position;
			public CachedSoundSampleProvider(CachedSound cs) { _cached = cs; }
			public WaveFormat WaveFormat => _cached.WaveFormat;
			public int Read(float[] buffer, int offset, int count) {
				var available = _cached.AudioData.Length - _position;
				if(available <= 0) return 0;
				var toCopy = (int)Math.Min(available, count);
				Array.Copy(_cached.AudioData, _position, buffer, offset, toCopy);
				_position += toCopy;
				return toCopy;
			}
		}

		/// <summary>Wraps a one-shot provider and returns 0s after it ends; mixer removes it automatically.</summary>
		private sealed class AutoDisposeSampleProvider : ISampleProvider {
			private readonly ISampleProvider _source;
			private bool _finished;
			public AutoDisposeSampleProvider(ISampleProvider source) { _source = source; }
			public WaveFormat WaveFormat => _source.WaveFormat;
			public int Read(float[] buffer, int offset, int count) {
				if(_finished) return 0;
				int read = _source.Read(buffer, offset, count);
				if(read == 0) _finished = true;
				return read;
			}
		}

		// --- Small resampler/channel helpers ---
		private static ISampleProvider ToMonoOrStereo(this ISampleProvider src, int channels) {
			if(channels == src.WaveFormat.Channels) return src;
			if(channels == 1) return new StereoToMonoSampleProvider(src);
			if(channels == 2) return new MonoToStereoSampleProvider(src);
			// Fallback: if mixer had >2 channels (unlikely), just return as-is
			return src;
		}

		private static ISampleProvider ToSampleRate(this ISampleProvider src, int sampleRate) {
			if(src.WaveFormat.SampleRate == sampleRate) return src;
			// WDL Resampling (NAudio): create a resampler provider
			return new WdlResamplingSampleProvider(src, sampleRate);
		}
	}
}
