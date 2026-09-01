# SharpNotebook — wymagania

Klon Jupyter Notebook dla C#.

## Kluczowe decyzje architektoniczne

- **Platforma**: web app self-hosted (klient-serwer, jak Jupyter), lokalny serwer + przeglądarka jako UI
- **Frontend**: Blazor Server
- **Silnik wykonania**: własny kernel na `Microsoft.CodeAnalysis.CSharp.Scripting` (Roslyn Scripting API), osobny proces per notebook. Brak .NET Interactive (ryzyko maintenance, nie rozwijane przez Microsoft).
- **Język**: tylko C# (bez polyglot — F#/PowerShell poza scope)
- **Format notebooka**: własny (nie `.ipynb`), pliki lokalne na dysku
- **Użytkownicy**: solo/lokalne narzędzie, jeden user, jedna maszyna. Brak auth w MVP, bind na localhost.
- **Target SDK**: latest .NET SDK (obecnie .NET 10)

## Funkcjonalne

### Notebook management
- create/open/save/rename/delete notebooka jako plik lokalny
- file browser do nawigacji po systemie plików
- wiele notebooków otwartych naraz (taby), każdy z własnym procesem kernela
- autosave + wskaźnik unsaved changes
- schema versioning formatu pliku

### Komórki
- typy: code (C#), markdown
- insert/delete/move/reorder, cut/copy/paste, undo/redo
- edytor: Monaco + IntelliSense oparty na Roslyn (Workspace + CompletionService) — ten sam stack co execution
- markdown preview

### Wykonanie
- kernel jako osobny proces, stan między komórkami przez łańcuch `ScriptState<T>.ContinueWithAsync`
- komunikacja web-host ↔ kernel-proces: protokół po stdin/stdout lub named pipes (JSON per linia)
- run cell / run all / run above / run below, numeracja wykonań
- interrupt / restart kernel / restart & run all
- `#r "nuget:Package,Version"`: własna obsługa przez `NuGet.Protocol`/`NuGet.Resolver` + `AssemblyLoadContext` do ładowania assembly do kernela

### Output
- stdout/stderr capture (redirect Console.Out/Error w kontekście kernela)
- exceptions z wykonania → do output, bez crashu procesu kernela
- rich output: tekst, tabele/DataFrame (formatted display kolekcji/obiektów), wykresy (ScottPlot/Plotly.NET), HTML, obrazy
- własny display protocol: rozpoznawanie typu zwracanego (string→HTML jeśli oznaczony, Image/byte[]→obraz, IEnumerable→tabela) + opcjonalny `Display()` helper

## Niefunkcjonalne

- **Architektura**: ASP.NET Core (Blazor Server) backend + API/WebSocket do streamingu output i komunikacji z kernelem; kernel = osobny proces — izolacja crashy, restart bez zabijania serwera web
- **Platforma**: cross-platform (.NET) — Windows/Linux/macOS
- **Sieć/auth**: domyślnie bind localhost, brak auth w MVP
- **Wydajność**: output streamowany async (WebSocket), IntelliSense nie blokuje UI
- **Bezpieczeństwo**: notebook = arbitrary code execution z definicji, brak sandboxa w MVP; warto oznaczać notebooki spoza własnych jako "untrusted" przed uruchomieniem
- **Zależności**: wymaga zainstalowanego .NET SDK na hoście (do kompilacji/wykonania w kernelu)
- **Extensibility hook**: kernel-communication protokół i output rendering budowane jako rozszerzalne (pluggable), żeby dało się później dodać debug/variable inspector bez przepisywania core

## Backlog (poza MVP)

- Debug / variable inspector (watch, breakpointy)
- Export (PDF/HTML/`.ipynb` konwersja)
- Polyglot (odrzucone świadomie)
- Auth / dostęp zdalny poza localhost
