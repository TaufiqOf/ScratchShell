using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using ListViewItem = Wpf.Ui.Controls.ListViewItem;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;

namespace ScratchShell.UserControls.BrowserControl;

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

    private ContextMenu contextMenu;
    private ContextMenu emptySpaceContextMenu;
    private readonly Dictionary<string, MenuItem> menuItems = new();
    private readonly Dictionary<string, MenuItem> emptySpaceMenuItems = new();
    private BrowserItem? currentlyEditingItem = null;
    private bool _isIsBrowserEnabled = false;
    /// <summary>
    /// Gets or sets whether the browser control is enabled for user interaction
    /// </summary>
    public bool IsBrowserEnabled
    {
        get => _isIsBrowserEnabled;
        set => _isIsBrowserEnabled = value;
    }

    public BrowserUserControl()
    {
        InitializeComponent();
        BrowserList.ItemsSource = Items;
        BrowserList.MouseDoubleClick += BrowserListMouseDoubleClick;
        BrowserList.PreviewMouseDown += BrowserListPreviewMouseDown;
        BrowserList.SelectionChanged += BrowserList_SelectionChanged;
        SetupContextMenu();
        SetupEmptySpaceContextMenu();
    }

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
                if (BrowserList.SelectedItem is BrowserItem item && item.Name != "..")
                {
                    StartInlineRename(item);
                    e.Handled = true;
                }
                break;
            case Key.A:
                // Ctrl+A - Select all items
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    SelectAllValidItems();
                    e.Handled = true;
                }
                break;
            case Key.Delete:
                // Delete key - Delete selected items
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
                NewFolderCreated?.Invoke(item);
            }
            else
            {
                var oldName = item.OriginalName;
                if (oldName != newName)
                {
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
            // Reset to original name if validation fails
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
            // Remove the new item
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

        // Check for invalid characters
        char[] invalidChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
        if (name.IndexOfAny(invalidChars) >= 0)
            return false;

        // Check for reserved names
        string[] reservedNames = { ".", "..", "CON", "PRN", "AUX", "NUL" };
        if (reservedNames.Any(reserved => 
            string.Equals(reserved, name, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    public void StartNewFolderCreation()
    {
        if (currentlyEditingItem != null)
        {
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
            FullPath = "" // Will be set when committing
        };

        Items.Add(newFolder);
        BrowserList.SelectedItem = newFolder;
        BrowserList.ScrollIntoView(newFolder);

        // Start editing immediately
        currentlyEditingItem = newFolder;
        newFolder.StartEdit();
    }

    private void BrowserListPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Commit any pending edits when clicking elsewhere
        if (currentlyEditingItem != null && e.ChangedButton == MouseButton.Left)
        {
            // Check if we're clicking on the edit textbox
            var depObj = (DependencyObject)e.OriginalSource;
            while (depObj != null && depObj is not TextBox)
            {
                depObj = VisualTreeHelper.GetParent(depObj);
            }

            // If we're not clicking on a textbox, commit the edit
            if (depObj is not TextBox)
            {
                CommitEdit(currentlyEditingItem);
            }
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            // Find the container that was clicked (ListViewItem / TreeViewItem / etc.)
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
                // Now you have the actual BrowserItem
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
        
        // Add separator
        var sep = new Separator();
        emptySpaceContextMenu.Items.Add(sep);
        
        AddEmptySpaceMenuItem("New Folder", (_, __) => EmptySpaceNewFolderRequested?.Invoke(), SymbolRegular.FolderAdd24);
    }

    private void HandleContextMenuRename()
    {
        if (BrowserList.SelectedItem is BrowserItem item && item.Name != "..")
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
            // For multi-select, handle copy/cut/delete operations
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
        
        // Single item or unsupported multi-select operation
        if (BrowserList.SelectedItem is BrowserItem item)
            action?.Invoke(item);
    }

    private void HandleMultiCopyEvent(List<BrowserItem> items)
    {
        // Raise an event for multi-copy operation
        MultiCopyRequested?.Invoke(items);
    }

    private void HandleMultiCutEvent(List<BrowserItem> items)
    {
        // Raise an event for multi-cut operation
        MultiCutRequested?.Invoke(items);
    }

    private void HandleMultiDeleteEvent(List<BrowserItem> items)
    {
        // Raise an event for multi-delete operation
        MultiDeleteRequested?.Invoke(items);
    }

    private void BrowserList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            // Find the container that was clicked (ListViewItem / TreeViewItem / etc.)
            var depObj = (DependencyObject)e.OriginalSource;
            while (depObj != null && depObj is not ListViewItem)
            {
                depObj = VisualTreeHelper.GetParent(depObj);
            }

            if (depObj is ListViewItem listViewItem && listViewItem.DataContext is BrowserItem item)
            {
                // Right-clicked on an item
                if (item.Name != "..")
                {
                    contextMenu.PlacementTarget = BrowserList;
                    contextMenu.IsOpen = true;
                }
            }
            else
            {
                // Right-clicked on empty space
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
    }

    public void AddItem(BrowserItem item)
    {
        Items.Add(item);
    }

    public void RemoveItem(BrowserItem item)
    {
        Items.Remove(item);
    }

    public void Clear()
    {
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
        SetEmptySpaceMenuEnabled("Paste", hasClipboardContent);
    }

    /// <summary>
    /// Gets the currently selected items in the browser
    /// </summary>
    /// <returns>List of selected BrowserItem objects</returns>
    public List<BrowserItem> GetSelectedItems()
    {
        var selectedItems = new List<BrowserItem>();
        
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
        BrowserList.SelectAll();
        
        // Deselect the parent directory (..) if it's selected
        var parentItem = Items.FirstOrDefault(item => item.Name == "..");
        if (parentItem != null && BrowserList.SelectedItems.Contains(parentItem))
        {
            BrowserList.SelectedItems.Remove(parentItem);
        }
    }

    private void BrowserList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedCount = GetSelectedItemCount();
        SelectionChanged?.Invoke(selectedCount);
    }
}