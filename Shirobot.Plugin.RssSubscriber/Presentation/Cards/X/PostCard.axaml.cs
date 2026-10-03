using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Shirobot.Plugin.RssSubscriber.Presentation.Cards.X;

public sealed partial class PostCard : UserControl
{
    public PostCard() => AvaloniaXamlLoader.Load(this);
}
