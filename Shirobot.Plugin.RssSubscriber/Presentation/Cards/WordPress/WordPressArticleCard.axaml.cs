using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Shirobot.Plugin.RssSubscriber.Presentation.Cards.WordPress;

public sealed partial class WordPressArticleCard : UserControl
{
    public WordPressArticleCard() => AvaloniaXamlLoader.Load(this);
}
