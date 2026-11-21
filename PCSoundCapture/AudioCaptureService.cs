using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Extras;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Vosk;

namespace PCSoundCapture;

/// <summary>
/// Owns one recording session. NAudio.Extras keeps the system and microphone on
/// one real-time clock; the speech worker never blocks the audio-writing thread.
/// </summary>
internal sealed class AudioCaptureService
{
    private const int MixRate = 48000;
    private const int MixChannels = 2;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Session? _session;

    public event EventHandler<CaptureCompletedEventArgs>? Completed;
    public event EventHandler<TranscriptEventArgs>? TranscriptUpdated;

    public bool IsActive => Volatile.Read(ref _session) is not null;
    public bool IsStopping => Volatile.Read(ref _session)?.IsStopping ?? false;
    public long CapturedBytes => Volatile.Read(ref _session)?.CapturedBytes ?? _lastBytes;
    private long _lastBytes;

    public async Task StartAsync(CaptureOptions options)
    {
        await _gate.WaitAsync();
        Session? session = null;
        try
        {
            if (_session is not null)
                throw new InvalidOperationException("An audio listening session is already running.");
            if (options.Source == CaptureSource.EntirePc && options.OutputDevice is null)
                throw new InvalidOperationException("Choose an output device.");
            if (options.Source == CaptureSource.SelectedApplication && options.ProcessId is null)
                throw new InvalidOperationException("Choose an application to capture.");
            if ((options.IncludeMicrophone || options.Source == CaptureSource.MicrophoneOnly)
                && options.MicrophoneDevice is null)
                throw new InvalidOperationException("Choose a microphone.");
            if (options.Transcribe && !Directory.Exists(options.ModelDirectory))
                throw new DirectoryNotFoundException("Choose the extracted Vosk model folder. The ZIP file itself cannot be used.");

            if (options.SaveRecording || options.SaveTranscript)
                Directory.CreateDirectory(options.OutputDirectory);
            string stem = $"pc-audio-{DateTime.Now:yyyyMMdd-HHmmss-fff}";
            string? audioPath = options.SaveRecording
                ? Path.Combine(options.OutputDirectory, stem + ".wav") : null;
            string? transcriptPath = options.Transcribe && options.SaveTranscript
                ? Path.Combine(options.OutputDirectory, stem + ".txt") : null;

            session = new Session(audioPath, transcriptPath);
            // Process loopback is asynchronous; do not use Build() for this source.
            if (options.Source == CaptureSource.SelectedApplication)
            {
                session.Recorders.Add(await new WasapiRecorderBuilder()
                    .WithProcessLoopback((uint)options.ProcessId!.Value)
                    .BuildAsync());
            }
            else if (options.Source == CaptureSource.EntirePc)
            {
                session.Recorders.Add(new WasapiRecorderBuilder()
                    .WithDevice(options.OutputDevice!)
                    .WithLoopbackCapture()
                    .Build());
            }

            if (options.IncludeMicrophone || options.Source == CaptureSource.MicrophoneOnly)
            {
                session.Recorders.Add(new WasapiRecorderBuilder()
                    .WithDevice(options.MicrophoneDevice!)
                    .Build());
            }

            if (options.Transcribe)
            {
                // Loading a model can be expensive; avoid freezing the WinForms UI.
                session.Model = await Task.Run(() => new Model(options.ModelDirectory));
                session.PcmChannel = Channel.CreateBounded<byte[]>(
                    new BoundedChannelOptions(64)
                    {
                        SingleReader = true,
                        SingleWriter = true,
                        FullMode = BoundedChannelFullMode.Wait
                    });
            }

            var mixer = new RealtimeCaptureMixer(
                WaveFormat.CreateIeeeFloatWaveFormat(MixRate, MixChannels));
            session.Mixer = mixer;
            float inputVolume = session.Recorders.Count > 1 ? 0.5f : 1.0f;
            foreach (WasapiRecorder recorder in session.Recorders)
            {
                var input = mixer.AddInput(recorder.WaveFormat,
                    tap: source => new VolumeSampleProvider(source) { Volume = inputVolume });
                recorder.DataAvailable += (data, flags, devicePosition, qpcPosition) =>
                {
                    try { input.AddSamples(data); }
                    catch (Exception ex) { RequestUnexpectedStop(session, ex); }
                };
                recorder.RecordingStopped += (_, args) =>
                {
                    if (!session.IsStopping)
                        RequestUnexpectedStop(session,
                            args.Exception ?? new IOException("An audio source stopped unexpectedly."));
                };
            }

            if (audioPath is not null)
                session.Writer = new WaveFileWriter(audioPath, new WaveFormat(MixRate, 16, MixChannels));
            Interlocked.Exchange(ref _lastBytes, 0);
            mixer.Start();
            // Register before sources start, so a stopped notification has a session.
            Volatile.Write(ref _session, session);
            foreach (WasapiRecorder recorder in session.Recorders)
                recorder.StartRecording();

            if (session.PcmChannel is not null)
                session.TranscriptionTask = Task.Run(() => TranscribeAsync(session));
            session.PumpTask = Task.Run(() => PumpAsync(session));
        }
        catch
        {
            if (session is not null)
            {
                session.MarkStopping();
                await DisposeSessionAsync(session);
                if (session.AudioPath is not null)
                    try { File.Delete(session.AudioPath); } catch { /* best effort */ }
                if (session.TranscriptPath is not null)
                    try { File.Delete(session.TranscriptPath); } catch { /* best effort */ }
            }
            if (session is not null)
                Volatile.Write(ref _session, null);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            Session? session = _session;
            if (session is null || session.IsStopping) return;
            session.MarkStopping();
            try
            {
                // Stop()/Dispose() can join capture threads; never block the UI.
                await Task.Run(() =>
                {
                    foreach (WasapiRecorder recorder in session.Recorders)
                    {
                        try { recorder.StopRecording(); }
                        catch (Exception ex) { session.AddError(ex); }
                    }
                });
                if (session.PumpTask is not null)
                {
                    try { await session.PumpTask; }
                    catch (Exception ex) { session.AddError(ex); }
                }
                session.PcmChannel?.Writer.TryComplete();
                if (session.TranscriptionTask is not null)
                {
                    try { await session.TranscriptionTask; }
                    catch (Exception ex) { session.AddError(ex); }
                }
            }
            finally
            {
                await DisposeSessionAsync(session);
                Interlocked.Exchange(ref _lastBytes, session.CapturedBytes);
                Volatile.Write(ref _session, null);
                Completed?.Invoke(this, new CaptureCompletedEventArgs(
                    session.AudioPath, session.TranscriptPath, session.CapturedBytes,
                    session.DroppedTranscriptChunks, session.Error));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RequestUnexpectedStop(Session session, Exception error)
    {
        session.AddError(error);
        if (session.IsStopping) return;
        // Never invoke StopAsync directly on an audio capture callback or pump thread.
        _ = Task.Run(async () =>
        {
            try { await StopAsync(); }
            catch (Exception ex) { session.AddError(ex); }
        });
    }

    private static async Task DisposeSessionAsync(Session session)
    {
        session.PcmChannel?.Writer.TryComplete();
        if (session.PumpTask is not null)
        {
            try { await session.PumpTask; } catch (Exception ex) { session.AddError(ex); }
        }
        if (session.TranscriptionTask is not null)
        {
            try { await session.TranscriptionTask; } catch (Exception ex) { session.AddError(ex); }
        }
        await Task.Run(() =>
        {
            foreach (var recorder in session.Recorders)
            {
                try { recorder.StopRecording(); } catch { /* may not have started */ }
                try { recorder.Dispose(); } catch (Exception ex) { session.AddError(ex); }
            }
        });
        try { session.Writer?.Dispose(); } catch (Exception ex) { session.AddError(ex); }
        try { session.Model?.Dispose(); } catch (Exception ex) { session.AddError(ex); }
    }

    private async Task PumpAsync(Session session)
    {
        try
        {
            // 100ms of 48kHz stereo output; do not let the pump run ahead of the clock.
            float[] floats = new float[MixRate * MixChannels / 10];
            byte[] output = new byte[floats.Length * sizeof(short)];
            int downsamplePhase = 0;
            float monoSum = 0;
            while (!session.IsStopping)
            {
                int count = session.Mixer!.Read(floats, 0, floats.Length);
                if (count == 0)
                {
                    await Task.Delay(5);
                    continue;
                }
                // Float -> 16-bit PCM stereo for WAV; additionally downsample the
                // already-mixed stereo signal to mono 16kHz for Vosk.
                byte[]? speech = session.PcmChannel is not null
                    ? new byte[(count / MixChannels / 3 + 2) * sizeof(short)] : null;
                int speechBytes = 0;
                for (int i = 0; i < count; i += 2)
                {
                    float left = Math.Clamp(floats[i], -1f, 1f);
                    float right = Math.Clamp(floats[i + 1], -1f, 1f);
                    short l = (short)(left * 32767f);
                    short r = (short)(right * 32767f);
                    int j = i * sizeof(short);
                    output[j] = (byte)l;
                    output[j + 1] = (byte)(l >> 8);
                    output[j + 2] = (byte)r;
                    output[j + 3] = (byte)(r >> 8);
                    if (speech is null) continue;
                    monoSum += (left + right) / 2;
                    if (++downsamplePhase == 3)
                    {
                        short s = (short)(Math.Clamp(monoSum / 3, -1f, 1f) * 32767f);
                        speech[speechBytes++] = (byte)s;
                        speech[speechBytes++] = (byte)(s >> 8);
                        downsamplePhase = 0;
                        monoSum = 0;
                    }
                }
                if (session.Writer is not null)
                {
                    session.Writer.Write(output, 0, count * sizeof(short));
                    Interlocked.Add(ref session.CapturedBytesField, count * sizeof(short));
                }
                if (speechBytes > 0 && session.PcmChannel is not null)
                {
                    if (speechBytes != speech!.Length) Array.Resize(ref speech, speechBytes);
                    if (!session.PcmChannel.Writer.TryWrite(speech))
                        Interlocked.Increment(ref session.DroppedChunksField);
                }
            }
        }
        catch (Exception ex)
        {
            RequestUnexpectedStop(session, ex);
        }
        finally
        {
            session.PcmChannel?.Writer.TryComplete();
        }
    }

    private async Task TranscribeAsync(Session session)
    {
        if (session.PcmChannel is null || session.Model is null)
            return;
        using var recognizer = new VoskRecognizer(session.Model, 16000f);
        StreamWriter? writer = session.TranscriptPath is null ? null
            : new StreamWriter(session.TranscriptPath, false,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            await foreach (byte[] pcm in session.PcmChannel.Reader.ReadAllAsync())
            {
                if (recognizer.AcceptWaveform(pcm, pcm.Length))
                {
                    string spoken = ReadJsonText(recognizer.Result(), "text");
                    if (!string.IsNullOrWhiteSpace(spoken))
                    {
                        if (writer is not null)
                        {
                            await writer.WriteLineAsync($"[{DateTime.Now:HH:mm:ss}] {spoken}");
                            await writer.FlushAsync();
                        }
                        TranscriptUpdated?.Invoke(this, new TranscriptEventArgs(spoken, true));
                    }
                }
                else
                {
                    string partial = ReadJsonText(recognizer.PartialResult(), "partial");
                    TranscriptUpdated?.Invoke(this, new TranscriptEventArgs(partial, false));
                }
            }
            string finalText = ReadJsonText(recognizer.FinalResult(), "text");
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                if (writer is not null)
                    await writer.WriteLineAsync($"[{DateTime.Now:HH:mm:ss}] {finalText}");
                TranscriptUpdated?.Invoke(this, new TranscriptEventArgs(finalText, true));
            }
            TranscriptUpdated?.Invoke(this, new TranscriptEventArgs(string.Empty, false));
        }
        finally
        {
            if (writer is not null) await writer.DisposeAsync();
        }
    }

    private static string ReadJsonText(string json, string name)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(name, out JsonElement property)
            ? property.GetString() ?? string.Empty : string.Empty;
    }

    private sealed class Session(string? audioPath, string? transcriptPath)
    {
        private int _stopping;
        private readonly object _errorLock = new();
        public readonly List<WasapiRecorder> Recorders = [];
        public string? AudioPath { get; } = audioPath;
        public string? TranscriptPath { get; } = transcriptPath;
        public RealtimeCaptureMixer? Mixer;
        public WaveFileWriter? Writer;
        public Model? Model;
        public Channel<byte[]>? PcmChannel;
        public Task? PumpTask;
        public Task? TranscriptionTask;
        public long CapturedBytesField;
        public long DroppedChunksField;
        public long CapturedBytes => Interlocked.Read(ref CapturedBytesField);
        public long DroppedTranscriptChunks => Interlocked.Read(ref DroppedChunksField);
        public Exception? Error { get { lock (_errorLock) return _error; } }
        private Exception? _error;
        public bool IsStopping => Volatile.Read(ref _stopping) != 0;
        public void MarkStopping() => Interlocked.Exchange(ref _stopping, 1);
        public void AddError(Exception ex) { lock (_errorLock) _error ??= ex; }
    }
}
