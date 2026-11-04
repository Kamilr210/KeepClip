# Pomiar świeżych klatek Windows Graphics Capture

W katalogu głównym repozytorium wykonaj

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare-wgc-sandbox.ps1
```

Skrypt wymaga SDK .NET 10, projekcji Windows SDK 26100 przywracanej przez NuGet oraz lokalnych środowisk .NET 10 Core i Windows Desktop w tej samej wersji poprawkowej. Tworzy osobny katalog w `TestResults/wgc-sandbox`. Do wejścia kopiuje tylko sondę, dwa skrypty i potrzebne składniki środowiska uruchomieniowego. Nie uruchamia Sandboxa. Projekt sondy jest niezależny od rozwiązania KeepClip i zwykłych testów QA.

Otwórz wygenerowany plik `KeepClip-WGC-Probe.wsb`, aby uruchomić próbę w nowej sesji Windows Sandbox. Sonda wymaga konta `WDAGUtilityAccount`. Wyłączono sieć, mikrofon, kamerę, drukarki i schowek. Sandbox widzi katalog wejściowy tylko do odczytu i oddzielny katalog wyników do zapisu. Używa domyślnej konfiguracji vGPU. Odczekaj na koniec wszystkich sześciu faz. W czasie testu pozostaw widoczną planszę i nie otwieraj w Sandboxie innych aplikacji.

Sonda tworzy pełnoekranową planszę ze zmiennym 16-bitowym licznikiem. Każda faza trwa 10 sekund od pierwszej odebranej klatki. Trzy fazy przechwytują monitor, trzy okno planszy. W obu grupach sonda mierzy domyślny interwał WGC, a następnie żąda interwału 8 i 16 ms. Zapisuje wartość domyślną i odczytaną po zmianie. Każdy odczyt kopiuje całą powierzchnię obrazu do pamięci CPU. Próba nie obejmuje kodowania, dźwięku ani zapisywania klipów w KeepClip.

`output/summary.json` zawiera wiersz dla każdej ukończonej fazy. Pole `status` mówi, czy udało się wykonać pomiar. Osobne pole `freshFrameTargetMet` mówi, czy wynik osiągnął cel dla 60 FPS. `freshFpsOverDuration` to liczba różnych identyfikatorów odczytanych z pikseli podzielona przez pełne 10 sekund. Próg przyjmuje sekundę marginesu, jak w teście GDI, co wymaga co najmniej 540 różnych obrazów. Plansza musi sama rysować co najmniej 60 obrazów/s. Markery muszą być poprawne i nie mogą się cofać, czasy kompozytora mają rosnąć, a odstęp między pierwszą i ostatnią próbką ma obejmować co najmniej 9 sekund bez jednosekundowych przerw. `freshFrameTargetMet` jest puste, gdy pomiar się nie powiódł. Szczegółowe odczyty klatek i zużycie zasobów są w `frames.json` i `results.json` danej fazy.

Aby ponownie obliczyć wynik zakończonej fazy bez nagrywania, podaj jej katalog do `scripts/probes/wgc/measure-result.ps1 -Directory <katalog>`. Zakończony pomiar może wykazać rzeczywisty FPS istotnie niższy od 60.
