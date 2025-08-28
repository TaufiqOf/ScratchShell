using ScratchShell.UserControls.BrowserControl;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using ScratchShell.Resources;

namespace ScratchShell.Services;

/// <summary>
/// Manages context menus for browser operations with unified handling and localization support
/// </summary>
public class BrowserContextMenuManager
{
    private readonly ContextMenu _itemContextMenu;
    private readonly ContextMenu _emptySpaceContextMenu;
    private readonly Dictionary<BrowserContextMenuAction, MenuItem> _itemMenuItems = new();
    private readonly Dictionary<BrowserContextMenuAction, MenuItem> _emptySpaceMenuItems = new();

    public event Action<BrowserContextMenuAction, object?>? ActionRequested;

    public BrowserContextMenuManager()
    {
        _itemContextMenu = CreateItemContextMenu();
        _emptySpaceContextMenu = CreateEmptySpaceContextMenu();
    }

    private ContextMenu CreateItemContextMenu()
    {
        var menu = new ContextMenu();

        AddItemMenuItem(BrowserContextMenuAction.Cut, SymbolRegular.Cut24);
        AddItemMenuItem(BrowserContextMenuAction.Copy, SymbolRegular.Copy24);
        AddItemMenuItem(BrowserContextMenuAction.Paste, SymbolRegular.ClipboardPaste24);
        AddItemMenuItem(BrowserContextMenuAction.Rename, SymbolRegular.Rename20);

        menu.Items.Add(new Separator());

        AddItemMenuItem(BrowserContextMenuAction.Upload, SymbolRegular.ArrowUpload24);
        AddItemMenuItem(BrowserContextMenuAction.Download, SymbolRegular.ArrowDownload24);

        return menu;
    }

    private ContextMenu CreateEmptySpaceContextMenu()
    {
        var menu = new ContextMenu();

        AddEmptySpaceMenuItem(BrowserContextMenuAction.EmptySpacePaste, SymbolRegular.ClipboardPaste24);
        AddEmptySpaceMenuItem(BrowserContextMenuAction.EmptySpaceUpload, SymbolRegular.ArrowUpload24);

        menu.Items.Add(new Separator());

        AddEmptySpaceMenuItem(BrowserContextMenuAction.EmptySpaceNewFolder, SymbolRegular.FolderAdd24);

        return menu;
    }

    private static string GetHeader(BrowserContextMenuAction action) => action switch
    {
        BrowserContextMenuAction.Cut => Langauge.ContextMenu_Cut,
        BrowserContextMenuAction.Copy => Langauge.ContextMenu_Copy,
        BrowserContextMenuAction.Paste => Langauge.ContextMenu_Paste,
        BrowserContextMenuAction.Rename => Langauge.ContextMenu_Rename,
        BrowserContextMenuAction.Upload => Langauge.ContextMenu_Upload,
        BrowserContextMenuAction.Download => Langauge.ContextMenu_Download,
        BrowserContextMenuAction.EmptySpacePaste => Langauge.ContextMenu_Paste,
        BrowserContextMenuAction.EmptySpaceUpload => Langauge.ContextMenu_Upload,
        BrowserContextMenuAction.EmptySpaceNewFolder => Langauge.ContextMenu_NewFolder,
        _ => action.ToString()
    };

    private void AddItemMenuItem(BrowserContextMenuAction action, SymbolRegular icon)
    {
        var menuItem = new MenuItem
        {
            Header = GetHeader(action),
            Icon = new SymbolIcon(icon)
        };

        menuItem.Click += (_, _) => ActionRequested?.Invoke(action, null);
        _itemContextMenu.Items.Add(menuItem);
        _itemMenuItems[action] = menuItem;
    }

    private void AddEmptySpaceMenuItem(BrowserContextMenuAction action, SymbolRegular icon)
    {
        var menuItem = new MenuItem
        {
            Header = GetHeader(action),
            Icon = new SymbolIcon(icon)
        };

        menuItem.Click += (_, _) => ActionRequested?.Invoke(action, null);
        _emptySpaceContextMenu.Items.Add(menuItem);
        _emptySpaceMenuItems[action] = menuItem;
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
            SetItemMenuVisibility(BrowserContextMenuAction.Cut, false);
            SetItemMenuVisibility(BrowserContextMenuAction.Copy, false);
            SetItemMenuVisibility(BrowserContextMenuAction.Paste, false);
            SetItemMenuVisibility(BrowserContextMenuAction.Rename, false);
            SetItemMenuVisibility(BrowserContextMenuAction.Upload, false);
            SetItemMenuVisibility(BrowserContextMenuAction.Download, false);
            return;
        }

        // Standard item menu configuration
        SetItemMenuVisibility(BrowserContextMenuAction.Cut, true);
        SetItemMenuVisibility(BrowserContextMenuAction.Copy, true);
        SetItemMenuVisibility(BrowserContextMenuAction.Paste, true);
        SetItemMenuVisibility(BrowserContextMenuAction.Rename, true);
        SetItemMenuVisibility(BrowserContextMenuAction.Download, true);

        // Upload only available for folders
        SetItemMenuVisibility(BrowserContextMenuAction.Upload, item.IsFolder);
    }

    public void SetItemMenuVisibility(BrowserContextMenuAction action, bool visible)
    {
        if (_itemMenuItems.TryGetValue(action, out var item))
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetItemMenuEnabled(BrowserContextMenuAction action, bool enabled)
    {
        if (_itemMenuItems.TryGetValue(action, out var item))
            item.IsEnabled = enabled;
    }

    public void SetEmptySpaceMenuVisibility(BrowserContextMenuAction action, bool visible)
    {
        if (_emptySpaceMenuItems.TryGetValue(action, out var item))
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetEmptySpaceMenuEnabled(BrowserContextMenuAction action, bool enabled)
    {
        if (_emptySpaceMenuItems.TryGetValue(action, out var item))
            item.IsEnabled = enabled;
    }

    public void UpdatePasteAvailability(bool hasClipboardContent)
    {
        SetItemMenuEnabled(BrowserContextMenuAction.Paste, hasClipboardContent);
        SetEmptySpaceMenuEnabled(BrowserContextMenuAction.EmptySpacePaste, hasClipboardContent);
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