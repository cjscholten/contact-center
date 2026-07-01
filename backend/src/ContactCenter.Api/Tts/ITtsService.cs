namespace ContactCenter.Api.Tts;

/// <summary>
/// Zet tekst om naar een door Asterisk afspeelbaar 8kHz-mono-WAV onder de gedeelde sounds-map
/// (referentie "sound:custom/&lt;naam&gt;"). Lokaal (zonder Piper/sounds-map) is de dienst
/// uitgeschakeld en zijn de aanroepen no-ops, zodat de backend ook zonder TTS blijft werken.
/// </summary>
public interface ITtsService
{
    /// <summary>Is TTS beschikbaar (piper-binary + sounds-map aanwezig)?</summary>
    bool IsEnabled { get; }

    /// <summary>De gebundelde stemmen (modelbestandsnamen zonder extensie).</summary>
    IReadOnlyList<string> AvailableVoices { get; }

    /// <summary>De standaardstem (eerste beschikbare, of een vaste fallback).</summary>
    string DefaultVoice { get; }

    /// <summary>Bestaat er al een gegenereerd bestand met deze naam (zonder extensie)?</summary>
    bool OutputExists(string outputName);

    /// <summary>
    /// Genereert {outputName}.wav (8kHz mono) in de sounds-map. Geeft false bij een uitgeschakelde
    /// dienst, lege tekst of een fout (de aanroeper valt dan terug op een standaardprompt).
    /// </summary>
    Task<bool> SynthesizeAsync(string text, string voice, string outputName, CancellationToken ct = default);

    /// <summary>
    /// Genereert een tijdelijk 8kHz-mono-WAV (zoals de beller het hoort) en geeft de bytes terug,
    /// voor een voorbeeld in de beheer-UI. Schrijft niets in de sounds-map. Null bij een
    /// uitgeschakelde dienst, lege tekst of een fout.
    /// </summary>
    Task<byte[]?> SynthesizePreviewAsync(string text, string voice, CancellationToken ct = default);
}
