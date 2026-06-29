using System.Diagnostics;

namespace ContactCenter.Api.Tts;

/// <summary>
/// TTS via Piper (lokale neural-TTS) + sox. Roept de gebundelde piper-binary aan
/// (LD_LIBRARY_PATH wijst naar de map met de bijbehorende libs) en hersamplet de
/// 22050Hz-output met sox naar 8kHz mono — het formaat dat Asterisk afspeelt.
/// Pad-config: Piper:Dir (binary + voices/) en Sounds:CustomDir (uitvoermap = gedeeld volume).
/// </summary>
public sealed class PiperTtsService : ITtsService
{
    private const string FallbackVoice = "nl_NL-pim-medium";

    private readonly string _piperDir;
    private readonly string _voicesDir;
    private readonly string _piperBinary;
    private readonly string? _customDir;
    private readonly ILogger<PiperTtsService> _logger;
    private readonly Lazy<IReadOnlyList<string>> _voices;

    public PiperTtsService(IConfiguration config, ILogger<PiperTtsService> logger)
    {
        _logger = logger;
        _piperDir = config["Piper:Dir"] ?? "";
        _customDir = config["Sounds:CustomDir"];
        _voicesDir = _piperDir.Length == 0 ? "" : Path.Combine(_piperDir, "voices");
        _piperBinary = _piperDir.Length == 0 ? "" : Path.Combine(_piperDir, "piper");
        _voices = new Lazy<IReadOnlyList<string>>(DiscoverVoices);
    }

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(_customDir)
        && _piperBinary.Length > 0
        && File.Exists(_piperBinary);

    public IReadOnlyList<string> AvailableVoices => _voices.Value;

    public string DefaultVoice => AvailableVoices.Count > 0 ? AvailableVoices[0] : FallbackVoice;

    public bool OutputExists(string outputName) =>
        !string.IsNullOrWhiteSpace(_customDir)
        && File.Exists(Path.Combine(_customDir, $"{outputName}.wav"));

    private IReadOnlyList<string> DiscoverVoices()
    {
        if (_voicesDir.Length == 0 || !System.IO.Directory.Exists(_voicesDir))
            return [];
        return [.. System.IO.Directory.EnumerateFiles(_voicesDir, "*.onnx")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .OrderBy(n => n)];
    }

    public async Task<bool> SynthesizeAsync(string text, string voice, string outputName, CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(text))
            return false;

        var chosen = AvailableVoices.Contains(voice) ? voice : DefaultVoice;
        var modelPath = Path.Combine(_voicesDir, $"{chosen}.onnx");
        if (!File.Exists(modelPath))
        {
            _logger.LogWarning("TTS-stem '{Voice}' niet gevonden ({Path})", chosen, modelPath);
            return false;
        }

        System.IO.Directory.CreateDirectory(_customDir!);
        var rawWav = Path.Combine(Path.GetTempPath(), $"tts-{Guid.NewGuid():N}.wav");
        var finalWav = Path.Combine(_customDir!, $"{outputName}.wav");
        try
        {
            if (!await RunPiperAsync(text, modelPath, rawWav, ct)) return false;
            if (!await RunSoxAsync(rawWav, finalWav, ct)) return false;
            _logger.LogInformation("TTS gegenereerd: {File} (stem {Voice})", finalWav, chosen);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TTS-synthese mislukt voor '{Output}'", outputName);
            return false;
        }
        finally
        {
            try { if (File.Exists(rawWav)) File.Delete(rawWav); } catch { /* best effort */ }
        }
    }

    private Task<bool> RunPiperAsync(string text, string modelPath, string outputWav, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _piperBinary,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(modelPath);
        psi.ArgumentList.Add("--output_file");
        psi.ArgumentList.Add(outputWav);
        psi.Environment["LD_LIBRARY_PATH"] = _piperDir; // de libs staan naast de binary
        return RunAsync(psi, text, "piper", ct);
    }

    private Task<bool> RunSoxAsync(string inputWav, string outputWav, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sox",
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // 8kHz mono 16-bit — wat Asterisk afspeelt.
        foreach (var arg in new[] { inputWav, "-r", "8000", "-c", "1", "-b", "16", outputWav })
            psi.ArgumentList.Add(arg);
        return RunAsync(psi, stdin: null, "sox", ct);
    }

    private async Task<bool> RunAsync(ProcessStartInfo psi, string? stdin, string name, CancellationToken ct)
    {
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"{name} kon niet worden gestart");
        if (stdin is not null)
        {
            await proc.StandardInput.WriteAsync(stdin.AsMemory(), ct);
            proc.StandardInput.Close();
        }
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            _logger.LogWarning("{Name} faalde (exit {Code}): {Err}", name, proc.ExitCode, stderr.Trim());
            return false;
        }
        return true;
    }
}
