using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

internal sealed class InstanceTiles : Grid
{
    private const double Gap = 12;
    private const double MinimumTileWidth = 238;
    private readonly ListBox _list;
    private readonly List<InstanceInfo[]> _groups = new();
    private IReadOnlyList<InstanceInfo> _items = Array.Empty<InstanceInfo>();
    private Func<InstanceInfo, FrameworkElement>? _card;
    private bool _covers = true;
    public int Columns { get; private set; } = 1;

    public InstanceTiles()
    {
        _list = Ui.VirtualList<InstanceInfo[]>(BuildGroup, Gap);
        VirtualizingPanel.SetCacheLength(_list, new VirtualizationCacheLength(0.5));
        VirtualizingPanel.SetCacheLengthUnit(_list, VirtualizationCacheLengthUnit.Page);
        Children.Add(_list);
        SizeChanged += (_, _) => ResizeColumns();
    }

    public bool Covers
    {
        get => _covers;
        set
        {
            if (_covers == value) return;
            _covers = value;
            ResizeColumns();
        }
    }

    public void Bind(IReadOnlyList<InstanceInfo> items, Func<InstanceInfo, FrameworkElement> card)
    {
        _items = items;
        _card = card;
        Regroup();
    }

    public void ScrollToIndex(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        _list.ScrollIntoView(_groups[index / Columns]);
        _list.UpdateLayout();
    }

    private void ResizeColumns()
    {
        int columns = Covers ? Math.Max(1, (int)((ActualWidth + Gap) / (MinimumTileWidth + Gap))) : 1;
        if (Columns == columns) return;
        Columns = columns;
        Regroup();
    }

    private void Regroup()
    {
        // Groups retain records; the virtualizing list owns only the visible row controls.
        _list.ItemsSource = null;
        _groups.Clear();
        for (int start = 0; start < _items.Count; start += Columns)
        {
            var group = new InstanceInfo[Math.Min(Columns, _items.Count - start)];
            for (int column = 0; column < group.Length; column++) group[column] = _items[start + column];
            _groups.Add(group);
        }
        _list.ItemsSource = _groups;
    }

    private UIElement BuildGroup(InstanceInfo[] instances)
    {
        var row = new Grid();
        for (int column = 0; column < Columns; column++) row.ColumnDefinitions.Add(new ColumnDefinition());
        for (int column = 0; column < instances.Length; column++)
        {
            FrameworkElement card = _card!(instances[column]);
            card.Margin = new Thickness(column == 0 ? 0 : Gap / 2, 0, column == Columns - 1 ? 0 : Gap / 2, 0);
            Grid.SetColumn(card, column);
            row.Children.Add(card);
        }
        return row;
    }
}
