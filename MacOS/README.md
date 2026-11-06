# Kaleido Wallpaper

De caleidoscoop uit de Psychedelic Visualizer als live bureaubladachtergrond voor macOS. De wallpaper
reageert op het geluid van één app naar keuze (Apple Music, Spotify, een browser, …) of op al het
computergeluid.

## Vereisten

- macOS 13 Ventura of nieuwer
- Xcode Command Line Tools: `xcode-select --install`

## Bouwen en installeren

```bash
cd KaleidoWallpaper
chmod +x build.sh
./build.sh
cp -R "build/Kaleido Wallpaper.app" /Applications/
open "/Applications/Kaleido Wallpaper.app"
```

## Eerste keer starten

1. macOS vraagt toestemming voor **Schermopname**. Daarmee kan de app het geluid van andere apps
   opnemen; het beeld wordt niet gebruikt.
2. Zet **Kaleido Wallpaper** aan in Systeeminstellingen → Privacy en beveiliging → Schermopname.
3. Stop de app (✨-menu → Stop) en open hem opnieuw.

Na elke nieuwe build vraagt macOS opnieuw om toestemming: een zelfgebouwde app heeft na elke build
een andere handtekening. Haal dan eventueel de oude regel weg in de lijst Schermopname.

## Instellingen (✨ in de menubalk)

| Menu | Wat het doet |
|---|---|
| React to | Kies een draaiende app of **All computer audio**. Je keuze blijft bewaard; start de app later, dan pakt de wallpaper hem vanzelf op. |
| Album art mode (Apple Music, Spotify) | Werkt als je bij **React to** Muziek of Spotify kiest. De hoes staat in het midden en de caleidoscoop krijgt de dominante kleuren ervan. Bij de eerste keer vraagt macOS per app toestemming om Muziek of Spotify te bedienen. |
| Album colors | **Natural**, **Vibrant** of **Max**: hoe fel de kleuren uit de hoes worden. Hoger kiest eerder kleine felle accenten dan grote vale vlakken, verzadigt sterker en houdt de trails op de hoeskleuren. |
| Colors | **From album (default)**: de kleuren van de hoes, of de regenboog zonder hoes. **Custom**: je eigen vier kleuren, ook zonder albumhoes-modus. Kies een kant-en-klare reeks (Sunset, Ocean, Neon, Forest, Ice) of stel ze zelf in met **Edit custom colors…**; wijzigingen zie je meteen. |
| Response | Smooth (zoals de browser), Fast of Instant |
| Beat effect | **Off**, **Subtle bounce**, **Bounce** (zoom, flits, kleurranden bij een klap) of **Color switch**: bij een klap springt het palet direct naar de volgende kleur, zonder te schokken. In de albumhoes-modus is dat de volgende hoeskleur. |
| Sensitivity, Trails, Ring speed | Zelfde betekenis als in de visualizer |
| Reverse ring rotation | Keert alle draairichtingen om |
| Quality | Battery saver / Normal / Sharp: renderresolutie tegenover stroomverbruik |
| Frame rate | 30 of 60 fps |
| Pause | Toont je gewone achtergrond weer en stopt de opname |
| Launch at login | Werkt als de app in je map Apps staat |
| Open Screen Recording settings… | Opent de privacy-instelling die de app nodig heeft |

## Goed om te weten

- De wallpaper staat achter je bureaubladiconen en op elke Space en elk scherm. Hij vangt geen klikken op.
- Zolang de wallpaper helemaal bedekt is door vensters, wordt er niets berekend of getekend.
- Tijdens het opnemen toont macOS het paarse opname-icoon in de menubalk. Op macOS 15 en nieuwer vraagt
  macOS af en toe of de app mag blijven opnemen.
- Bij browsers komt het geluid soms uit hulpprocessen. Werkt een browser als bron niet, kies dan
  **Al het computergeluid**.

## Bestanden

- `Sources/App.swift`: menubalk, instellingen, vensters per scherm
- `Sources/AudioCapture.swift`: geluid per app via ScreenCaptureKit
- `Sources/ColorEditor.swift`: venster met vier kleurvakjes voor je eigen palet
- `Sources/NowPlayingArtwork.swift`: albumhoes uit Muziek of Spotify (AppleScript), met de iTunes-zoekdienst als terugval
- `Sources/SpectrumAnalyzer.swift`: FFT die exact dezelfde waarden geeft als de Web Audio AnalyserNode
- `Sources/WallpaperWindow.swift`: venster op bureaubladniveau met de WebGL-weergave
- `Resources/AppIcon.png` / `AppIcon.svg`: het app-icoon (de SVG is de bron; `build.sh` maakt er een `.icns` van)
- `Resources/kaleido.html`: de caleidoscoop (zelfde shader als de visualizer). Bekijk hem in een browser
  met `kaleido.html?demo`.
