using System.Collections.ObjectModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using SharpNotebook.Core;
using SharpNotebook.Kernel.Contracts;
using SharpNotebook.Services;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace SharpNotebook.Desktop;

public partial class MainWindow : Window
{
    private static readonly RegistryOptions RegistryOptions = new(ThemeName.DarkPlus);
    private static readonly FontFamily MonoFont = new("avares://SharpNotebook.Desktop/Assets/Fonts#JetBrains Mono,Consolas,monospace");
    private static IRawTheme? _mochaTheme;
    private static IRawTheme? _latteTheme;

    private static readonly (string Key, string Mocha, string Latte)[] PaletteMap =
    [
        ("CtpBaseBrush", "#1e1e2e", "#eff1f5"),
        ("CtpMantleBrush", "#181825", "#e6e9ef"),
        ("CtpCrustBrush", "#11111b", "#dce0e8"),
        ("CtpSurface0Brush", "#313244", "#ccd0da"),
        ("CtpSurface1Brush", "#45475a", "#bcc0cc"),
        ("CtpSurface2Brush", "#585b70", "#acb0be"),
        ("CtpTextBrush", "#cdd6f4", "#4c4f69"),
        ("CtpSubtext1Brush", "#bac2de", "#5c5f77"),
        ("CtpOverlay0Brush", "#6c7086", "#9ca0b0"),
        ("CtpMauveBrush", "#cba6f7", "#8839ef"),
        ("CtpMauveDimBrush", "#cba6f7", "#8839ef"),
        ("CtpRedBrush", "#f38ba8", "#d20f39"),
        ("CtpGreenBrush", "#a6e3a1", "#40a02b"),
        ("CtpPeachBrush", "#fab387", "#fe640b"),
    ];

    private readonly string _rootDir;
    private readonly string _favoritesPath;
    private readonly IAiCodeGenerator _aiGenerator;
    private readonly List<NotebookTab> _tabs = new();
    private readonly ObservableCollection<CellTemplate> _favorites = new();

    private const double SidebarWidth = 240;

    private string _currentDir;
    private bool _isDark = true;
    private SidePanelMode _sidePanelMode = SidePanelMode.None;
    private bool _leftCollapsed;
    private bool _rightCollapsed;
    private readonly List<PaletteCommand> _allCommands = new();

    // Shared, mutable brush instances (see App.axaml) — cached once here so code-built controls can use
    // them directly; the light/dark toggle mutates these brushes' .Color in place, so every control that
    // picked one up repaints live with no rebuild needed.
    private IBrush _base = null!, _crust = null!, _surface0 = null!, _text = null!, _overlay0 = null!,
        _mauve = null!, _mauveDim = null!, _red = null!, _green = null!, _peach = null!;

    public MainWindow(IAiCodeGenerator aiGenerator)
    {
        InitializeComponent();
        _aiGenerator = aiGenerator;
        _rootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SharpNotebook");
        _currentDir = _rootDir;

        CacheBrushes();
        RefreshFilesList();

        _favoritesPath = FavoritesStore.DefaultPath;
        foreach (var favorite in FavoritesStore.Load(_favoritesPath))
            _favorites.Add(favorite);
        FavoritesList.ItemsSource = _favorites;
        FavoritesList.ItemTemplate = new FuncDataTemplate<CellTemplate>((template, _) => BuildFavoriteRow(template));

        InitializeCommandPalette();

        Closing += (_, _) =>
        {
            foreach (var tab in _tabs)
                _ = tab.Session.DisposeAsync();
        };
    }

    private void CacheBrushes()
    {
        var res = Application.Current!.Resources;
        _base = (IBrush)res["CtpBaseBrush"]!;
        _crust = (IBrush)res["CtpCrustBrush"]!;
        _surface0 = (IBrush)res["CtpSurface0Brush"]!;
        _text = (IBrush)res["CtpTextBrush"]!;
        _overlay0 = (IBrush)res["CtpOverlay0Brush"]!;
        _mauve = (IBrush)res["CtpMauveBrush"]!;
        _mauveDim = (IBrush)res["CtpMauveDimBrush"]!;
        _red = (IBrush)res["CtpRedBrush"]!;
        _green = (IBrush)res["CtpGreenBrush"]!;
        _peach = (IBrush)res["CtpPeachBrush"]!;
    }

    // ---------- command palette ----------

    private sealed record PaletteCommand(string Name, Action Execute)
    {
        public override string ToString() => Name;
    }

    // Reuses the same Click handlers the toolbar buttons call (invoked with a dummy RoutedEventArgs) for
    // global actions; per-tab actions go through the named TrustTabAsync/AddCellToTab/SaveTabAsync/
    // RestartRunAllAsync methods those buttons also call, guarded by "if there's a current tab".
    private void InitializeCommandPalette()
    {
        _allCommands.AddRange(new PaletteCommand[]
        {
            new("Nowy notebook...", () => NewNotebookNameBox.Focus()),
            new("Dodaj komórkę", () => { if (CurrentTab is { } t) AddCellToTab(t); }),
            new("Zapisz notebook (Ctrl+S)", () => { if (CurrentTab is { } t) _ = SaveTabAsync(t); }),
            new("Restart i uruchom wszystko (Ctrl+Shift+Enter)", () => { if (CurrentTab is { } t) _ = RestartRunAllAsync(t); }),
            new("Ufaj temu notebookowi", () => { if (CurrentTab is { } t) _ = TrustTabAsync(t); }),
            new("Zamknij zakładkę", () => { if (CurrentTab is { } t) CloseTab(t); }),
            new("Eksportuj kod (.cs)", () => ExportCs_Click(null, new RoutedEventArgs())),
            new("Eksportuj wynik (.html)", () => ExportHtml_Click(null, new RoutedEventArgs())),
            new("Eksportuj do .ipynb (Jupyter)", () => ExportIpynb_Click(null, new RoutedEventArgs())),
            new("Pokaż/schowaj Eksplorator (Ctrl+B)", () => ToggleLeftSidebar_Click(null, new RoutedEventArgs())),
            new("Pokaż/schowaj Ulubione (Ctrl+Alt+B)", () => ToggleRightSidebar_Click(null, new RoutedEventArgs())),
            new("Zwiń/rozwiń wszystkie komórki (Ctrl+Alt+C)", () => CollapseAllToggle_Click(null, new RoutedEventArgs())),
            new("Szukaj w notebooku (Ctrl+F)", OpenSearch),
            new("Przełącz motyw Dark/Light", () => ThemeToggle_Click(null, new RoutedEventArgs())),
            new("Pokaż/schowaj panel Zmienne", () => _ = ToggleSidePanelAsync(SidePanelMode.Variables)),
            new("Pokaż/schowaj Spis treści", () => _ = ToggleSidePanelAsync(SidePanelMode.Outline)),
            new("Pokaż/schowaj panel Paczki", () => _ = ToggleSidePanelAsync(SidePanelMode.Packages)),
        });
    }

