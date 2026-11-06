# Kaleido Wallpaper voor Windows

De caleidoscoop uit de Psychedelic Visualizer als live bureaubladachtergrond voor Windows 10 (2004+) en
Windows 11. De achtergrond reageert op het geluid van één app (Apple Music, Spotify, een browser, …) of op al
het computergeluid, en kan de hoes van wat er speelt in het midden tonen en de kleuren ervan overnemen.

## Bouwen

Vereisten: de [.NET 8 SDK](https://dotnet.microsoft.com/download) en de WebView2 Runtime (standaard
aanwezig op Windows 10/11).

```bat
build.cmd
publish\KaleidoWallpaper.exe
```

Of open `KaleidoWallpaper.csproj` in Visual Studio of Rider en druk op F5.

## Instellingen (pictogram in het systeemvak)

| Menu | Wat het doet |
|---|---|
| React to | **All computer audio**, of een app die geluid heeft gemaakt (speel eerst iets af om hem in de lijst te zien). Je keuze blijft bewaard; start de app later, dan pakt de achtergrond hem vanzelf op. |
| Album art mode | Hoes in het midden en de kleuren ervan. Werkt met elke app die "nu afspelen" aan Windows doorgeeft, zoals Apple Music en Spotify. |
| Album colors | **Natural**, **Vibrant** of **Max**: hoe fel de kleuren uit de hoes worden |
| Colors | **From album (default)**, of **Custom**: je eigen vier kleuren (ook zonder albumhoes-modus). Kant-en-klare reeksen: Sunset, Ocean, Neon, Forest, Ice. Aanpassen via **Edit custom colors…** |
| Response | Smooth (zoals de browser), Fast of Instant |
| Beat effect | **Off**, **Subtle bounce**, **Bounce** of **Color switch** (bij een klap direct de volgende kleur in plaats van een schok) |
| Sensitivity, Trails, Ring speed, Reverse ring rotation | Zelfde betekenis als in de visualizer |
| Quality, Frame rate | Scherpte en vloeiendheid tegenover stroomverbruik |
| Pause | Je gewone achtergrond komt terug |
| Launch at sign-in | Zet de app in de lijst Opstarten van Windows |

Instellingen staan in `%APPDATA%\KaleidoWallpaper\settings.json`.

## Hoe het werkt

- **Achter de iconen**: de app hangt zijn venster in de bureaubladvensters van Explorer. Op Windows 11 24H2+
  is dat een *layered* kindvenster van `Progman`, direct onder de iconen. Op oudere versies is het de
  klassieke `WorkerW`-laag. De app herkent zelf welke opbouw je pc heeft en hangt zichzelf terug als
  Explorer herstart.
- **Geluid per app**: WASAPI *process loopback*. Dit neemt ook de hulpprocessen van een app mee, zodat
  browsers gewoon werken.
- **Albumhoes**: via de mediabediening van Windows (dezelfde info als in het volumepaneel). Geeft een speler
  geen hoes door, dan zoekt de app hem op in de openbare iTunes-catalogus.
- **Weergave**: exact dezelfde `kaleido.html` als in de Mac-versie, in WebView2.

## Bestanden

- `Core/`: geluid (`AudioCapture`, `AudioApps`, `AudioInterop`), bureaublad (`DesktopHost`, `Native`),
  `SpectrumAnalyzer` (geeft exact dezelfde waarden als de Web Audio AnalyserNode) en `Settings`
- `TrayApp.cs`: systeemvak en menu, `WallpaperForm.cs`: venster per scherm, `MediaArtwork.cs`: albumhoes,
  `ColorEditorForm.cs`: venster voor je eigen kleuren
- `wwwroot/kaleido.html`: de caleidoscoop
