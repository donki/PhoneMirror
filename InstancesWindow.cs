using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhoneMirror.Localization;
using PhoneMirror.Services;

namespace PhoneMirror;

/// <summary>
/// Al arrancar con otra Phone Mirror ya abierta: la lista de las que hay (una por movil, a la vista o
/// en la bandeja) para enseñar una, o abrir una instancia nueva.
/// </summary>
public sealed class InstancesWindow : Window
{
    private readonly ListBox _list;

    /// <summary>La instancia elegida para enseñar, o null si se quiere una nueva.</summary>
    public Instances.Instance? Chosen { get; private set; }

    public InstancesWindow(IReadOnlyList<Instances.Instance> instances)
    {
        Title = Loc.Get("AppTitle");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (System.Windows.Media.Brush)FindResource("PageBackground");
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);
        Topmost = true;

        var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(18, 14, 18, 16) };
        var stack = new StackPanel();
        card.Child = stack;
        stack.Children.Add(new TextBlock { Text = Loc.Get("InstancesIntro"), Style = (Style)FindResource("BodyText"), TextWrapping = TextWrapping.Wrap });

        _list = new ListBox
        {
            Margin = new Thickness(0, 12, 0, 0),
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
            MaxHeight = 220,
        };
        foreach (var instance in instances)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 4, 4, 4) };
            row.Children.Add(new TextBlock { Text = "", FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = instance.Title, VerticalAlignment = VerticalAlignment.Center });
            _list.Items.Add(new ListBoxItem { Content = row, Tag = instance });
        }
        _list.SelectedIndex = 0;
        _list.MouseDoubleClick += (_, _) => Pick();
        stack.Children.Add(_list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var open = new Button { Style = (Style)FindResource("IconButton"), Content = "", ToolTip = Loc.Get("InstancesOpen"), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        open.Click += (_, _) => Pick();
        var fresh = new Button { Style = (Style)FindResource("OutlineButton"), Content = Loc.Get("InstancesNew"), Margin = new Thickness(0, 0, 8, 0) };
        fresh.Click += (_, _) => { Chosen = null; DialogResult = true; };
        var cancel = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("Cancel"), IsCancel = true };
        buttons.Children.Add(fresh);
        buttons.Children.Add(cancel);
        buttons.Children.Add(open);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(card);
        root.Children.Add(buttons);
        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
        Loaded += (_, _) => _list.Focus();
    }

    private void Pick()
    {
        if (_list.SelectedItem is ListBoxItem { Tag: Instances.Instance instance })
        {
            Chosen = instance;
            DialogResult = true;
        }
    }
}