    // Window-level shortcuts — bubble up from wherever focus is (including a cell's editor), so they work
    // no matter what's focused. Everything here is also reachable via the command palette (Ctrl+Shift+P);
    // these are just the handful worth a direct key for.
    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (ctrl && shift && !alt && e.Key == Key.P)
        {
            e.Handled = true;
            OpenCommandPalette();
        }
        else if (e.Key == Key.Escape && CommandPaletteOverlay.IsVisible)
        {
            e.Handled = true;
            CloseCommandPalette();
        }
        else if (ctrl && !shift && !alt && e.Key == Key.B)
        {
            e.Handled = true;
            ToggleLeftSidebar_Click(null, new RoutedEventArgs());
        }
        else if (ctrl && alt && !shift && e.Key == Key.B)
        {
            e.Handled = true;
            ToggleRightSidebar_Click(null, new RoutedEventArgs());
        }
        else if (ctrl && alt && !shift && e.Key == Key.C)
        {
            e.Handled = true;
            CollapseAllToggle_Click(null, new RoutedEventArgs());
        }
        else if (ctrl && !shift && !alt && e.Key == Key.S)
        {
            e.Handled = true;
            if (CurrentTab is { } tabToSave)
                _ = SaveTabAsync(tabToSave);
        }
        else if (ctrl && shift && !alt && e.Key == Key.Enter)
        {
            e.Handled = true;
            if (CurrentTab is { } tabToRestart)
                _ = RestartRunAllAsync(tabToRestart);
        }
        else if (ctrl && !shift && !alt && e.Key == Key.F)
        {
            e.Handled = true;
            OpenSearch();
        }
        else if (e.Key == Key.Escape && SearchOverlay.IsVisible)
        {
            e.Handled = true;
            CloseSearch();
        }
    }

    // ---------- search across the whole notebook ----------

    private readonly List<(int SlotIndex, int Offset, int Length)> _searchMatches = new();
    private int _searchMatchIndex = -1;

    private void OpenSearch()
    {
        SearchOverlay.IsVisible = true;
        SearchInput.Text = "";
        _searchMatches.Clear();
        _searchMatchIndex = -1;
        UpdateSearchCount();
        SearchInput.Focus();
    }

    private void CloseSearch() => SearchOverlay.IsVisible = false;
    private void CloseSearch_Click(object? sender, RoutedEventArgs e) => CloseSearch();

    // Searches live editor text (not just the last-saved Cell.Source) across every cell in the current
    // tab, in cell order — so it finds what's on screen, including unsaved edits.
    private void RunSearch(string query)
    {
        _searchMatches.Clear();
        _searchMatchIndex = -1;

        var tab = CurrentTab;
        if (tab is not null && !string.IsNullOrEmpty(query))
        {
            for (var i = 0; i < tab.Slots.Count; i++)
            {
                var text = tab.Slots[i].Editor.Text;
                var start = 0;
                while (true)
                {
                    var idx = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0)
                        break;
                    _searchMatches.Add((i, idx, query.Length));
                    start = idx + query.Length;
                }
            }
        }

        UpdateSearchCount();
        if (_searchMatches.Count > 0)
            GoToMatch(0);
    }

    private void UpdateSearchCount() =>
        SearchCount.Text = _searchMatches.Count == 0 ? "0/0" : $"{_searchMatchIndex + 1}/{_searchMatches.Count}";

    private void GoToMatch(int index)
    {
        if (_searchMatches.Count == 0)
            return;

        _searchMatchIndex = ((index % _searchMatches.Count) + _searchMatches.Count) % _searchMatches.Count;
        UpdateSearchCount();

        var tab = CurrentTab;
        if (tab is null || _searchMatchIndex >= tab.Slots.Count)
            return;

        var (slotIndex, offset, length) = _searchMatches[_searchMatchIndex];
        var slot = tab.Slots[slotIndex];
        if (slot.SourceCollapsed)
            SetSourceCollapsed(slot, false);

        slot.Editor.Select(offset, length);
        slot.Editor.ScrollToLine(slot.Editor.Document.GetLineByOffset(offset).LineNumber);
        slot.Root.BringIntoView();
    }

    private void SearchInput_TextChanged(object? sender, TextChangedEventArgs e) => RunSearch(SearchInput.Text ?? "");
    private void SearchNext_Click(object? sender, RoutedEventArgs e) => GoToMatch(_searchMatchIndex + 1);
    private void SearchPrev_Click(object? sender, RoutedEventArgs e) => GoToMatch(_searchMatchIndex - 1);

    private void SearchInput_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                CloseSearch();
                break;
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                e.Handled = true;
                GoToMatch(_searchMatchIndex - 1);
                break;
            case Key.Enter:
                e.Handled = true;
                GoToMatch(_searchMatchIndex + 1);
                break;
        }
    }

    private void OpenCommandPalette()
    {
        CommandPaletteOverlay.IsVisible = true;
        CommandPaletteInput.Text = "";
        FilterCommands("");
        CommandPaletteInput.Focus();
    }

    private void CloseCommandPalette() => CommandPaletteOverlay.IsVisible = false;

    private void FilterCommands(string query)
    {
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allCommands
            : _allCommands.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        CommandPaletteList.ItemsSource = filtered;
        if (filtered.Count > 0)
            CommandPaletteList.SelectedIndex = 0;
    }

    private void CommandPaletteInput_TextChanged(object? sender, TextChangedEventArgs e) =>
        FilterCommands(CommandPaletteInput.Text ?? "");

    private void CommandPaletteInput_KeyDown(object? sender, KeyEventArgs e)
    {
        var items = CommandPaletteList.ItemsSource as IList<PaletteCommand>;
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                CloseCommandPalette();
                break;
            case Key.Down when items is { Count: > 0 }:
                e.Handled = true;
                CommandPaletteList.SelectedIndex = Math.Min(CommandPaletteList.SelectedIndex + 1, items.Count - 1);
                break;
            case Key.Up when items is { Count: > 0 }:
                e.Handled = true;
                CommandPaletteList.SelectedIndex = Math.Max(CommandPaletteList.SelectedIndex - 1, 0);
                break;
            case Key.Enter:
                e.Handled = true;
                ExecuteSelectedCommand();
                break;
        }
    }

    private void CommandPaletteList_DoubleTapped(object? sender, TappedEventArgs e) => ExecuteSelectedCommand();

    private void ExecuteSelectedCommand()
    {
        if (CommandPaletteList.SelectedItem is not PaletteCommand cmd)
            return;

        CloseCommandPalette();
        cmd.Execute();
    }

    // ---------- file browser (nested folders) ----------

    private sealed record FileEntry(string Label, string FullPath, bool IsDirectory)
    {
        public override string ToString() => Label;
    }

    private void RefreshFilesList()
    {
        Directory.CreateDirectory(_rootDir);
        Directory.CreateDirectory(_currentDir);

        var entries = new List<FileEntry>();
        if (Path.GetFullPath(_currentDir) != Path.GetFullPath(_rootDir))
            entries.Add(new FileEntry("⬆ ..", Path.GetDirectoryName(_currentDir.TrimEnd(Path.DirectorySeparatorChar))!, IsDirectory: true));

        foreach (var dir in Directory.GetDirectories(_currentDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            entries.Add(new FileEntry("📁 " + Path.GetFileName(dir), dir, IsDirectory: true));

        foreach (var file in Directory.GetFiles(_currentDir, "*" + NotebookFile.Extension).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            entries.Add(new FileEntry("📄 " + Path.GetFileNameWithoutExtension(file), file, IsDirectory: false));

        FilesList.ItemsSource = entries;
    }

    private async void FilesList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FilesList.SelectedItem is not FileEntry entry)
            return;

        if (entry.IsDirectory)
        {
            _currentDir = entry.FullPath;
            RefreshFilesList();
            return;
        }

        await OpenNotebookAsync(entry.FullPath);
    }

    private async void NewNotebook_Click(object? sender, RoutedEventArgs e)
    {
        var name = NewNotebookNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        Directory.CreateDirectory(_currentDir);
        var path = Path.Combine(_currentDir, name + NotebookFile.Extension);
        if (!File.Exists(path))
            NotebookFile.Save(path, NotebookFile.CreateEmpty());

        NewNotebookNameBox.Text = "";
        RefreshFilesList();
        await OpenNotebookAsync(path);
    }

    // ---------- sidebar collapse ----------

    // Setting the Grid column's own width to 0 (not just hiding its content) is what lets the middle
    // (Tabs) column, being "*", actually reclaim the freed space — hiding only the Border would leave an
    // empty 240px gap instead of the middle column expanding into it.
    private void ToggleLeftSidebar_Click(object? sender, RoutedEventArgs e)
    {
        _leftCollapsed = !_leftCollapsed;
        RootGrid.ColumnDefinitions[0].Width = new GridLength(_leftCollapsed ? 0 : SidebarWidth);
        LeftSidebar.IsVisible = !_leftCollapsed;
    }

    private void ToggleRightSidebar_Click(object? sender, RoutedEventArgs e)
    {
        _rightCollapsed = !_rightCollapsed;
        RootGrid.ColumnDefinitions[2].Width = new GridLength(_rightCollapsed ? 0 : SidebarWidth);
        RightSidebar.IsVisible = !_rightCollapsed;
    }

    // ---------- favorites (cell templates) ----------

    // ItemsControl container recycling clears a container's Content to null before reusing/discarding it
    // (ClearContainerForItemOverride) — that null re-enters this same template function, not just fresh
    // items, so it must tolerate a null template rather than assume every call is a real row.
    private Control BuildFavoriteRow(CellTemplate? template)
    {
        if (template is null)
            return new Border();

        var nameText = new TextBlock
        {
            Text = template.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var typeTag = new TextBlock
        {
            Text = template.Type switch { CellType.Code => "K", CellType.Markdown => "M", _ => "AI" },
            Foreground = _overlay0,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var insertButton = new Button { Content = "+", Padding = new Thickness(6, 0), FontSize = 12 };
        var deleteButton = new Button { Content = "✕", Padding = new Thickness(6, 0), FontSize = 12 };
        ToolTip.SetTip(insertButton, "Dodaj jako nową komórkę");
        ToolTip.SetTip(deleteButton, "Usuń z ulubionych");

        insertButton.Click += (_, _) => InsertFavoriteIntoCurrentTab(template);
        // Deferred rather than synchronous: removing the bound item straight from its own container's
        // Click handler, while that Click is still routing through the very container about to be
        // recycled, is a known Avalonia/WPF ItemsControl footgun. Posting lets the click finish
        // dispatching first, so the ListBox's container teardown happens on a clean turn afterward.
        deleteButton.Click += (_, _) => Dispatcher.UIThread.Post(() => RemoveFavorite(template));

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        Grid.SetColumn(nameText, 0);
        Grid.SetColumn(typeTag, 1);
        Grid.SetColumn(insertButton, 2);
        Grid.SetColumn(deleteButton, 3);
        typeTag.Margin = new Thickness(4, 0, 6, 0);
        row.Children.Add(nameText);
        row.Children.Add(typeTag);
        row.Children.Add(insertButton);
        row.Children.Add(deleteButton);
        return row;
    }

    private void InsertFavoriteIntoCurrentTab(CellTemplate template)
    {
        var tab = CurrentTab;
        if (tab is null)
            return;

        SyncEditorsToCells(tab);
        tab.Notebook.Cells.Add(new Cell { Type = template.Type, Source = template.Source });
        RenderCells(tab);
        MarkDirty(tab);
    }

    private void RemoveFavorite(CellTemplate template)
    {
        _favorites.Remove(template);
        FavoritesStore.Save(_favoritesPath, _favorites);
    }

    private void SaveAsFavorite(Cell cell, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return;

        _favorites.Add(new CellTemplate(DeriveTemplateName(source), cell.Type, source));
        FavoritesStore.Save(_favoritesPath, _favorites);
    }

    private static void SetSourceCollapsed(CellSlot slot, bool collapsed)
    {
        slot.SourceCollapsed = collapsed;
        slot.Editor.IsVisible = !collapsed;
        slot.SourcePreview.IsVisible = collapsed;
        slot.SourcePreview.Text = collapsed ? SummarizeSource(slot.Editor.Text) : "";
        slot.SourceCollapseButton.Content = collapsed ? "▸" : "▾";
    }

    private static void SetOutputCollapsed(CellSlot slot, bool collapsed)
    {
        slot.OutputPanel.IsVisible = !collapsed;
        slot.OutputCollapseButton.Content = collapsed ? "▸" : "▾";
    }

    // Toggles based on the FIRST cell's current source-collapse state — if anything is expanded, "collapse
    // all" wins; only when everything is already collapsed does the button switch to "expand all". Matches
    // how most editors' fold-all buttons behave (fold wins over an inconsistent mixed state).
    private void CollapseAllToggle_Click(object? sender, RoutedEventArgs e)
    {
        var tab = CurrentTab;
        if (tab is null || tab.Slots.Count == 0)
            return;

        var collapseAll = tab.Slots.Any(s => !s.SourceCollapsed || (s.Cell.Outputs.Count > 0 && s.OutputPanel.IsVisible));
        foreach (var slot in tab.Slots)
        {
            SetSourceCollapsed(slot, collapseAll);
            if (slot.Cell.Outputs.Count > 0)
                SetOutputCollapsed(slot, collapseAll);
        }
    }

    private static void SetTag(Cell cell, string tag, bool present)
    {
        if (present && !cell.Tags.Contains(tag))
            cell.Tags.Add(tag);
        else if (!present)
            cell.Tags.Remove(tag);
    }

    private static string SummarizeSource(string source)
    {
        var lines = source.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstLine = lines.FirstOrDefault() ?? "";
        var extra = lines.Length - 1;
        return extra > 0 ? $"{firstLine}  (+{extra} linii)" : firstLine;
    }

    private static string DeriveTemplateName(string source)
    {
        var firstLine = source
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        if (firstLine.Length > 40)
            firstLine = firstLine[..40] + "…";
        return string.IsNullOrWhiteSpace(firstLine) ? "Bez nazwy" : firstLine;
    }

    // ---------- tabs ----------

    private enum SidePanelMode { None, Variables, Packages, Outline }

    private NotebookTab? CurrentTab => (Tabs.SelectedItem as TabItem)?.Tag as NotebookTab;

    private async Task OpenNotebookAsync(string path)
    {
        var existing = _tabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.Ordinal));
        if (existing is not null)
        {
            Tabs.SelectedItem = existing.TabItem;
            return;
        }

        var notebook = NotebookFile.Load(path);
        var session = new NotebookSession();
        await session.StartAsync();

        var cellsPanel = new StackPanel { Spacing = 1 };
        var scroll = new ScrollViewer { Content = cellsPanel, Background = Brushes.Transparent };

        var tab = new NotebookTab
        {
            Path = path,
            Notebook = notebook,
            Session = session,
            CellsPanel = cellsPanel,
            Scroll = scroll,
        };

        var nameText = new TextBlock { Text = Path.GetFileNameWithoutExtension(path), VerticalAlignment = VerticalAlignment.Center };
        var dirtyDot = new TextBlock { Text = " ●", Foreground = _peach, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
        var closeButton = new Button { Content = "✕", Padding = new Thickness(4, 0), FontSize = 11 };
        ToolTip.SetTip(closeButton, "Zamknij zakładkę");
        closeButton.Click += (_, _) => CloseTab(tab);

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        header.Children.Add(nameText);
        header.Children.Add(dirtyDot);
        header.Children.Add(closeButton);
        tab.DirtyDot = dirtyDot;

        var trustButton = new Button { Content = "🔓", IsVisible = !notebook.Trusted, Foreground = _red, BorderBrush = _red };
        ToolTip.SetTip(trustButton, "Ufaj temu notebookowi");
        var addCellButton = new Button { Content = "＋" };
        ToolTip.SetTip(addCellButton, "Dodaj komórkę");
        var saveButton = new Button { Content = "💾" };
        ToolTip.SetTip(saveButton, "Zapisz (Ctrl+S)");
        var restartButton = new Button { Content = "⟳" };
        ToolTip.SetTip(restartButton, "Restart i uruchom wszystko (Ctrl+Shift+Enter)");

        tab.TrustButton = trustButton;
        trustButton.Click += async (_, _) => await TrustTabAsync(tab);
        addCellButton.Click += (_, _) => AddCellToTab(tab);
        saveButton.Click += async (_, _) => await SaveTabAsync(tab);
        restartButton.Click += async (_, _) => await RestartRunAllAsync(tab);

        var actionBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(12, 8) };
        actionBar.Children.Add(trustButton);
        actionBar.Children.Add(addCellButton);
        actionBar.Children.Add(saveButton);
        actionBar.Children.Add(restartButton);
        var actionBarBorder = new Border
        {
            Child = actionBar,
            Background = (IBrush)Application.Current!.Resources["CtpMantleBrush"]!,
            BorderBrush = _surface0,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

        var content = new DockPanel();
        DockPanel.SetDock(actionBarBorder, Dock.Top);
        content.Children.Add(actionBarBorder);
        content.Children.Add(scroll);

        var tabItem = new TabItem { Header = header, Content = content, Tag = tab };
        tab.TabItem = tabItem;

        _tabs.Add(tab);
        Tabs.Items.Add(tabItem);
        Tabs.SelectedItem = tabItem;

        RenderCells(tab);
    }

    private void CloseTab(NotebookTab tab)
    {
        _ = tab.Session.DisposeAsync();
        Tabs.Items.Remove(tab.TabItem);
        _tabs.Remove(tab);
    }

    private async void Tabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SidePanel.IsVisible)
            await RefreshSidePanelAsync();
    }

    // ---------- cell model sync / rendering ----------

    private static void MarkDirty(NotebookTab tab)
    {
        tab.Dirty = true;
        tab.DirtyDot.IsVisible = true;
    }

    private Task SaveCurrentNotebookAsync(NotebookTab tab)
    {
        NotebookFile.Save(tab.Path, tab.Notebook);
        tab.Dirty = false;
        tab.DirtyDot.IsVisible = false;
        return Task.CompletedTask;
    }

    // TextEditor content isn't kept synced to Cell.Source on every keystroke; pull it back right before
    // any save/run/rebuild, since nothing reads Cell.Source in between.
    private static void SyncEditorsToCells(NotebookTab tab)
    {
        foreach (var slot in tab.Slots)
            slot.Cell.Source = slot.Editor.Text;
    }

    private void RenderCells(NotebookTab tab)
    {
        tab.CellsPanel.Children.Clear();
        tab.Slots.Clear();

        foreach (var cell in tab.Notebook.Cells)
        {
            var slot = BuildCellSlot(tab, cell);
            tab.Slots.Add(slot);
            tab.CellsPanel.Children.Add(slot.Root);
        }
    }

    private void MoveCell(NotebookTab tab, Cell cell, int delta)
    {
        SyncEditorsToCells(tab);
        var index = tab.Notebook.Cells.IndexOf(cell);
        var newIndex = index + delta;
        if (newIndex < 0 || newIndex >= tab.Notebook.Cells.Count)
            return;

        (tab.Notebook.Cells[index], tab.Notebook.Cells[newIndex]) = (tab.Notebook.Cells[newIndex], tab.Notebook.Cells[index]);
        RenderCells(tab);
        MarkDirty(tab);
    }

    private void DeleteCell(NotebookTab tab, Cell cell)
    {
        SyncEditorsToCells(tab);
        tab.Notebook.Cells.Remove(cell);
        RenderCells(tab);
        MarkDirty(tab);
    }

    private void FocusNextOrCreateCell(NotebookTab tab, Cell currentCell)
    {
        var index = tab.Notebook.Cells.IndexOf(currentCell);
        if (index < 0)
            return;

        if (index + 1 < tab.Slots.Count)
        {
            tab.Slots[index + 1].Editor.Focus();
            return;
        }

        SyncEditorsToCells(tab);
        tab.Notebook.Cells.Add(new Cell());
        RenderCells(tab);
        MarkDirty(tab);
        tab.Slots[^1].Editor.Focus();
    }

    // ---------- one cell's controls ----------

    private CellSlot BuildCellSlot(NotebookTab tab, Cell cell)
    {
        var grip = new TextBlock
        {
            Text = "⠿",
            Foreground = _overlay0,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            Cursor = new Cursor(StandardCursorType.SizeAll),
        };
        ToolTip.SetTip(grip, "Przeciągnij, aby zmienić kolejność");

        var typeBox = new ComboBox
        {
            ItemsSource = new[] { CellType.Code, CellType.Markdown, CellType.Ai },
            SelectedItem = cell.Type,
            Width = 110,
        };

        var collapseButton = new Button { Content = "▾" };
        ToolTip.SetTip(collapseButton, "Zwiń/rozwiń komórkę");
        var upButton = new Button { Content = "↑" };
        ToolTip.SetTip(upButton, "Przesuń w górę");
        var downButton = new Button { Content = "↓" };
        ToolTip.SetTip(downButton, "Przesuń w dół");
        var deleteButton = new Button { Content = "🗑" };
        ToolTip.SetTip(deleteButton, "Usuń komórkę");
        var favoriteButton = new Button { Content = "☆" };
        ToolTip.SetTip(favoriteButton, "Zapisz jako ulubione");
        var hideInputToggle = new ToggleButton { Content = "👁", IsChecked = cell.Tags.Contains("hide-input") };
        ToolTip.SetTip(hideInputToggle, "Ukryj kod przy otwarciu notebooka (tag: hide-input)");
        var skipRunAllToggle = new ToggleButton { Content = "⏭", IsChecked = cell.Tags.Contains("skip-on-run-all") };
        ToolTip.SetTip(skipRunAllToggle, "Pomiń przy Restart+Uruchom wszystko / Powyżej / Poniżej (tag: skip-on-run-all)");
        var runAboveButton = new Button { Content = "⏫", IsEnabled = tab.Notebook.Trusted };
        ToolTip.SetTip(runAboveButton, "Uruchom komórki powyżej");
        var runBelowButton = new Button { Content = "⏬", IsEnabled = tab.Notebook.Trusted };
        ToolTip.SetTip(runBelowButton, "Uruchom tę i komórki poniżej");
        var actionButton = new Button
        {
            Content = cell.Type == CellType.Ai ? "✨" : "▶",
            IsVisible = cell.Type != CellType.Markdown,
            IsEnabled = tab.Notebook.Trusted,
        };
        ToolTip.SetTip(actionButton, cell.Type == CellType.Ai ? "Generuj kod (Ctrl+Enter)" : "Uruchom (Ctrl+Enter)");
        var execBadge = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.Bold,
            Foreground = _peach,
            Text = cell.ExecutionCount is { } n ? $"[{n}]" : "",
        };
        var statusText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };

        var editor = new TextEditor
        {
            FontFamily = MonoFont,
            FontSize = 14,
            ShowLineNumbers = true,
            Height = 160,
            Text = cell.Source,
            Background = _base,
            Foreground = _text,
            LineNumbersForeground = _overlay0,
        };
        editor.TextArea.SelectionBrush = _mauveDim;
        editor.TextArea.Caret.CaretBrush = _mauve;

        var sourcePreview = new TextBlock
        {
            Foreground = _overlay0,
            FontFamily = MonoFont,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsVisible = false,
        };

        var errorText = new TextBlock { Foreground = _red, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };
        var diagnosticsText = new TextBlock { Foreground = _red, TextWrapping = TextWrapping.Wrap, IsVisible = false, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };

        var outputHeader = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 6, 0, 0),
            IsVisible = cell.Outputs.Count > 0,
        };
        var outputCollapseButton = new Button { Content = "▾", Padding = new Thickness(6, 2), FontSize = 10 };
        ToolTip.SetTip(outputCollapseButton, "Zwiń/rozwiń wynik");
        var outputHeaderLabel = new TextBlock { Text = $"Wynik ({cell.Outputs.Count})", Foreground = _overlay0, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        outputHeader.Children.Add(outputCollapseButton);
        outputHeader.Children.Add(outputHeaderLabel);

        var outputPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var output in cell.Outputs)
            outputPanel.Children.Add(RenderOutput(output));

        var slot = new CellSlot(tab, cell, editor, execBadge, statusText, outputPanel, actionButton)
        {
            OutputHeader = outputHeader,
            OutputHeaderLabel = outputHeaderLabel,
            OutputCollapseButton = outputCollapseButton,
            SourcePreview = sourcePreview,
            SourceCollapseButton = collapseButton,
        };
        InstallHighlighting(slot, cell.Type);

        collapseButton.Click += (_, _) => SetSourceCollapsed(slot, !slot.SourceCollapsed);
        outputCollapseButton.Click += (_, _) => SetOutputCollapsed(slot, slot.OutputPanel.IsVisible);

        // "hide-input" auto-collapses on render — reuses the same collapse machinery as the manual toggle,
        // so it composes correctly with the "collapse all" button and stays consistent after re-render.
        if (cell.Tags.Contains("hide-input"))
            SetSourceCollapsed(slot, true);

        hideInputToggle.IsCheckedChanged += (_, _) =>
        {
            SetTag(cell, "hide-input", hideInputToggle.IsChecked == true);
            MarkDirty(tab);
        };
        skipRunAllToggle.IsCheckedChanged += (_, _) =>
        {
            SetTag(cell, "skip-on-run-all", skipRunAllToggle.IsChecked == true);
            MarkDirty(tab);
        };

        typeBox.SelectionChanged += (_, _) =>
        {
            SyncEditorsToCells(tab);
            cell.Type = (CellType)typeBox.SelectedItem!;
            RenderCells(tab);
            MarkDirty(tab);
        };
        upButton.Click += (_, _) => MoveCell(tab, cell, -1);
        downButton.Click += (_, _) => MoveCell(tab, cell, 1);
        deleteButton.Click += (_, _) => DeleteCell(tab, cell);
        favoriteButton.Click += (_, _) => SaveAsFavorite(cell, slot.Editor.Text);
        runAboveButton.Click += async (_, _) => await RunRangeAsync(tab, 0, tab.Notebook.Cells.IndexOf(cell) - 1);
        runBelowButton.Click += async (_, _) => await RunRangeAsync(tab, tab.Notebook.Cells.IndexOf(cell), tab.Notebook.Cells.Count - 1);
        actionButton.Click += async (_, _) =>
        {
            if (cell.Type == CellType.Ai)
                await GenerateAsync(slot, errorText);
            else
                await RunCellAsync(slot);
        };

        // Shift+Enter: run/generate, then move to (or create) the next cell — Ctrl+Enter: run in place.
        // No Escape/arrow-key "command mode" cell navigation like Jupyter's — this covers the shortcut
        // that actually saves keystrokes during normal editing; add that later if it's missed.
        editor.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || cell.Type == CellType.Markdown)
                return;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                if (cell.Type == CellType.Ai)
                    await GenerateAsync(slot, errorText);
                else
                    await RunCellAsync(slot);
                FocusNextOrCreateCell(tab, cell);
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                if (cell.Type == CellType.Ai)
                    await GenerateAsync(slot, errorText);
                else
                    await RunCellAsync(slot);
            }
        };

        editor.TextChanged += (_, _) => MarkDirty(tab);

        // Live diagnostics and hover docs only make sense for real C# — not for a Markdown/Ai cell's
        // plain-text/prompt content. Neither is gated on Trusted: compiling code for diagnostics/symbol
        // info never executes it, same reasoning as completion already not being gated (see CLAUDE.md).
        if (cell.Type == CellType.Code)
        {
            DispatcherTimer? diagnosticsTimer = null;
            editor.TextChanged += (_, _) =>
            {
                diagnosticsTimer?.Stop();
                diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                diagnosticsTimer.Tick += async (_, _) =>
                {
                    diagnosticsTimer!.Stop();
                    await RefreshDiagnosticsAsync(slot, diagnosticsText);
                };
                diagnosticsTimer.Start();
            };

            DispatcherTimer? hoverTimer = null;
            editor.PointerMoved += (_, e) =>
            {
                var point = e.GetPosition(editor);
                hoverTimer?.Stop();
                hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                hoverTimer.Tick += async (_, _) =>
                {
                    hoverTimer!.Stop();
                    await ShowHoverAsync(slot, editor, point);
                };
                hoverTimer.Start();
            };
            editor.PointerExited += (_, _) =>
            {
                hoverTimer?.Stop();
                ToolTip.SetIsOpen(editor, false);
            };
        }

        // Reorder by drag: press the grip, move over another cell, release to drop there. No live
        // reordering while dragging (only on release) — swapping mid-drag would rebuild the visual tree
        // and drop the grip's own pointer capture, breaking the gesture partway through.
        var dragging = false;
        grip.PointerPressed += (_, e) =>
        {
            dragging = true;
            e.Pointer.Capture(grip);
            e.Handled = true;
        };
        grip.PointerReleased += (_, e) =>
        {
            if (!dragging)
                return;
            dragging = false;
            e.Pointer.Capture(null);

            var pointerY = e.GetPosition(tab.CellsPanel).Y;
            var currentIndex = tab.Notebook.Cells.IndexOf(cell);
            var targetIndex = currentIndex;
            foreach (var (s, i) in tab.Slots.Select((s, i) => (s, i)))
            {
                if (pointerY >= s.Root.Bounds.Y && pointerY <= s.Root.Bounds.Y + s.Root.Bounds.Height)
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex != currentIndex && currentIndex >= 0)
            {
                SyncEditorsToCells(tab);
                var moved = tab.Notebook.Cells[currentIndex];
                tab.Notebook.Cells.RemoveAt(currentIndex);
                tab.Notebook.Cells.Insert(targetIndex, moved);
                RenderCells(tab);
                MarkDirty(tab);
            }
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        header.Children.Add(grip);
        header.Children.Add(collapseButton);
        header.Children.Add(typeBox);
        header.Children.Add(upButton);
        header.Children.Add(downButton);
        header.Children.Add(deleteButton);
        header.Children.Add(favoriteButton);
        header.Children.Add(hideInputToggle);
        header.Children.Add(skipRunAllToggle);
        header.Children.Add(runAboveButton);
        header.Children.Add(runBelowButton);
        header.Children.Add(actionButton);
        header.Children.Add(execBadge);
        header.Children.Add(statusText);

        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(header);
        body.Children.Add(editor);
        body.Children.Add(sourcePreview);
        body.Children.Add(diagnosticsText);
        body.Children.Add(errorText);
        body.Children.Add(outputHeader);
        body.Children.Add(outputPanel);

        slot.Root = new Border
        {
            Background = _base,
            BorderBrush = _surface0,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12),
            Child = body,
        };

        return slot;
    }

    // ---------- diagnostics / hover ----------

    // Only surfaces errors, not warnings — a lower-noise "will this even compile" signal before Run,
    // not a full Problems panel. No inline squiggle underlines (would need a custom AvaloniaEdit
    // render layer); a text summary below the editor is the bounded version of this feature.
    private static async Task RefreshDiagnosticsAsync(CellSlot slot, TextBlock diagnosticsText)
    {
        var diagnostics = await slot.Tab.Session.GetDiagnosticsAsync(slot.Cell.Id.ToString(), slot.Editor.Text);
        var errors = diagnostics.Where(d => d.Severity == "Error").ToList();

        if (errors.Count == 0)
        {
            diagnosticsText.IsVisible = false;
            return;
        }

        diagnosticsText.Text = string.Join("\n", errors.Take(5).Select(d => $"⚠ {d.Line}:{d.Column} {d.Message}"));
        diagnosticsText.IsVisible = true;
    }

    private static async Task ShowHoverAsync(CellSlot slot, TextEditor editor, Point point)
    {
        var visualPosition = editor.GetPositionFromPoint(point);
        if (visualPosition is null)
            return;

        var offset = editor.Document.GetOffset(visualPosition.Value.Line, visualPosition.Value.Column);
        var text = await slot.Tab.Session.GetHoverAsync(slot.Cell.Id.ToString(), editor.Text, offset);
        if (string.IsNullOrWhiteSpace(text))
            return;

        ToolTip.SetTip(editor, text);
        ToolTip.SetIsOpen(editor, true);
    }

    // ---------- execution ----------

    private async Task RunCellAsync(CellSlot slot)
    {
        var tab = slot.Tab;
        if (!tab.Notebook.Trusted)
            return;

        slot.ActionButton.IsEnabled = false;
        slot.Cell.Outputs.Clear();
        slot.OutputPanel.Children.Clear();
        slot.Cell.Source = slot.Editor.Text;
        slot.StatusText.Text = "⏳ uruchamianie...";
        slot.StatusText.Foreground = _overlay0;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await tab.Session.RunCellAsync(
            slot.Cell.Id.ToString(),
            slot.Cell.Source,
            (mime, data) => Dispatcher.UIThread.Post(() => AppendOutput(slot, mime, data)),
            error => Dispatcher.UIThread.Post(() => AppendOutput(slot, "text/plain", $"ERROR: {error}")));
        stopwatch.Stop();

        slot.Cell.ExecutionCount = result.ExecutionCount;
        slot.ExecBadge.Text = $"[{result.ExecutionCount}]";
        slot.StatusText.Text = $"{(result.Success ? "✓" : "✗")} {FormatDuration(stopwatch.Elapsed)}";
        slot.StatusText.Foreground = result.Success ? _green : _red;
        slot.ActionButton.IsEnabled = true;

        // Fresh output always starts expanded — a manual collapse from a previous run shouldn't hide the
        // new result.
        SetOutputCollapsed(slot, collapsed: false);
        slot.OutputHeader.IsVisible = slot.Cell.Outputs.Count > 0;
        slot.OutputHeaderLabel.Text = $"Wynik ({slot.Cell.Outputs.Count})";

        await SaveCurrentNotebookAsync(tab);

        if (_sidePanelMode == SidePanelMode.Variables && CurrentTab == tab)
            await RefreshSidePanelAsync();
    }

    private async Task GenerateAsync(CellSlot slot, TextBlock errorText)
    {
        var tab = slot.Tab;
        if (!tab.Notebook.Trusted || string.IsNullOrWhiteSpace(slot.Editor.Text))
            return;

        slot.ActionButton.IsEnabled = false;
        errorText.IsVisible = false;

        try
        {
            var code = await _aiGenerator.GenerateAsync(slot.Editor.Text);
            SyncEditorsToCells(tab);
            slot.Cell.Source = code;
            slot.Cell.Type = CellType.Code;
            RenderCells(tab);
            MarkDirty(tab);
        }
        catch (Exception ex)
        {
            errorText.Text = ex.Message;
            errorText.IsVisible = true;
            slot.ActionButton.IsEnabled = true;
        }
    }

    private async Task RestartRunAllAsync(NotebookTab tab)
    {
        SyncEditorsToCells(tab);
        await tab.Session.RestartAsync();

        foreach (var cell in tab.Notebook.Cells)
            cell.Outputs.Clear();
        RenderCells(tab);

        foreach (var slot in tab.Slots.ToList())
        {
            if (slot.Cell.Type == CellType.Code && !slot.Cell.Tags.Contains("skip-on-run-all"))
                await RunCellAsync(slot);
        }
    }

    private async Task TrustTabAsync(NotebookTab tab)
    {
        if (tab.Notebook.Trusted)
            return;

        tab.Notebook.Trusted = true;
        tab.TrustButton.IsVisible = false;
        RenderCells(tab);
        await SaveCurrentNotebookAsync(tab);
    }

    private void AddCellToTab(NotebookTab tab)
    {
        SyncEditorsToCells(tab);
        tab.Notebook.Cells.Add(new Cell());
        RenderCells(tab);
        MarkDirty(tab);
    }

    private async Task SaveTabAsync(NotebookTab tab)
    {
        SyncEditorsToCells(tab);
        await SaveCurrentNotebookAsync(tab);
    }

    // "Run Above"/"Run Below" — unlike RestartRunAllAsync, no kernel restart: they build on whatever
    // state the kernel already has, same as running each of those cells by hand in order would.
    private async Task RunRangeAsync(NotebookTab tab, int fromIndex, int toIndex)
    {
        SyncEditorsToCells(tab);
        var lo = Math.Max(0, fromIndex);
        var hi = Math.Min(toIndex, tab.Slots.Count - 1);
        for (var i = lo; i <= hi; i++)
        {
            if (tab.Slots[i].Cell.Type == CellType.Code && !tab.Slots[i].Cell.Tags.Contains("skip-on-run-all"))
                await RunCellAsync(tab.Slots[i]);
        }
    }

    // Mirrors the Web frontend's AppendOutput: a single Console.WriteLine(nonStringValue) arrives as two
    // onOutput calls (the value, then the trailing newline) — merge consecutive text/plain chunks instead
    // of rendering a stray empty box under real output.
    private void AppendOutput(CellSlot slot, string mimeType, string data)
    {
        var last = slot.Cell.Outputs.Count > 0 ? slot.Cell.Outputs[^1] : null;
        var canMerge = mimeType == "text/plain"
            && last is { MimeType: "text/plain" }
            && !data.StartsWith("ERROR:")
            && !last.Data.StartsWith("ERROR:");

        if (canMerge)
        {
            var merged = last! with { Data = last.Data + data };
            slot.Cell.Outputs[^1] = merged;
            if (slot.OutputPanel.Children[^1] is Border { Child: TextBlock tb })
                tb.Text = merged.Data;
        }
        else
        {
            var output = new CellOutput(mimeType, data);
            slot.Cell.Outputs.Add(output);
            slot.OutputPanel.Children.Add(RenderOutput(output));
        }
    }

    private Control RenderOutput(CellOutput output)
    {
        if (output.MimeType == "text/html")
            return HtmlRenderer.Render(output.Data, _text);

        if (output.MimeType == "image/png")
        {
            try
            {
                var bytes = Convert.FromBase64String(output.Data);
                using var ms = new MemoryStream(bytes);
                return new Border
                {
                    Background = _crust,
                    BorderBrush = _surface0,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new Image { Source = new Bitmap(ms), MaxWidth = 600 },
                };
            }
            catch
            {
                return new TextBlock { Text = "[błąd renderowania obrazu]", Foreground = _red };
            }
        }

        return new Border
        {
            Background = _crust,
            BorderBrush = _surface0,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6),
            Child = new TextBlock
            {
                Text = output.Data,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = MonoFont,
                Foreground = output.Data.StartsWith("ERROR:") ? _red : _green,
            },
        };
    }

    // ---------- syntax highlighting / theme ----------

    private void InstallHighlighting(CellSlot slot, CellType type)
    {
        if (type != CellType.Code)
        {
            slot.Installation = null;
            return;
        }

        try
        {
            var installation = slot.Editor.InstallTextMate(RegistryOptions);
            installation.SetTheme(CurrentRawTheme);
            var language = RegistryOptions.GetLanguageByExtension(".cs");
            if (language is not null)
                installation.SetGrammar(RegistryOptions.GetScopeByLanguageId(language.Id));
            slot.Installation = installation;
        }
        catch
        {
            // best-effort — plain editing still works without highlighting
        }
    }

    private static IRawTheme LoadEmbeddedTheme(string resourceSuffix)
    {
        var assembly = typeof(MainWindow).Assembly;
        var resourceName = assembly.GetManifestResourceNames().First(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return TextMateSharp.Internal.Themes.Reader.ThemeReader.ReadThemeSync(reader);
    }

    private static IRawTheme MochaTheme => _mochaTheme ??= LoadEmbeddedTheme("catppuccin-mocha-theme.json");
    private static IRawTheme LatteTheme => _latteTheme ??= LoadEmbeddedTheme("catppuccin-latte-theme.json");
    private IRawTheme CurrentRawTheme => _isDark ? MochaTheme : LatteTheme;

    private void ThemeToggle_Click(object? sender, RoutedEventArgs e)
    {
        _isDark = !_isDark;
        ThemeToggleButton.Content = _isDark ? "🌙" : "☀️";
        Application.Current!.RequestedThemeVariant = _isDark ? ThemeVariant.Dark : ThemeVariant.Light;

        foreach (var (key, mocha, latte) in PaletteMap)
            ((SolidColorBrush)Application.Current!.Resources[key]!).Color = Color.Parse(_isDark ? mocha : latte);

        foreach (var tab in _tabs)
            foreach (var slot in tab.Slots)
                slot.Installation?.SetTheme(CurrentRawTheme);
    }

    // ---------- variables / packages side panel ----------

    private async void ToggleVariables_Click(object? sender, RoutedEventArgs e) => await ToggleSidePanelAsync(SidePanelMode.Variables);
    private async void TogglePackages_Click(object? sender, RoutedEventArgs e) => await ToggleSidePanelAsync(SidePanelMode.Packages);
    private async void ToggleOutline_Click(object? sender, RoutedEventArgs e) => await ToggleSidePanelAsync(SidePanelMode.Outline);
    private async void RefreshSidePanel_Click(object? sender, RoutedEventArgs e) => await RefreshSidePanelAsync();

    private void SidePanelList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_sidePanelMode != SidePanelMode.Outline || SidePanelList.SelectedItem is not OutlineEntry entry)
            return;

        var tab = CurrentTab;
        if (tab is null || entry.SlotIndex >= tab.Slots.Count)
            return;

        tab.Slots[entry.SlotIndex].Root.BringIntoView();
    }

    private void CloseSidePanel_Click(object? sender, RoutedEventArgs e)
    {
        _sidePanelMode = SidePanelMode.None;
        SidePanel.IsVisible = false;
    }

    private async Task ToggleSidePanelAsync(SidePanelMode mode)
    {
        if (_sidePanelMode == mode && SidePanel.IsVisible)
        {
            _sidePanelMode = SidePanelMode.None;
            SidePanel.IsVisible = false;
            return;
        }

        _sidePanelMode = mode;
        SidePanel.IsVisible = true;
        SidePanelTitle.Text = mode switch
        {
            SidePanelMode.Variables => "Zmienne",
            SidePanelMode.Packages => "Paczki NuGet",
            SidePanelMode.Outline => "Spis treści",
            _ => "",
        };
        await RefreshSidePanelAsync();
    }

    private async Task RefreshSidePanelAsync()
    {
        var tab = CurrentTab;
        if (tab is null || _sidePanelMode == SidePanelMode.None)
            return;

        if (_sidePanelMode == SidePanelMode.Variables)
        {
            var vars = await tab.Session.GetVariablesAsync();
            SidePanelList.ItemsSource = vars.Count == 0
                ? new[] { "(brak zmiennych)" }
                : vars.Select(v => $"{v.Name} : {v.Type} = {Truncate(v.Value, 200)}").ToList();
        }
        else if (_sidePanelMode == SidePanelMode.Packages)
        {
            var pkgs = await tab.Session.GetPackagesAsync();
            SidePanelList.ItemsSource = pkgs.Count == 0
                ? new[] { "(brak zainstalowanych paczek)" }
                : pkgs.Select(p => $"{p.Id} @ {p.Version}").ToList();
        }
        else
        {
            var outline = BuildOutline(tab);
            SidePanelList.ItemsSource = outline.Count == 0
                ? new[] { "(brak nagłówków markdown)" }
                : outline.Cast<object>().ToList();
        }
    }

    // ---------- outline (markdown headers) ----------

    private sealed record OutlineEntry(string Label, int SlotIndex)
    {
        public override string ToString() => Label;
    }

    private static List<OutlineEntry> BuildOutline(NotebookTab tab)
    {
        var entries = new List<OutlineEntry>();
        for (var i = 0; i < tab.Slots.Count; i++)
        {
            var slot = tab.Slots[i];
            if (slot.Cell.Type != CellType.Markdown)
                continue;

            foreach (var line in slot.Editor.Text.Split('\n'))
            {
                var trimmed = line.TrimStart();
                var level = 0;
                while (level < trimmed.Length && trimmed[level] == '#')
                    level++;

                if (level is > 0 and <= 6 && level < trimmed.Length && trimmed[level] == ' ')
                {
                    var title = trimmed[(level + 1)..].Trim();
                    entries.Add(new OutlineEntry(new string(' ', (level - 1) * 2) + title, i));
                }
            }
        }
        return entries;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static string FormatDuration(TimeSpan elapsed) =>
        elapsed.TotalMilliseconds < 1000 ? $"{elapsed.TotalMilliseconds:F0}ms" : $"{elapsed.TotalSeconds:F2}s";

    // ---------- export ----------

    private async void ExportCs_Click(object? sender, RoutedEventArgs e)
    {
        var tab = CurrentTab;
        if (tab is null)
            return;
        SyncEditorsToCells(tab);

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Eksportuj do .cs",
            SuggestedFileName = Path.GetFileNameWithoutExtension(tab.Path) + ".cs",
            FileTypeChoices = [new FilePickerFileType("C# script") { Patterns = ["*.cs"] }],
        });
        if (file is null)
            return;

        var sb = new StringBuilder();
        var n = 1;
        foreach (var cell in tab.Notebook.Cells.Where(c => c.Type == CellType.Code))
        {
            sb.AppendLine($"// --- Cell {n++} ---");
            sb.AppendLine(cell.Source);
            sb.AppendLine();
        }

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(sb.ToString());
    }

    private async void ExportHtml_Click(object? sender, RoutedEventArgs e)
    {
        var tab = CurrentTab;
        if (tab is null)
            return;
        SyncEditorsToCells(tab);

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Eksportuj do .html",
            SuggestedFileName = Path.GetFileNameWithoutExtension(tab.Path) + ".html",
            FileTypeChoices = [new FilePickerFileType("HTML") { Patterns = ["*.html"] }],
        });
        if (file is null)
            return;

        var html = BuildHtmlExport(tab.Notebook, Path.GetFileNameWithoutExtension(tab.Path));
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(html);
    }

    private async void ExportIpynb_Click(object? sender, RoutedEventArgs e)
    {
        var tab = CurrentTab;
        if (tab is null)
            return;
        SyncEditorsToCells(tab);

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Eksportuj do .ipynb",
            SuggestedFileName = Path.GetFileNameWithoutExtension(tab.Path) + ".ipynb",
            FileTypeChoices = [new FilePickerFileType("Jupyter Notebook") { Patterns = ["*.ipynb"] }],
        });
        if (file is null)
            return;

        var json = BuildIpynbJson(tab.Notebook);
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(json);
    }

    // Real nbformat v4.5 — a notebook exported this way opens in actual Jupyter/JupyterLab/VS Code's
    // notebook viewer/nbviewer/GitHub's own .ipynb preview, not just a lookalike. The "Ai" cell type has
    // no nbformat equivalent — exported as a code cell (its prompt text as-is), same as after GenerateAsync
    // converts it in-app.
    private static string BuildIpynbJson(Notebook notebook)
    {
        var cells = new JsonArray();
        foreach (var cell in notebook.Cells)
        {
            var isMarkdown = cell.Type == CellType.Markdown;
            var cellObj = new JsonObject
            {
                ["cell_type"] = isMarkdown ? "markdown" : "code",
                ["metadata"] = new JsonObject(),
                ["source"] = ToIpynbLines(cell.Source),
            };

            if (!isMarkdown)
            {
                cellObj["execution_count"] = cell.ExecutionCount is { } n ? JsonValue.Create(n) : null;
                var outputs = new JsonArray();
                foreach (var output in cell.Outputs)
                    outputs.Add(BuildIpynbOutput(output));
                cellObj["outputs"] = outputs;
            }

            cells.Add(cellObj);
        }

        var root = new JsonObject
        {
            ["cells"] = cells,
            ["metadata"] = new JsonObject
            {
                ["kernelspec"] = new JsonObject { ["display_name"] = "C# (SharpNotebook)", ["language"] = "csharp", ["name"] = "csharp" },
                ["language_info"] = new JsonObject { ["name"] = "csharp" },
            },
            ["nbformat"] = 4,
            ["nbformat_minor"] = 5,
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    // nbformat's "source"/"text" fields are an array of lines, each (except the last) keeping its
    // trailing \n — not one big string with embedded newlines. A source that itself ends with \n must NOT
    // produce a spurious empty trailing element (naive Split('\n') does exactly that) — the preceding
    // element's own "\n" already accounts for it, matching Jupyter's own writer.
    private static JsonArray ToIpynbLines(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        var endsWithNewline = normalized.EndsWith('\n');
        var trimmed = endsWithNewline ? normalized[..^1] : normalized;
        var lines = trimmed.Split('\n');

        var arr = new JsonArray();
        for (var i = 0; i < lines.Length; i++)
        {
            var isLastLine = i == lines.Length - 1;
            arr.Add(isLastLine && !endsWithNewline ? lines[i] : lines[i] + "\n");
        }
        return arr;
    }

    private static JsonObject BuildIpynbOutput(CellOutput output)
    {
        if (output.MimeType == "text/plain" && output.Data.StartsWith("ERROR:"))
        {
            var message = output.Data["ERROR:".Length..].TrimStart();
            return new JsonObject
            {
                ["output_type"] = "error",
                ["ename"] = "Error",
                ["evalue"] = message,
                ["traceback"] = new JsonArray(message),
            };
        }

        if (output.MimeType == "text/plain")
        {
            return new JsonObject
            {
                ["output_type"] = "stream",
                ["name"] = "stdout",
                ["text"] = ToIpynbLines(output.Data),
            };
        }

        var data = new JsonObject();
        if (output.MimeType == "image/png")
            data["image/png"] = output.Data;
        else if (output.MimeType == "text/html")
            data["text/html"] = ToIpynbLines(output.Data);
        else
            data["text/plain"] = ToIpynbLines(output.Data);

        return new JsonObject
        {
            ["output_type"] = "display_data",
            ["data"] = data,
            ["metadata"] = new JsonObject(),
        };
    }

    private static string BuildHtmlExport(Notebook notebook, string title)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(WebUtility.HtmlEncode(title)).Append("</title>");
        sb.Append("<style>body{background:#1e1e2e;color:#cdd6f4;font-family:monospace;padding:2em;} pre{background:#11111b;color:#a6e3a1;padding:0.8em;white-space:pre-wrap;border:1px solid #313244;} .src{background:#181825;padding:0.8em;border:1px solid #313244;white-space:pre-wrap;margin-bottom:0.4em;}</style></head><body>");
        sb.Append("<h1>").Append(WebUtility.HtmlEncode(title)).Append("</h1>");

        foreach (var cell in notebook.Cells)
        {
            sb.Append("<div class=\"src\">").Append(WebUtility.HtmlEncode(cell.Source)).Append("</div>");
            foreach (var output in cell.Outputs)
            {
                if (output.MimeType == "image/png")
                    sb.Append($"<img src=\"data:image/png;base64,{output.Data}\" style=\"max-width:600px;\" />");
                else if (output.MimeType == "text/html")
                    sb.Append(output.Data);
                else
                    sb.Append("<pre>").Append(WebUtility.HtmlEncode(output.Data)).Append("</pre>");
            }
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    // ---------- data holders ----------

    private sealed class NotebookTab
    {
        public required string Path;
        public required Notebook Notebook;
        public required NotebookSession Session;
        public required StackPanel CellsPanel;
        public required ScrollViewer Scroll;
        public List<CellSlot> Slots { get; } = new();
        public bool Dirty;
        public TextBlock DirtyDot = null!;
        public TabItem TabItem = null!;
        public Button TrustButton = null!;
    }

    private sealed class CellSlot(NotebookTab tab, Cell cell, TextEditor editor, TextBlock execBadge, TextBlock statusText, StackPanel outputPanel, Button actionButton)
    {
        public NotebookTab Tab { get; } = tab;
        public Cell Cell { get; } = cell;
        public TextEditor Editor { get; } = editor;
        public TextBlock ExecBadge { get; } = execBadge;
        public TextBlock StatusText { get; } = statusText;
        public StackPanel OutputPanel { get; } = outputPanel;
        public Button ActionButton { get; } = actionButton;
        public Control Root { get; set; } = null!;
        public TextMate.Installation? Installation { get; set; }
        public StackPanel OutputHeader { get; set; } = null!;
        public TextBlock OutputHeaderLabel { get; set; } = null!;
        public Button OutputCollapseButton { get; set; } = null!;
        public TextBlock SourcePreview { get; set; } = null!;
        public Button SourceCollapseButton { get; set; } = null!;
        public bool SourceCollapsed { get; set; }
    }
}
