using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Shirobot.Plugin.RssSubscriber.Presentation.Cards.Bilibili;

public sealed partial class DynamicCard : UserControl
{
    public DynamicCard() => AvaloniaXamlLoader.Load(this);
}
