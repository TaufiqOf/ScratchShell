using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using ListViewItem = Wpf.Ui.Controls.ListViewItem;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;

namespace ScratchShell.UserControls.BrowserControl;

public enum BrowserViewMode
{
    List,
    Grid
}

public partial class BrowserUserControl : UserControl
{
    public ObservableCollection<BrowserItem> Items { get; private set; } = new ObservableCollection<BrowserItem>();

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
        BrowserList.SelectionChanged += BrowserList_SelectionChanged;
        BrowserList.PreviewMouseLeftButtonDown += BrowserList_PreviewMouseLeftButtonDown;

        // Add handler for BrowserGrid PreviewMouseDown with handledEventsToo = true
        BrowserGrid.AddHandler(
            UIElement.PreviewMouseDownEvent,
            new MouseButtonEventHandler(BrowserGridPreviewMouseDown),
            true // handledEventsToo
        );

        SetupContextMenu();
        SetupEmptySpaceContextMenu();
        UpdateViewMode();
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

    private void ListViewButton_Click(object sender, RoutedEventArgs e)
    {
        CurrentViewMode = BrowserViewMode.List;
    }

    private void GridViewButton_Click(object sender, RoutedEventArgs e)
    {
        CurrentViewMode = BrowserViewMode.Grid;
    }

    #endregion

    #region Grid View Event Handlers

