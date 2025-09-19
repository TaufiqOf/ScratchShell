using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using ListViewItem = Wpf.Ui.Controls.ListViewItem;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;
using System.IO;
using ScratchShell.Services;

namespace ScratchShell.UserControls.BrowserControl;

public enum BrowserViewMode
{
    List,
    Grid
}

public partial class BrowserUserControl : UserControl
{
    public ObservableCollection<BrowserItem> Items { get; private set; } = new ObservableCollection<BrowserItem>();

    public event Action<bool>? OnProgress;

    // Delegate events
    public event Action<BrowserItem>? CopyRequested;

    public event Action<BrowserItem>? CutRequested;

    public event Action<BrowserItem>? PasteRequested;

    public event Action<BrowserItem>? UploadRequested;

    public event Action<BrowserItem>? DownloadRequested;

    public event Action<BrowserItem>? DeleteRequested;

    public event Action<BrowserItem>? EnterRequested;

    public event Action<BrowserItem>? RenameRequested;

    // New events for inline editing
    public event Action<BrowserItem, string>? ItemRenamed;

    public event Action<BrowserItem>? NewFolderCreated;

    public event Action<BrowserItem>? ItemEditCancelled;

    // New events for empty space context menu
    public event Action? EmptySpacePasteRequested;

    public event Action? EmptySpaceUploadRequested;

    public event Action? EmptySpaceNewFolderRequested;

    // Multi-select events
    public event Action<List<BrowserItem>>? MultiCopyRequested;

    public event Action<List<BrowserItem>>? MultiCutRequested;

    public event Action<List<BrowserItem>>? MultiDeleteRequested;

    // Selection change event
    public event Action<int>? SelectionChanged;

    // View mode change event
    public event Action<BrowserViewMode>? ViewModeChanged;

    // Refresh event
    public event Action? RefreshRequested;

    // Progress events - NEW
    public event Action<bool, string, int?, int?>? ProgressChanged;

    public event Action? CancelRequested;

    // Drag and drop events - NEW
    public event Action<string[]>? FilesDropped;

    private ContextMenu contextMenu;
    private ContextMenu emptySpaceContextMenu;
    private readonly Dictionary<string, MenuItem> menuItems = new();
    private readonly Dictionary<string, MenuItem> emptySpaceMenuItems = new();
    private BrowserItem? currentlyEditingItem = null;
    private bool _isIsBrowserEnabled = false;
    private BrowserViewMode _currentViewMode = BrowserViewMode.Grid;
    private readonly List<BrowserItem> _gridSelectedItems = new();
    private BrowserItem? _lastClickedGridItem = null;

    /// <summary>
    /// Gets or sets whether the browser control is enabled for user interaction
    /// </summary>
    public bool IsBrowserEnabled
    {
        get => _isIsBrowserEnabled;
        set => _isIsBrowserEnabled = value;
    }

    /// <summary>
    /// Gets or sets the current view mode (List or Grid)
    /// </summary>
    public BrowserViewMode CurrentViewMode
    {
        get => _currentViewMode;
        set
        {
            if (_currentViewMode != value)
            {
                _currentViewMode = value;
                UpdateViewMode();
                ViewModeChanged?.Invoke(value);
            }
        }
    }

    private string? _lastSortProperty;
    private ListSortDirection _lastSortDirection = ListSortDirection.Ascending;

    public BrowserUserControl()
    {
        
        InitializeComponent();
        BrowserList.ItemsSource = Items;
        BrowserGrid.ItemsSource = Items;
        BrowserList.MouseDoubleClick += BrowserListMouseDoubleClick;
        BrowserList.PreviewMouseDown += BrowserListPreviewMouseDown;
        BrowserList.SelectionChanged += BrowserListSelectionChanged;
        BrowserList.PreviewMouseLeftButtonDown += BrowserListPreviewMouseLeftButtonDown;

        // Add handler for BrowserGrid PreviewMouseDown with handledEventsToo = true
        BrowserGrid.AddHandler(
            UIElement.PreviewMouseDownEvent,
            new MouseButtonEventHandler(BrowserGridPreviewMouseDown),
            true // handledEventsToo
        );

        SetupContextMenu();
        SetupEmptySpaceContextMenu();
        UpdateViewMode();
        
        // Initialize sort UI
        InitializeSortUI();
        
        // Subscribe to language changes to update context menus
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        
        // Subscribe to Unloaded event for cleanup
        this.Unloaded += BrowserUserControlUnloaded;
    }
    
    private void BrowserUserControlUnloaded(object sender, RoutedEventArgs e)
    {
        // Unsubscribe from language change events
        LocalizationManager.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Recreate context menus with localized text
        SetupContextMenu();
        SetupEmptySpaceContextMenu();
        
        // Update sort tooltips
        UpdateSortDirectionIcon(_lastSortDirection);
    }

    /// <summary>
    /// Initialize the sorting UI with default values
    /// </summary>
    private void InitializeSortUI()
    {
        _lastSortProperty = "Name";
        _lastSortDirection = ListSortDirection.Ascending;
        UpdateSortDropdownContent("Name", ListSortDirection.Ascending);
        UpdateSortDirectionIcon(ListSortDirection.Ascending);
    }

    #region View Mode Management

    private void UpdateViewMode()
    {
        switch (_currentViewMode)
        {
            case BrowserViewMode.List:
                BrowserList.Visibility = Visibility.Visible;
                GridScrollViewer.Visibility = Visibility.Collapsed;
                ListViewButton.Background = System.Windows.Media.Brushes.LightBlue;
                GridViewButton.Background = System.Windows.Media.Brushes.Transparent;
                break;

            case BrowserViewMode.Grid:
                BrowserList.Visibility = Visibility.Collapsed;
                GridScrollViewer.Visibility = Visibility.Visible;
                ListViewButton.Background = System.Windows.Media.Brushes.Transparent;
                GridViewButton.Background = System.Windows.Media.Brushes.LightBlue;
                break;
        }
    }

