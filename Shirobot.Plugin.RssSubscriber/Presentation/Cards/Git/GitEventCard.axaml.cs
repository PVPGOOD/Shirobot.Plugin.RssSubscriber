using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Shirobot.Plugin.RssSubscriber.Presentation.Cards.Git;

public sealed partial class GitEventCard : UserControl
{
    public GitEventCard() => AvaloniaXamlLoader.Load(this);
}