    private void GridItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is BrowserItem item)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Toggle selection
                if (_gridSelectedItems.Contains(item))
                {
                    _gridSelectedItems.Remove(item);
                    UpdateGridItemSelection(border, false);
                }
                else
                {
                    _gridSelectedItems.Add(item);
                    UpdateGridItemSelection(border, true);
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Shift && _lastClickedGridItem != null)
            {
                // Range selection - simplified for now
                _gridSelectedItems.Clear();
                _gridSelectedItems.Add(item);
                ClearAllGridSelections();
                UpdateGridItemSelection(border, true);
            }
            else
            {
                // Single selection
                _gridSelectedItems.Clear();
                _gridSelectedItems.Add(item);
                ClearAllGridSelections();
                UpdateGridItemSelection(border, true);
            }

            _lastClickedGridItem = item;
            SelectionChanged?.Invoke(GetSelectedItemCount());
        }
    }

    private void GridItem_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is BrowserItem item)
        {
            // Select the item if not already selected
            if (!_gridSelectedItems.Contains(item))
            {
                _gridSelectedItems.Clear();
                _gridSelectedItems.Add(item);
                ClearAllGridSelections();
                UpdateGridItemSelection(border, true);
            }
            
            if (item.Name == "..")
            {
                SetMenuVisibility("Cut", false);
                SetMenuVisibility("Copy", false);
                SetMenuVisibility("Paste", false);
                SetMenuVisibility("Rename", false);
                SetMenuVisibility("Separator", false);
                SetMenuVisibility("Upload", false);
                SetMenuVisibility("Download", false);
                SetMenuVisibility("Delete", false);
            }
            else if (item.IsFolder)
            {
                SetMenuVisibility("Cut", true);
                SetMenuVisibility("Copy", true);
                SetMenuVisibility("Paste", true);
                SetMenuVisibility("Rename", true);
                SetMenuVisibility("Separator", false);
                SetMenuVisibility("Upload", true);
                SetMenuVisibility("Download", true);
                SetMenuVisibility("Delete", true);
            }
            else
            {
                SetMenuVisibility("Cut", true);
                SetMenuVisibility("Copy", true);
                SetMenuVisibility("Paste", true);
                SetMenuVisibility("Rename", true);
                SetMenuVisibility("Separator", true);
                SetMenuVisibility("Upload", false);
                SetMenuVisibility("Download", true);
                SetMenuVisibility("Delete", true);
            }

            contextMenu.PlacementTarget = BrowserGrid;
            contextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void UpdateGridItemSelection(Border border, bool isSelected)
    {
        border.Background = isSelected ? 
            System.Windows.Media.Brushes.LightBlue : 
            System.Windows.Media.Brushes.Transparent;
    }

    private void ClearAllGridSelections()
    {
        // Clear visual selection for all grid items
        foreach (var child in GetGridItemBorders())
        {
            child.Background = System.Windows.Media.Brushes.Transparent;
        }
    }

    private IEnumerable<Border> GetGridItemBorders()
    {
        var wrapPanel = FindChild<WrapPanel>(BrowserGrid);
        if (wrapPanel != null)
        {
            foreach (var child in wrapPanel.Children)
            {
                if (child is ContentPresenter presenter && presenter.Content is Border border)
                {
                    yield return border;
                }
            }
        }
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T foundChild)
                return foundChild;

            var result = FindChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
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
            }
            else
            {
                hitTest = VisualTreeHelper.HitTest(GridScrollViewer, e.GetPosition(GridScrollViewer));
                if (hitTest?.VisualHit == GridScrollViewer)
                {
                    emptySpaceContextMenu.PlacementTarget = BrowserGrid;
                    emptySpaceContextMenu.IsOpen = true;
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

    #endregion

    // Event handlers for inline editing
    private void DisplayTextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only allow rename on slow double click or F2, not single click
        // This will be handled by F2 key or context menu
    }

    private void EditTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is BrowserItem item)
        {
            CommitEdit(item);
        }
    }

    private void EditTextBox_KeyDown(object sender, KeyEventArgs e)
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

    private void BrowserList_PreviewKeyDown(object sender, KeyEventArgs e)
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
            Name = "New folder",
            OriginalName = "New folder",
            IsFolder = true,
            IsNewItem = true,
            LastUpdated = DateTime.Now,
            Size = 0,
            FullPath = ""
        };

        System.Diagnostics.Debug.WriteLine($"[BrowserUserControl] Created new folder item with Name: {newFolder.Name}, IsNewItem: {newFolder.IsNewItem}");
        Items.Add(newFolder);
        
        switch (_currentViewMode)
        {
            case BrowserViewMode.List:
                BrowserList.SelectedItem = newFolder;
                BrowserList.ScrollIntoView(newFolder);
                System.Diagnostics.Debug.WriteLine($"[BrowserUserControl] Set selection in ListView");
                break;
            case BrowserViewMode.Grid:
                _gridSelectedItems.Clear();
                _gridSelectedItems.Add(newFolder);
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
                    SetMenuVisibility("Cut", false);
                    SetMenuVisibility("Copy", false);
                    SetMenuVisibility("Paste", false);
                    SetMenuVisibility("Rename", false);
                    SetMenuVisibility("Separator", false);
                    SetMenuVisibility("Upload", false);
                    SetMenuVisibility("Download", false);
                    SetMenuVisibility("Delete", false);
                    e.Handled = true;
                    return;
                }
                
                if (item.IsFolder)
                {
                    SetMenuVisibility("Cut", true);
                    SetMenuVisibility("Copy", true);
                    SetMenuVisibility("Paste", true);
                    SetMenuVisibility("Rename", true);
                    SetMenuVisibility("Separator", false);
                    SetMenuVisibility("Upload", true);
                    SetMenuVisibility("Download", true);
                    SetMenuVisibility("Delete", true);
                }
                else
                {
                    SetMenuVisibility("Cut", true);
                    SetMenuVisibility("Copy", true);
                    SetMenuVisibility("Paste", true);
                    SetMenuVisibility("Rename", true);
                    SetMenuVisibility("Separator", true);
                    SetMenuVisibility("Upload", false);
                    SetMenuVisibility("Download", true);
                    SetMenuVisibility("Delete", true);
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
        AddMenuItem("Cut", (_, __) => RaiseContextEvent(CutRequested), SymbolRegular.Cut24);
        AddMenuItem("Copy", (_, __) => RaiseContextEvent(CopyRequested), SymbolRegular.Copy24);
        AddMenuItem("Paste", (_, __) => RaiseContextEvent(PasteRequested), SymbolRegular.ClipboardPaste24);
        AddMenuItem("Rename", (_, __) => HandleContextMenuRename(), SymbolRegular.Rename20);
        
        var sep1 = new Separator();
        contextMenu.Items.Add(sep1);
        menuItems["Separator"] = new MenuItem { Header = "Separator", Visibility = Visibility.Collapsed };
        sep1.DataContext = menuItems["Separator"];
        
        AddMenuItem("Upload", (_, __) => RaiseContextEvent(UploadRequested), SymbolRegular.ArrowUpload24);
        AddMenuItem("Download", (_, __) => RaiseContextEvent(DownloadRequested), SymbolRegular.ArrowDownload24);
        
        var sep2 = new Separator();
        contextMenu.Items.Add(sep2);
        
        AddMenuItem("Delete", (_, __) => RaiseContextEvent(DeleteRequested), SymbolRegular.Delete24);
    }

    private void SetupEmptySpaceContextMenu()
    {
        emptySpaceContextMenu = new ContextMenu();
        AddEmptySpaceMenuItem("Paste", (_, __) => EmptySpacePasteRequested?.Invoke(), SymbolRegular.ClipboardPaste24);
        AddEmptySpaceMenuItem("Upload", (_, __) => EmptySpaceUploadRequested?.Invoke(), SymbolRegular.ArrowUpload24);
        
        var sep = new Separator();
        emptySpaceContextMenu.Items.Add(sep);
        
        AddEmptySpaceMenuItem("New Folder", (_, __) => EmptySpaceNewFolderRequested?.Invoke(), SymbolRegular.FolderAdd24);
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
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        
        // Clear grid selection when loading new items
        _gridSelectedItems.Clear();
    }

    public void AddItem(BrowserItem item)
    {
        Items.Add(item);
    }

    public void RemoveItem(BrowserItem item)
    {
        Items.Remove(item);
        _gridSelectedItems.Remove(item);
    }

    public void Clear()
    {
        Items.Clear();
        _gridSelectedItems.Clear();
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
        SetEmptySpaceMenuEnabled("Paste", hasClipboardContent);
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
                _gridSelectedItems.Clear();
                _gridSelectedItems.AddRange(Items.Where(item => item.Name != ".."));
                ClearAllGridSelections();
                foreach (var border in GetGridItemBorders())
                {
                    if (border.DataContext is BrowserItem item && item.Name != "..")
                    {
                        UpdateGridItemSelection(border, true);
                    }
                }
                break;
        }
    }

    private void BrowserList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_currentViewMode == BrowserViewMode.List)
        {
            var selectedCount = GetSelectedItemCount();
            SelectionChanged?.Invoke(selectedCount);
        }
    }

    // Sorting functionality
    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader header && header.Column != null)
        {
            string? propertyName = null;
            var headerText = header.Content as string;
            switch (headerText)
            {
                case "Name": propertyName = "Name"; break;
                case "Date Modified": propertyName = "LastUpdated"; break;
                case "Type": propertyName = "DisplayType"; break;
                case "Size": propertyName = "Size"; break;
            }
            if (!string.IsNullOrEmpty(propertyName))
            {
                ApplySort(propertyName);
            }
        }
    }

    private void ApplySort(string propertyName)
    {
        if (_lastSortProperty == propertyName)
        {
            _lastSortDirection = _lastSortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        }
        else
        {
            _lastSortDirection = ListSortDirection.Ascending;
        }
        _lastSortProperty = propertyName;

        IOrderedEnumerable<BrowserItem> sorted;
        switch (propertyName)
        {
            case "Name":
                sorted = _lastSortDirection == ListSortDirection.Ascending
                    ? Items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                    : Items.OrderByDescending(i => i.Name, StringComparer.CurrentCultureIgnoreCase);
                break;
            case "LastUpdated":
                sorted = _lastSortDirection == ListSortDirection.Ascending
                    ? Items.OrderBy(i => i.LastUpdated)
                    : Items.OrderByDescending(i => i.LastUpdated);
                break;
            case "DisplayType":
                sorted = _lastSortDirection == ListSortDirection.Ascending
                    ? Items.OrderBy(i => i.DisplayType)
                    : Items.OrderByDescending(i => i.DisplayType);
                break;
            case "Size":
                sorted = _lastSortDirection == ListSortDirection.Ascending
                    ? Items.OrderBy(i => i.Size)
                    : Items.OrderByDescending(i => i.Size);
                break;
            default:
                return;
        }
        
        var parent = Items.FirstOrDefault(i => i.Name == ".." );
        var sortedList = sorted.ToList();
        if (parent != null)
        {
            sortedList.Remove(parent);
            sortedList.Insert(0, parent);
        }
        Items.Clear();
        foreach (var item in sortedList)
            Items.Add(item);
    }

    private void BrowserList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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
            switch (headerText)
            {
                case "Name": propertyName = "Name"; break;
                case "Date Modified": propertyName = "LastUpdated"; break;
                case "Type": propertyName = "DisplayType"; break;
                case "Size": propertyName = "Size"; break;
            }
            if (!string.IsNullOrEmpty(propertyName))
            {
                ApplySort(propertyName);
                e.Handled = true;
            }
        }
    }
}