    private void ListViewButtonClick(object sender, RoutedEventArgs e)
    {
        CurrentViewMode = BrowserViewMode.List;
    }

    private void GridViewButtonClick(object sender, RoutedEventArgs e)
    {
        CurrentViewMode = BrowserViewMode.Grid;
    }

    #endregion View Mode Management

    #region Grid View Event Handlers

    private void GridItemMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is BrowserItem item)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Toggle selection
                if (_gridSelectedItems.Contains(item))
                {
                    _gridSelectedItems.Remove(item);
                    item.IsSelected = false;
                }
                else
                {
                    _gridSelectedItems.Add(item);
                    item.IsSelected = true;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Shift && _lastClickedGridItem != null && _gridSelectedItems.Any())
            {
                // Proper range selection between last anchor and current item
                SelectGridRange(_lastClickedGridItem, item);
            }
            else
            {
                // Single selection
                ClearGridSelection();
                _gridSelectedItems.Add(item);
                item.IsSelected = true;
            }

            _lastClickedGridItem = item;
            SelectionChanged?.Invoke(GetSelectedItemCount());
        }
    }

    private void GridItemMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is BrowserItem item)
        {
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            if (shift && _lastClickedGridItem != null && _gridSelectedItems.Any())
            {
                // Shift + Right click extends selection range
                SelectGridRange(_lastClickedGridItem, item);
            }
            else if (control)
            {
                // Ctrl + right click toggle
                if (_gridSelectedItems.Contains(item))
                {
                    _gridSelectedItems.Remove(item);
                    item.IsSelected = false;
                }
                else
                {
                    _gridSelectedItems.Add(item);
                    item.IsSelected = true;
                }
            }
            else
            {
                // Normal right-click behavior: select item (clearing others) if not already part of selection
                if (!_gridSelectedItems.Contains(item))
                {
                    ClearGridSelection();
                    _gridSelectedItems.Add(item);
                    item.IsSelected = true;
                }
            }

            _lastClickedGridItem = item;
            SelectionChanged?.Invoke(GetSelectedItemCount());

            if (item.Name == "..")
            {
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Cut"), false);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Copy"), false);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Paste"), false);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Rename"), false);
                SetMenuVisibility("Separator", false);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Upload"), false);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Download"), false);
                SetMenuVisibility(LocalizationManager.GetString("General_Delete"), false);
            }
            else if (item.IsFolder)
            {
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Cut"), true);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Copy"), true);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Paste"), true);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Rename"), true);
                SetMenuVisibility("Separator", false);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Upload"), true);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Download"), true);
                SetMenuVisibility(LocalizationManager.GetString("General_Delete"), true);
            }
            else
            {
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Cut"), true);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Copy"), true);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Paste"), true);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Rename"), true);
                SetMenuVisibility("Separator", true);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Upload"), false);
                SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Download"), true);
                SetMenuVisibility(LocalizationManager.GetString("General_Delete"), true);
            }

            contextMenu.PlacementTarget = BrowserGrid;
            contextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void ClearGridSelection()
    {
        // Clear the IsSelected property for all items
        foreach (var item in _gridSelectedItems)
        {
            item.IsSelected = false;
        }
        _gridSelectedItems.Clear();
    }

    private void BrowserGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            RaiseContextEvent(EnterRequested);
        }
    }

    private void BrowserGridPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Commit any pending edits when clicking elsewhere
        if (currentlyEditingItem != null && e.ChangedButton == MouseButton.Left)
        {
            var depObj = (DependencyObject)e.OriginalSource;
            while (depObj != null && depObj is not TextBox)
            {
                depObj = VisualTreeHelper.GetParent(depObj);
            }

            if (depObj is not TextBox)
            {
                CommitEdit(currentlyEditingItem);
            }
        }
    }

    private void BrowserGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            // Check if we clicked on empty space
            var hitTest = VisualTreeHelper.HitTest(BrowserGrid, e.GetPosition(BrowserGrid));
            if (hitTest?.VisualHit == BrowserGrid)
            {
                emptySpaceContextMenu.PlacementTarget = BrowserGrid;
                emptySpaceContextMenu.IsOpen = true;
                e.Handled = true;
            }
            else
            {
                // Check if we're clicking on empty area - simplified check
                var target = e.OriginalSource as DependencyObject;
                if (target != null)
                {
                    // Walk up the visual tree to see if we hit a Border with BrowserItem data context
                    while (target != null && !(target is Border border && border.DataContext is BrowserItem))
                    {
                        target = VisualTreeHelper.GetParent(target);
                    }

                    // If we didn't find a Border with BrowserItem, we clicked on empty space
                    if (target == null)
                    {
                        emptySpaceContextMenu.PlacementTarget = BrowserGrid;
                        emptySpaceContextMenu.IsOpen = true;
                        e.Handled = true;
                    }
                }
            }
        }
    }

    private void BrowserGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F2:
                if (_gridSelectedItems.Count == 1 && _gridSelectedItems[0].Name != "..")
                {
                    StartInlineRename(_gridSelectedItems[0]);
                    e.Handled = true;
                }
                break;

            case Key.F5:
                RefreshRequested?.Invoke();
                e.Handled = true;
                break;

            case Key.A:
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    SelectAllValidItems();
                    e.Handled = true;
                }
                break;

            case Key.Delete:
                var selectedItems = GetSelectedItems().Where(item => item.Name != "..").ToList();
                if (selectedItems.Any())
                {
                    if (selectedItems.Count == 1)
                    {
                        RaiseContextEvent(DeleteRequested);
                    }
                    else
                    {
                        HandleMultiDeleteEvent(selectedItems);
                    }
                    e.Handled = true;
                }
                break;
        }
    }

    #endregion Grid View Event Handlers

    // Event handlers for inline editing
    private void DisplayTextBlockMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only allow rename on slow double click or F2, not single click
        // This will be handled by F2 key or context menu
    }

    private void EditTextBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private void EditTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is BrowserItem item)
        {
            CommitEdit(item);
        }
    }

    private void EditTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is BrowserItem item)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    CommitEdit(item);
                    e.Handled = true;
                    break;

                case Key.Escape:
                    CancelEdit(item);
                    e.Handled = true;
                    break;
            }
        }
    }

    private void BrowserListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F2:
                if (GetActiveSelectedItem() is BrowserItem item && item.Name != "..")
                {
                    StartInlineRename(item);
                    e.Handled = true;
                }
                break;

            case Key.F5:
                RefreshRequested?.Invoke();
                e.Handled = true;
                break;

            case Key.A:
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    SelectAllValidItems();
                    e.Handled = true;
                }
                break;

            case Key.Delete:
                var selectedItems = GetSelectedItems().Where(item => item.Name != "..").ToList();
                if (selectedItems.Any())
                {
                    if (selectedItems.Count == 1)
                    {
                        RaiseContextEvent(DeleteRequested);
                    }
                    else
                    {
                        HandleMultiDeleteEvent(selectedItems);
                    }
                    e.Handled = true;
                }
                break;
        }
    }

    private BrowserItem? GetActiveSelectedItem()
    {
        return _currentViewMode switch
        {
            BrowserViewMode.List => BrowserList.SelectedItem as BrowserItem,
            BrowserViewMode.Grid => _gridSelectedItems.FirstOrDefault(),
            _ => null
        };
    }

    private void StartInlineRename(BrowserItem item)
    {
        if (currentlyEditingItem != null)
        {
            CommitEdit(currentlyEditingItem);
        }

        currentlyEditingItem = item;
        item.StartEdit();
    }

    private void CommitEdit(BrowserItem item)
    {
        if (!item.IsInEditMode) return;

        var newName = item.Name?.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            CancelEdit(item);
            return;
        }

        if (ValidateName(newName))
        {
            if (item.IsNewItem)
            {
                // This is the critical event that should fire for new folders
                System.Diagnostics.Debug.WriteLine($"[BrowserUserControl] Firing NewFolderCreated event for: {newName}");
                NewFolderCreated?.Invoke(item);
            }
            else
            {
                var oldName = item.OriginalName;
                if (oldName != newName)
                {
                    System.Diagnostics.Debug.WriteLine($"[BrowserUserControl] Firing ItemRenamed event from: {oldName} to: {newName}");
                    ItemRenamed?.Invoke(item, newName);
                }
                else
                {
                    item.CommitEdit();
                }
            }
        }
        else
        {
            item.Name = item.OriginalName;
            item.CommitEdit();
        }

        currentlyEditingItem = null;
    }

    private void CancelEdit(BrowserItem item)
    {
        if (!item.IsInEditMode) return;

        if (item.IsNewItem)
        {
            Items.Remove(item);
            ItemEditCancelled?.Invoke(item);
        }
        else
        {
            item.CancelEdit();
        }

        currentlyEditingItem = null;
    }

    private bool ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        char[] invalidChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
        if (name.IndexOfAny(invalidChars) >= 0)
            return false;

        string[] reservedNames = { ".", "..", "CON", "PRN", "AUX", "NUL" };
        if (reservedNames.Any(reserved =>
            string.Equals(reserved, name, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    public void StartNewFolderCreation()
    {
        System.Diagnostics.Debug.WriteLine($"[BrowserUserControl] StartNewFolderCreation called");

        if (currentlyEditingItem != null)
        {
            System.Diagnostics.Debug.WriteLine($"[BrowserUserControl] Committing existing edit item: {currentlyEditingItem.Name}");
            CommitEdit(currentlyEditingItem);
        }

        var newFolder = new BrowserItem
        {
            Name = LocalizationManager.GetString("ContextMenu_NewFolder"),
            OriginalName = LocalizationManager.GetString("ContextMenu_NewFolder"),
            IsFolder = true,
            IsNewItem = true,
            LastUpdated = DateTime.Now,
            Size = 0,
            FullPath = ""
        };

        System.Diagnostics.Debug.WriteLine($"[BrowserUserControl] Created new folder item with Name: {newFolder.Name}, IsNewItem: {newFolder.IsNewItem}");
        
        // Use AddItem which will maintain sort order
        AddItem(newFolder);

        switch (_currentViewMode)
        {
            case BrowserViewMode.List:
                BrowserList.SelectedItem = newFolder;
                BrowserList.ScrollIntoView(newFolder);
                System.Diagnostics.Debug.WriteLine($"[BrowserUserControl] Set selection in ListView");
                break;

            case BrowserViewMode.Grid:
                ClearGridSelection();
                _gridSelectedItems.Add(newFolder);
                newFolder.IsSelected = true;
                System.Diagnostics.Debug.WriteLine($"[BrowserUserControl] Set selection in GridView");
                break;
        }

        currentlyEditingItem = newFolder;
        System.Diagnostics.Debug.WriteLine($"[BrowserUserControl] Starting edit on new folder");
        newFolder.StartEdit();

        System.Diagnostics.Debug.WriteLine($"[BrowserUserControl] New folder edit started - IsInEditMode: {newFolder.IsInEditMode}");
    }

    private void BrowserListPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (currentlyEditingItem != null && e.ChangedButton == MouseButton.Left)
        {
            var depObj = (DependencyObject)e.OriginalSource;
            while (depObj != null && depObj is not TextBox)
            {
                depObj = VisualTreeHelper.GetParent(depObj);
            }

            if (depObj is not TextBox)
            {
                CommitEdit(currentlyEditingItem);
            }
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            var depObj = (DependencyObject)e.OriginalSource;
            while (depObj != null && depObj is not ListViewItem)
            {
                depObj = VisualTreeHelper.GetParent(depObj);
            }

            if (depObj is ListViewItem listViewItem && listViewItem.DataContext is BrowserItem item)
            {
                if (item.Name == "..")
                {
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Cut"), false);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Copy"), false);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Paste"), false);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Rename"), false);
                    SetMenuVisibility("Separator", false);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Upload"), false);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Download"), false);
                    SetMenuVisibility(LocalizationManager.GetString("General_Delete"), false);
                    e.Handled = true;
                    return;
                }

                if (item.IsFolder)
                {
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Cut"), true);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Copy"), true);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Paste"), true);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Rename"), true);
                    SetMenuVisibility("Separator", false);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Upload"), true);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Download"), true);
                    SetMenuVisibility(LocalizationManager.GetString("General_Delete"), true);
                }
                else
                {
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Cut"), true);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Copy"), true);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Paste"), true);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Rename"), true);
                    SetMenuVisibility("Separator", true);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Upload"), false);
                    SetMenuVisibility(LocalizationManager.GetString("ContextMenu_Download"), true);
                    SetMenuVisibility(LocalizationManager.GetString("General_Delete"), true);
                }

                e.Handled = true;
            }
        }
    }

    private void BrowserListMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            RaiseContextEvent(EnterRequested);
        }
    }

    private void SetupContextMenu()
    {
        contextMenu = new ContextMenu();
        AddMenuItem(LocalizationManager.GetString("ContextMenu_Cut"), (_, __) => RaiseContextEvent(CutRequested), SymbolRegular.Cut24);
        AddMenuItem(LocalizationManager.GetString("ContextMenu_Copy"), (_, __) => RaiseContextEvent(CopyRequested), SymbolRegular.Copy24);
        AddMenuItem(LocalizationManager.GetString("ContextMenu_Paste"), (_, __) => RaiseContextEvent(PasteRequested), SymbolRegular.ClipboardPaste24);
        AddMenuItem(LocalizationManager.GetString("ContextMenu_Rename"), (_, __) => HandleContextMenuRename(), SymbolRegular.Rename20);

        var sep1 = new Separator();
        contextMenu.Items.Add(sep1);
        menuItems["Separator"] = new MenuItem { Header = "Separator", Visibility = Visibility.Collapsed };
        sep1.DataContext = menuItems["Separator"];

        AddMenuItem(LocalizationManager.GetString("ContextMenu_Upload"), (_, __) => RaiseContextEvent(UploadRequested), SymbolRegular.ArrowUpload24);
        AddMenuItem(LocalizationManager.GetString("ContextMenu_Download"), (_, __) => RaiseContextEvent(DownloadRequested), SymbolRegular.ArrowDownload24);

        var sep2 = new Separator();
        contextMenu.Items.Add(sep2);

        AddMenuItem(LocalizationManager.GetString("General_Delete"), (_, __) => RaiseContextEvent(DeleteRequested), SymbolRegular.Delete24);
    }

    private void SetupEmptySpaceContextMenu()
    {
        emptySpaceContextMenu = new ContextMenu();
        AddEmptySpaceMenuItem(LocalizationManager.GetString("ContextMenu_Paste"), (_, __) => EmptySpacePasteRequested?.Invoke(), SymbolRegular.ClipboardPaste24);
        AddEmptySpaceMenuItem(LocalizationManager.GetString("ContextMenu_Upload"), (_, __) => EmptySpaceUploadRequested?.Invoke(), SymbolRegular.ArrowUpload24);

        var sep = new Separator();
        emptySpaceContextMenu.Items.Add(sep);

        AddEmptySpaceMenuItem(LocalizationManager.GetString("ContextMenu_NewFolder"), (_, __) => EmptySpaceNewFolderRequested?.Invoke(), SymbolRegular.FolderAdd24);
    }

    private void HandleContextMenuRename()
    {
        if (GetActiveSelectedItem() is BrowserItem item && item.Name != "..")
        {
            StartInlineRename(item);
        }
    }

    private void AddMenuItem(string header, RoutedEventHandler handler, SymbolRegular? icon = null)
    {
        var menuItem = new MenuItem
        {
            Header = header,
        };
        menuItem.Icon = icon.HasValue ? new SymbolIcon(icon.Value) : null;
        menuItem.Click += handler;
        contextMenu.Items.Add(menuItem);
        menuItems[header] = menuItem;
    }

    private void AddEmptySpaceMenuItem(string header, RoutedEventHandler handler, SymbolRegular? icon = null)
    {
        var menuItem = new MenuItem
        {
            Header = header,
        };
        menuItem.Icon = icon.HasValue ? new SymbolIcon(icon.Value) : null;
        menuItem.Click += handler;
        emptySpaceContextMenu.Items.Add(menuItem);
        emptySpaceMenuItems[header] = menuItem;
    }

    private void RaiseContextEvent(Action<BrowserItem>? action)
    {
        var selectedItems = GetSelectedItems().Where(item => item.Name != "..").ToList();

        if (selectedItems.Count > 1)
        {
            if (action == CopyRequested)
            {
                HandleMultiCopyEvent(selectedItems);
                return;
            }
            else if (action == CutRequested)
            {
                HandleMultiCutEvent(selectedItems);
                return;
            }
            else if (action == DeleteRequested)
            {
                HandleMultiDeleteEvent(selectedItems);
                return;
            }
        }

        if (GetActiveSelectedItem() is BrowserItem item)
            action?.Invoke(item);
    }

    private void HandleMultiCopyEvent(List<BrowserItem> items)
    {
        MultiCopyRequested?.Invoke(items);
    }

    private void HandleMultiCutEvent(List<BrowserItem> items)
    {
        MultiCutRequested?.Invoke(items);
    }

    private void HandleMultiDeleteEvent(List<BrowserItem> items)
    {
        MultiDeleteRequested?.Invoke(items);
    }

    private void BrowserList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            var depObj = (DependencyObject)e.OriginalSource;
            while (depObj != null && depObj is not ListViewItem)
            {
                depObj = VisualTreeHelper.GetParent(depObj);
            }

            if (depObj is ListViewItem listViewItem && listViewItem.DataContext is BrowserItem item)
            {
                if (item.Name != "..")
                {
                    contextMenu.PlacementTarget = BrowserList;
                    contextMenu.IsOpen = true;
                }
            }
            else
            {
                emptySpaceContextMenu.PlacementTarget = BrowserList;
                emptySpaceContextMenu.IsOpen = true;
            }
        }
    }

    // 📣 Public API

    public void LoadItems(IEnumerable<BrowserItem> items)
    {
        // Clear existing selection
        ClearGridSelection();

        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
            
        // Apply current sort order after loading items
        RefreshSort();
    }

    public void AddItem(BrowserItem item)
    {
        Items.Add(item);
        
        // If we have a current sort applied, reapply it to maintain order
        // This is less efficient but ensures correctness
        if (!string.IsNullOrEmpty(_lastSortProperty))
        {
            RefreshSort();
        }
    }

    /// <summary>
    /// Adds multiple items efficiently while maintaining sort order
    /// </summary>
    /// <param name="items">Items to add</param>
    public void AddItems(IEnumerable<BrowserItem> items)
    {
        foreach (var item in items)
        {
            Items.Add(item);
        }
        
        // Apply sort once after adding all items for better performance
        RefreshSort();
    }

    public void Clear()
    {
        ClearGridSelection();
        Items.Clear();
        currentlyEditingItem = null;
    }

    public void Refresh()
    {
        BrowserList.Items.Refresh();
    }

    // 📣 Context Menu Visibility / Enable Control

    public void SetMenuVisibility(string menuHeader, bool visible)
    {
        if (menuItems.TryGetValue(menuHeader, out var item))
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetMenuEnabled(string menuHeader, bool enabled)
    {
        if (menuItems.TryGetValue(menuHeader, out var item))
            item.IsEnabled = enabled;
    }

    public void SetEmptySpaceMenuVisibility(string menuHeader, bool visible)
    {
        if (emptySpaceMenuItems.TryGetValue(menuHeader, out var item))
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetEmptySpaceMenuEnabled(string menuHeader, bool enabled)
    {
        if (emptySpaceMenuItems.TryGetValue(menuHeader, out var item))
            item.IsEnabled = enabled;
    }

    public void UpdateEmptySpaceContextMenu(bool hasClipboardContent)
    {
        SetEmptySpaceMenuEnabled(LocalizationManager.GetString("ContextMenu_Paste"), hasClipboardContent);
    }

    /// <summary>
    /// Gets the currently selected items in the browser
    /// </summary>
    /// <returns>List of selected BrowserItem objects</returns>
    public List<BrowserItem> GetSelectedItems()
    {
        var selectedItems = new List<BrowserItem>();

        switch (_currentViewMode)
        {
            case BrowserViewMode.List:
                if (BrowserList.SelectedItems != null)
                {
                    foreach (var item in BrowserList.SelectedItems)
                    {
                        if (item is BrowserItem browserItem)
                        {
                            selectedItems.Add(browserItem);
                        }
                    }
                }
                break;

            case BrowserViewMode.Grid:
                selectedItems.AddRange(_gridSelectedItems);
                break;
        }

        return selectedItems;
    }

    /// <summary>
    /// Gets the count of currently selected items (excluding parent directory)
    /// </summary>
    /// <returns>Number of selected valid items</returns>
    public int GetSelectedItemCount()
    {
        return GetSelectedItems().Count(item => item.Name != "..");
    }

    private void SelectAllValidItems()
    {
        switch (_currentViewMode)
        {
            case BrowserViewMode.List:
                BrowserList.SelectAll();
                var parentItem = Items.FirstOrDefault(item => item.Name == "..");
                if (parentItem != null && BrowserList.SelectedItems.Contains(parentItem))
                {
                    BrowserList.SelectedItems.Remove(parentItem);
                }
                break;

            case BrowserViewMode.Grid:
                ClearGridSelection();
                foreach (var item in Items.Where(item => item.Name != ".."))
                {
                    _gridSelectedItems.Add(item);
                    item.IsSelected = true;
                }
                break;
        }
    }

    private void BrowserListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_currentViewMode == BrowserViewMode.List)
        {
            var selectedCount = GetSelectedItemCount();
            SelectionChanged?.Invoke(selectedCount);
        }
    }

    #region Sorting Functionality

    /// <summary>
    /// Current sort property name
    /// </summary>
    public string CurrentSortProperty => _lastSortProperty ?? "Name";

    /// <summary>
    /// Current sort direction
    /// </summary>
    public ListSortDirection CurrentSortDirection => _lastSortDirection;

    /// <summary>
    /// Event handler for sort menu item clicks
    /// </summary>
    private void SortMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var gettype = sender.GetType().Name;
        if (sender is MenuItem menuItem && menuItem.Tag is string tagValue)
        {
            var parts = tagValue.Split(':');
            if (parts.Length == 2)
            {
                var property = parts[0];
                var direction = Enum.Parse<ListSortDirection>(parts[1]);

                ApplySortFromMenu(property, direction);
                UpdateSortDropdownContent(property, direction);
                UpdateSortDirectionIcon(direction);
            }
        }
    }

    /// <summary>
    /// Event handler for sort direction toggle button
    /// </summary>
    private void SortDirectionButton_Click(object sender, RoutedEventArgs e)
    {
        // Toggle the current sort direction
        var newDirection = _lastSortDirection == ListSortDirection.Ascending 
            ? ListSortDirection.Descending 
            : ListSortDirection.Ascending;

        var currentProperty = _lastSortProperty ?? "Name";
        ApplySortFromMenu(currentProperty, newDirection);
        UpdateSortDropdownContent(currentProperty, newDirection);
        UpdateSortDirectionIcon(newDirection);
    }

    /// <summary>
    /// Applies sorting based on menu selection
    /// </summary>
    private void ApplySortFromMenu(string property, ListSortDirection direction)
    {
        _lastSortProperty = property;
        _lastSortDirection = direction;

        string sortProperty = property switch
        {
            "Name" => "Name",
            "Size" => "Size", 
            "Type" => "DisplayType",
            "Date" => "LastUpdated",
            _ => "Name"
        };

        PerformSort(sortProperty, direction);
    }

    /// <summary>
    /// Updates the sort dropdown button content
    /// </summary>
    private void UpdateSortDropdownContent(string property, ListSortDirection direction)
    {
        var directionText = direction == ListSortDirection.Ascending ? "↑" : "↓";
        var propertyText = property switch
        {
            "Name" => LocalizationManager.GetString("UI_Name") ?? "Name",
            "Size" => LocalizationManager.GetString("UI_Size") ?? "Size",
            "Type" => LocalizationManager.GetString("UI_Type") ?? "Type", 
            "Date" => LocalizationManager.GetString("UI_Date") ?? "Date",
            _ => LocalizationManager.GetString("UI_Name") ?? "Name"
        };
        
        SortDropDown.Tag = $"{propertyText} {directionText}";
    }

    /// <summary>
    /// Updates the sort direction icon
    /// </summary>
    private void UpdateSortDirectionIcon(ListSortDirection direction)
    {
        SortDirectionIcon.Symbol = direction == ListSortDirection.Ascending 
            ? SymbolRegular.ArrowUp20 
            : SymbolRegular.ArrowDown20;
            
        var ascendingText = LocalizationManager.GetString("UI_SortAscending") ?? "Sort ascending (click to sort descending)";
        var descendingText = LocalizationManager.GetString("UI_SortDescending") ?? "Sort descending (click to sort ascending)";
        
        SortDirectionButton.ToolTip = direction == ListSortDirection.Ascending
            ? ascendingText
            : descendingText;
    }

    /// <summary>
    /// Enhanced ApplySort method that integrates with the new UI
    /// </summary>
    private void ApplySort(string propertyName)
    {
        if (_lastSortProperty == propertyName)
        {
            _lastSortDirection = _lastSortDirection == ListSortDirection.Ascending 
                ? ListSortDirection.Descending 
                : ListSortDirection.Ascending;
        }
        else
        {
            _lastSortDirection = ListSortDirection.Ascending;
        }
        _lastSortProperty = propertyName;

        PerformSort(propertyName, _lastSortDirection);
        
        // Update UI to reflect the new sort
        var displayProperty = propertyName switch
        {
            "Name" => "Name",
            "LastUpdated" => "Date",
            "DisplayType" => "Type",
            "Size" => "Size",
            _ => "Name"
        };
        
        UpdateSortDropdownContent(displayProperty, _lastSortDirection);
        UpdateSortDirectionIcon(_lastSortDirection);
    }

    /// <summary>
    /// Core sorting logic extracted for reuse
    /// </summary>
    private void PerformSort(string propertyName, ListSortDirection direction)
    {
        IOrderedEnumerable<BrowserItem> sorted;
        
        switch (propertyName)
        {
            case "Name":
                sorted = direction == ListSortDirection.Ascending
                    ? Items.OrderBy(i => i.IsFolder ? 0 : 1).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                    : Items.OrderBy(i => i.IsFolder ? 0 : 1).ThenByDescending(i => i.Name, StringComparer.CurrentCultureIgnoreCase);
                break;

            case "LastUpdated":
                sorted = direction == ListSortDirection.Ascending
                    ? Items.OrderBy(i => i.IsFolder ? 0 : 1).ThenBy(i => i.LastUpdated)
                    : Items.OrderBy(i => i.IsFolder ? 0 : 1).ThenByDescending(i => i.LastUpdated);
                break;

            case "DisplayType":
                sorted = direction == ListSortDirection.Ascending
                    ? Items.OrderBy(i => i.IsFolder ? 0 : 1).ThenBy(i => i.DisplayType)
                    : Items.OrderBy(i => i.IsFolder ? 0 : 1).ThenByDescending(i => i.DisplayType);
                break;

            case "Size":
                sorted = direction == ListSortDirection.Ascending
                    ? Items.OrderBy(i => i.IsFolder ? 0 : 1).ThenBy(i => i.IsFolder ? 0 : i.Size)
                    : Items.OrderBy(i => i.IsFolder ? 0 : 1).ThenByDescending(i => i.IsFolder ? 0 : i.Size);
                break;
            case "Date":
                sorted = direction == ListSortDirection.Ascending
                    ? Items.OrderBy(i => i.IsFolder ? 0 : 1).ThenBy(i=> i.LastUpdated)
                    : Items.OrderBy(i => i.IsFolder ? 0 : 1).ThenByDescending(i=> i.LastUpdated);
                break;

            default:
                return;
        }

        // Always keep parent directory (..) at the top
        var parent = Items.FirstOrDefault(i => i.Name == "..");
        var sortedList = sorted.ToList();
        
        if (parent != null)
        {
            sortedList.Remove(parent);
            sortedList.Insert(0, parent);
        }

        // Clear and rebuild the collection
        Items.Clear();
        foreach (var item in sortedList)
        {
            Items.Add(item);
        }
    }

    /// <summary>
    /// Public method to set initial sort (useful for external callers)
    /// </summary>
    public void SetSort(string property, ListSortDirection direction)
    {
        ApplySortFromMenu(property, direction);
        UpdateSortDropdownContent(property, direction);
        UpdateSortDirectionIcon(direction);
    }

    /// <summary>
    /// Public method to refresh current sort (useful after adding new items)
    /// </summary>
    public void RefreshSort()
    {
        if (!string.IsNullOrEmpty(_lastSortProperty))
        {
            PerformSort(_lastSortProperty, _lastSortDirection);
        }
    }

    #endregion Sorting Functionality

    #region Progress Management

    /// <summary>
    /// Shows or hides the progress overlay
    /// </summary>
    /// <param name="show">True to show progress, false to hide</param>
    /// <param name="message">Progress message to display</param>
    /// <param name="current">Current item number (optional)</param>
    /// <param name="total">Total number of items (optional)</param>
    public void ShowProgress(bool show, string message = "", int? current = null, int? total = null)
    {
        OnProgress?.Invoke(show);
        ToolBar.IsEnabled = !show;
        if (show)
        {
            // Use default localized message if none provided
            var displayMessage = string.IsNullOrEmpty(message) 
                ? LocalizationManager.GetString("Operation_InProgress") ?? "Operation in progress..."
                : message;
                
            if (current.HasValue && total.HasValue)
            {
                displayMessage = $"{displayMessage}";
            }

            ProgressText.Text = displayMessage;
            ProgressOverlay.Visibility = Visibility.Visible;
            CancelOperationButton.IsEnabled = true;

            // Disable browser interaction during operations
            IsBrowserEnabled = false;
        }
        else
        {
            ProgressOverlay.Visibility = Visibility.Collapsed;
            CancelOperationButton.IsEnabled = false;

            // Re-enable browser interaction
            IsBrowserEnabled = true;
        }

        // Notify parent about progress change
        ProgressChanged?.Invoke(show, message, current, total);
    }

    /// <summary>
    /// Event handler for the cancel operation button
    /// </summary>
    private void CancelOperationButtonClick(object sender, RoutedEventArgs e)
    {
        CancelOperationButton.IsEnabled = false;
        ProgressText.Text = LocalizationManager.GetString("Operation_Cancelling") ?? "Cancelling operation...";

        // Notify parent that cancel was requested
        CancelRequested?.Invoke();
    }

    #endregion Progress Management

    #region Drag and Drop Event Handlers

    private void BrowserUserControlDragEnter(object sender, DragEventArgs e)
    {
        HandleDragEvent(e);
    }

    private void BrowserUserControlDragOver(object sender, DragEventArgs e)
    {
        HandleDragEvent(e);
    }

    private void HandleDragEvent(DragEventArgs e)
    {
        // Check if the dragged data contains files
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            // Allow drop if we have files and browser is enabled
            if (files != null && files.Length > 0 && IsBrowserEnabled)
            {
                e.Effects = DragDropEffects.Copy;

                // Show the drag drop overlay
                ShowDragDropOverlay(true, files);
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void BrowserUserControlDragLeave(object sender, DragEventArgs e)
    {
        // Hide the drag drop overlay when leaving the control
        ShowDragDropOverlay(false);
        e.Handled = true;
    }

    private void BrowserUserControlDrop(object sender, DragEventArgs e)
    {
        try
        {
            // Hide the drag drop overlay
            ShowDragDropOverlay(false);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (files != null && files.Length > 0 && IsBrowserEnabled)
                {
                    // Raise the FilesDropped event to notify parent control
                    FilesDropped?.Invoke(files);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error handling drop: {ex.Message}");
        }

        e.Handled = true;
    }

    private void ShowDragDropOverlay(bool show, string[]? files = null)
    {
        if (show && files != null)
        {
            // Update the overlay text based on what's being dropped
            var fileCount = files.Length;
            var folderCount = files.Count(f => Directory.Exists(f));
            var regularFileCount = fileCount - folderCount;

            string primaryText;
            string subText;

            if (fileCount == 1)
            {
                var fileName = Path.GetFileName(files[0]);
                var isFolder = Directory.Exists(files[0]);
                
                var folderText = LocalizationManager.GetString("General_Folder") ?? "folder";
                var fileText = LocalizationManager.GetString("General_File") ?? "file";
                var dropText = LocalizationManager.GetString("DragDrop_DropHere") ?? "Drop {0} '{1}' here to upload";
                
                primaryText = string.Format(dropText, isFolder ? folderText : fileText, fileName);
                
                var folderSubText = LocalizationManager.GetString("DragDrop_FolderRecursive") ?? "Folder contents will be uploaded recursively";
                var fileSubText = LocalizationManager.GetString("DragDrop_FileToDirectory") ?? "File will be uploaded to current directory";
                subText = isFolder ? folderSubText : fileSubText;
            }
            else
            {
                var dropMultipleText = LocalizationManager.GetString("DragDrop_DropMultiple") ?? "Drop {0} item(s) here to upload";
                primaryText = string.Format(dropMultipleText, fileCount);

                if (folderCount > 0 && regularFileCount > 0)
                {
                    var mixedText = LocalizationManager.GetString("DragDrop_FilesAndFolders") ?? "{0} file(s) and {1} folder(s) will be uploaded";
                    subText = string.Format(mixedText, regularFileCount, folderCount);
                }
                else if (folderCount > 0)
                {
                    var foldersText = LocalizationManager.GetString("DragDrop_FoldersOnly") ?? "{0} folder(s) will be uploaded recursively";
                    subText = string.Format(foldersText, folderCount);
                }
                else
                {
                    var filesText = LocalizationManager.GetString("DragDrop_FilesOnly") ?? "{0} file(s) will be uploaded";
                    subText = string.Format(filesText, regularFileCount);
                }
            }

            DragDropText.Text = primaryText;
            DragDropSubText.Text = subText;
            DragDropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            DragDropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    #endregion Drag and Drop Event Handlers

    // Sorting functionality for ListView column headers
    private void GridViewColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader header && header.Column != null)
        {
            string? propertyName = null;
            var headerText = header.Content as string;
            
            // Handle both English and localized headers
            var nameText = LocalizationManager.GetString("UI_Name") ?? "Name";
            var dateText = LocalizationManager.GetString("UI_DateModified") ?? "Date Modified";
            var typeText = LocalizationManager.GetString("UI_Type") ?? "Type";
            var sizeText = LocalizationManager.GetString("UI_Size") ?? "Size";
            
            if (headerText == nameText || headerText == "Name")
                propertyName = "Name";
            else if (headerText == dateText || headerText == "Date Modified")
                propertyName = "LastUpdated";
            else if (headerText == typeText || headerText == "Type")
                propertyName = "DisplayType";
            else if (headerText == sizeText || headerText == "Size")
                propertyName = "Size";
                
            if (!string.IsNullOrEmpty(propertyName))
            {
                ApplySort(propertyName);
            }
        }
    }

    private void BrowserListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var depObj = (DependencyObject)e.OriginalSource;
        while (depObj != null && depObj is not GridViewColumnHeader)
        {
            depObj = VisualTreeHelper.GetParent(depObj);
        }
        if (depObj is GridViewColumnHeader header && header.Column != null)
        {
            string? propertyName = null;
            var headerText = header.Content as string;
            
            // Handle both English and localized headers
            var nameText = LocalizationManager.GetString("UI_Name") ?? "Name";
            var dateText = LocalizationManager.GetString("UI_DateModified") ?? "Date Modified";
            var typeText = LocalizationManager.GetString("UI_Type") ?? "Type";
            var sizeText = LocalizationManager.GetString("UI_Size") ?? "Size";
            
            if (headerText == nameText || headerText == "Name")
                propertyName = "Name";
            else if (headerText == dateText || headerText == "Date Modified")
                propertyName = "LastUpdated";
            else if (headerText == typeText || headerText == "Type")
                propertyName = "DisplayType";
            else if (headerText == sizeText || headerText == "Size")
                propertyName = "Size";
                
            if (!string.IsNullOrEmpty(propertyName))
            {
                ApplySort(propertyName);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Event handler for sort dropdown button click
    /// </summary>
    private void SortDropDownClick(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void GridScrollViewerMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_currentViewMode != BrowserViewMode.Grid) return;
        // Determine if click was on an item by hit testing BrowserGrid children
        var pos = e.GetPosition(BrowserGrid);
        var hit = VisualTreeHelper.HitTest(BrowserGrid, pos);
        if (hit == null)
        {
            emptySpaceContextMenu.PlacementTarget = BrowserGrid;
            emptySpaceContextMenu.IsOpen = true;
            e.Handled = true;
            return;
        }
        // Walk up to see if inside a Border with BrowserItem
        var dp = hit.VisualHit as DependencyObject;
        bool onItem = false;
        while (dp != null)
        {
            if (dp is Border b && b.DataContext is BrowserItem)
            {
                onItem = true; break;
            }
            dp = VisualTreeHelper.GetParent(dp);
        }
        if (!onItem)
        {
            emptySpaceContextMenu.PlacementTarget = BrowserGrid;
            emptySpaceContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void SelectGridRange(BrowserItem anchor, BrowserItem current)
    {
        int anchorIndex = Items.IndexOf(anchor);
        int currentIndex = Items.IndexOf(current);
        if (anchorIndex == -1 || currentIndex == -1)
        {
            return; // Safety
        }

        int start = Math.Min(anchorIndex, currentIndex);
        int end = Math.Max(anchorIndex, currentIndex);

        // Rebuild selection
        ClearGridSelection();
        for (int i = start; i <= end; i++)
        {
            var it = Items[i];
            if (it.Name == "..") continue; // Skip parent directory if present
            if (!_gridSelectedItems.Contains(it))
            {
                _gridSelectedItems.Add(it);
                it.IsSelected = true;
            }
        }
    }
}