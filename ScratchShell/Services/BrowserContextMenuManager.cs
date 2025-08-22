using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using ScratchShell.UserControls.BrowserControl;
using Wpf.Ui.Controls;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace ScratchShell.Services;

/// <summary>
/// Manages context menus for browser operations with unified handling
/// </summary>
public class BrowserContextMenuManager
{
    private readonly ContextMenu _itemContextMenu;
    private readonly ContextMenu _emptySpaceContextMenu;
    private readonly Dictionary<string, MenuItem> _itemMenuItems = new();
    private readonly Dictionary<string, MenuItem> _emptySpaceMenuItems = new();

    public event Action<BrowserContextMenuAction, object?>? ActionRequested;

    public BrowserContextMenuManager()
    {
        _itemContextMenu = CreateItemContextMenu();
        _emptySpaceContextMenu = CreateEmptySpaceContextMenu();
    }

    private ContextMenu CreateItemContextMenu()
    {
        var menu = new ContextMenu();
        
        AddItemMenuItem("Cut", BrowserContextMenuAction.Cut, SymbolRegular.Cut24);
        AddItemMenuItem("Copy", BrowserContextMenuAction.Copy, SymbolRegular.Copy24);
        AddItemMenuItem("Paste", BrowserContextMenuAction.Paste, SymbolRegular.ClipboardPaste24);
        AddItemMenuItem("Rename", BrowserContextMenuAction.Rename, SymbolRegular.Rename20);
        
        menu.Items.Add(new Separator());
        
        AddItemMenuItem("Upload", BrowserContextMenuAction.Upload, SymbolRegular.ArrowUpload24);
        AddItemMenuItem("Download", BrowserContextMenuAction.Download, SymbolRegular.ArrowDownload24);
        
        return menu;
    }

    private ContextMenu CreateEmptySpaceContextMenu()
    {
        var menu = new ContextMenu();
        
        AddEmptySpaceMenuItem("Paste", BrowserContextMenuAction.EmptySpacePaste, SymbolRegular.ClipboardPaste24);
        AddEmptySpaceMenuItem("Upload", BrowserContextMenuAction.EmptySpaceUpload, SymbolRegular.ArrowUpload24);
        
        menu.Items.Add(new Separator());
        
        AddEmptySpaceMenuItem("New Folder", BrowserContextMenuAction.EmptySpaceNewFolder, SymbolRegular.FolderAdd24);
        
        return menu;
    }

    private void AddItemMenuItem(string header, BrowserContextMenuAction action, SymbolRegular icon)
    {
        var menuItem = new MenuItem
        {
            Header = header,
            Icon = new SymbolIcon(icon)
        };
        
        menuItem.Click += (_, _) => ActionRequested?.Invoke(action, null);
        _itemContextMenu.Items.Add(menuItem);
        _itemMenuItems[header] = menuItem;
    }

    private void AddEmptySpaceMenuItem(string header, BrowserContextMenuAction action, SymbolRegular icon)
    {
        var menuItem = new MenuItem
        {
            Header = header,
            Icon = new SymbolIcon(icon)
        };
        
        menuItem.Click += (_, _) => ActionRequested?.Invoke(action, null);
        _emptySpaceContextMenu.Items.Add(menuItem);
        _emptySpaceMenuItems[header] = menuItem;
    }

    public void ShowItemContextMenu(FrameworkElement target, BrowserItem item)
    {
        ConfigureItemMenuForItem(item);
        _itemContextMenu.PlacementTarget = target;
        _itemContextMenu.DataContext = item;
        _itemContextMenu.IsOpen = true;
    }

    public void ShowEmptySpaceContextMenu(FrameworkElement target)
    {
        _emptySpaceContextMenu.PlacementTarget = target;
        _emptySpaceContextMenu.IsOpen = true;
    }

    private void ConfigureItemMenuForItem(BrowserItem item)
    {
        // Configure menu based on item type and state
        if (item.Name == "..")
        {
            SetItemMenuVisibility("Cut", false);
            SetItemMenuVisibility("Copy", false);
            SetItemMenuVisibility("Paste", false);
            SetItemMenuVisibility("Rename", false);
            SetItemMenuVisibility("Upload", false);
            SetItemMenuVisibility("Download", false);
            return;
        }

        // Standard item menu configuration
        SetItemMenuVisibility("Cut", true);
        SetItemMenuVisibility("Copy", true);
        SetItemMenuVisibility("Paste", true);
        SetItemMenuVisibility("Rename", true);
        SetItemMenuVisibility("Download", true);
        
        // Upload only available for folders
        SetItemMenuVisibility("Upload", item.IsFolder);
    }

    public void SetItemMenuVisibility(string menuHeader, bool visible)
    {
        if (_itemMenuItems.TryGetValue(menuHeader, out var item))
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetItemMenuEnabled(string menuHeader, bool enabled)
    {
        if (_itemMenuItems.TryGetValue(menuHeader, out var item))
            item.IsEnabled = enabled;
    }

    public void SetEmptySpaceMenuVisibility(string menuHeader, bool visible)
    {
        if (_emptySpaceMenuItems.TryGetValue(menuHeader, out var item))
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetEmptySpaceMenuEnabled(string menuHeader, bool enabled)
    {
        if (_emptySpaceMenuItems.TryGetValue(menuHeader, out var item))
            item.IsEnabled = enabled;
    }

    public void UpdatePasteAvailability(bool hasClipboardContent)
    {
        SetItemMenuEnabled("Paste", hasClipboardContent);
        SetEmptySpaceMenuEnabled("Paste", hasClipboardContent);
    }
}

public enum BrowserContextMenuAction
{
    Cut,
    Copy,
    Paste,
    Rename,
    Upload,
    Download,
    EmptySpacePaste,
    EmptySpaceUpload,
    EmptySpaceNewFolder
}