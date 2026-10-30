using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace GlassBoard;

public sealed class MenuTheme
{
	private static readonly Dictionary<bool, MenuTheme> themes = new Dictionary<bool, MenuTheme>();

	private readonly Style menu;

	private readonly Style item;

	private readonly Style separator;

	private MenuTheme(bool light)
	{
		string background = (light ? "#FAFCFF" : "#202B3D");
		string ink = (light ? "#202635" : "#F4F6FC");
		string line = (light ? "#CFD7E5" : "#48566D");
		string hover = (light ? "#DFEAFE" : "#3A5074");
		Func<string, string> func = (string x) => x.Replace("@BG", background).Replace("@INK", ink).Replace("@LINE", line)
			.Replace("@HOVER", hover);
		string arg = "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='MenuItem'>\n<Grid>\n <Border x:Name='Chrome' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{TemplateBinding Background}' CornerRadius='6' Padding='{TemplateBinding Padding}'>\n  <Grid>\n   <Grid.ColumnDefinitions><ColumnDefinition Width='22'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='16'/></Grid.ColumnDefinitions>\n   <ContentPresenter x:Name='Icon' ContentSource='Icon' VerticalAlignment='Center' HorizontalAlignment='Center'/>\n   <TextBlock x:Name='Check' Text='✓' Foreground='{TemplateBinding Foreground}' Visibility='Collapsed' VerticalAlignment='Center'/>\n   <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center' HorizontalAlignment='Left'/>\n   <TextBlock Grid.Column='2' Text='{TemplateBinding InputGestureText}' Foreground='{TemplateBinding Foreground}' Opacity='.7' Margin='18,0,8,0' VerticalAlignment='Center'/>\n   <Path x:Name='Arrow' Grid.Column='3' Data='M 0,0 L 4,4 L 0,8' Stroke='{TemplateBinding Foreground}' StrokeThickness='1.6' Visibility='Collapsed' VerticalAlignment='Center' HorizontalAlignment='Right'/>\n  </Grid>\n </Border>\n <Popup x:Name='PART_Popup' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Placement='Right' HorizontalOffset='-1' AllowsTransparency='True' Focusable='False' IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>\n  <Border x:Name='SubmenuSurface' Background='@BG' BorderBrush='@LINE' BorderThickness='1' CornerRadius='10' Padding='5' TextElement.Foreground='@INK' SnapsToDevicePixels='True'>\n   <ScrollViewer CanContentScroll='True' MaxHeight='420' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>\n    <ItemsPresenter Grid.IsSharedSizeScope='True'/>\n   </ScrollViewer>\n  </Border>\n </Popup>\n</Grid>\n<ControlTemplate.Triggers>\n <Trigger Property='HasItems' Value='True'><Setter TargetName='Arrow' Property='Visibility' Value='Visible'/></Trigger>\n <Trigger Property='IsChecked' Value='True'><Setter TargetName='Check' Property='Visibility' Value='Visible'/><Setter TargetName='Icon' Property='Visibility' Value='Collapsed'/></Trigger>\n <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Chrome' Property='Background' Value='@HOVER'/></Trigger>\n <Trigger Property='IsSubmenuOpen' Value='True'><Setter TargetName='Chrome' Property='Background' Value='@HOVER'/></Trigger>\n <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Chrome' Property='Opacity' Value='.45'/></Trigger>\n</ControlTemplate.Triggers>\n</ControlTemplate>";
		item = new Style(typeof(MenuItem));
		item.Setters.Add(new Setter(Control.ForegroundProperty, Polish.Ink(light)));
		item.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
		item.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8.0, 9.0, 8.0, 9.0)));
		item.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(func(arg))));
		separator = new Style(typeof(Separator));
		separator.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(func("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Separator'><Border Height='1' Background='@LINE' Margin='8,5'/></ControlTemplate>"))));
		menu = new Style(typeof(ContextMenu));
		menu.Setters.Add(new Setter(Control.ForegroundProperty, Polish.Ink(light)));
		menu.Setters.Add(new Setter(Control.BackgroundProperty, Polish.Color(background)));
		menu.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
		menu.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(func("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ContextMenu'><Border Background='{TemplateBinding Background}' BorderBrush='@LINE' BorderThickness='1' CornerRadius='10' Padding='5' SnapsToDevicePixels='True'><ScrollViewer CanContentScroll='True' MaxHeight='420' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'><ItemsPresenter Grid.IsSharedSizeScope='True'/></ScrollViewer></Border></ControlTemplate>"))));
		item.Seal();
		separator.Seal();
		menu.Seal();
	}

	private static MenuTheme For(bool light)
	{
		if (!themes.TryGetValue(light, out var value))
		{
			value = new MenuTheme(light);
			themes[light] = value;
		}
		return value;
	}

	public static void Install(Window window, bool light)
	{
		MenuTheme menuTheme = For(light);
		window.Resources[typeof(ContextMenu)] = menuTheme.menu;
		window.Resources[typeof(MenuItem)] = menuTheme.item;
		window.Resources[typeof(Separator)] = menuTheme.separator;
	}

	public static ContextMenu Create(bool light)
	{
		MenuTheme menuTheme = For(light);
		return new ContextMenu
		{
			Style = menuTheme.menu,
			Resources = 
			{
				[(object)typeof(MenuItem)] = menuTheme.item,
				[(object)typeof(Separator)] = menuTheme.separator
			}
		};
	}
}
