using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Shirobot.Plugin.RssSubscriber.Presentation.Cards.Generic;

public sealed partial class ArticleCard : UserControl
{
    public ArticleCard() => AvaloniaXamlLoader.Load(this);
}
