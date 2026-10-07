# Shirobot.Plugin.RssSubscriber

当前发布：`v0.2.1`。本版本使用 SDK `0.9.8`，需要宿主 `0.9.8` 的新 ABI；旧宿主不兼容。

Shirobot.Plugin.RssSubscriber 是 ShiroBot 插件生态中的一员，旨在为群聊和私聊提供 RSS / Atom 订阅、更新监测与内容推送，并按适配器实例隔离订阅数据。

## 功能

- 支持 RSS 2.0、Atom 1.0 和 RDF/RSS 1.0。
- 支持自动轮询、失败退避、条件请求、去重和状态持久化。
- 支持通用 feed，以及 WordPress、Bilibili、X/Twitter、GitHub 和 Gitea 内容卡片。
- 支持 `#rss latest`、`#rss test` 和 Markdown 操作按钮。
- 支持群管理员权限控制及私聊个人订阅。

## 支持渲染卡片的平台

| 平台 | 识别方式 | 卡片内容 |
| --- | --- | --- |
| WordPress | RSS generator 标记 | 站点头像、标题、日期、封面、正文和分类 |
| Bilibili | Feed 或条目链接 | 视频、动态和专栏；视频卡片可显示作者头像、时长及播放、点赞、投币、收藏和转发数 |
| X / Twitter | 条目域名或 RSSHub `/twitter/user/` 路径 | 帖子、作者头像、正文和配图 |
| GitHub / Gitea | Feed 或条目域名 | Commit、Pull Request、Issue 和状态 |
| 其他 RSS / Atom | 通用回退 | 站点头像、标题、正文和可选封面 |

卡片用于自动推送、`#rss latest` 和 `#rss test`。平台数据缺失时会隐藏对应内容；渲染不可用时回退为普通消息。

## 运行要求

- ShiroBot 宿主 v0.9.8 或更高版本。
- `shirobot.model.qq` 包版本至少为 `0.9.8`。
- QQ 官方 Markdown 和媒体卡片需要兼容版本的 QQPlatform 适配器。

插件遵循 ShiroBot 的适配器实例和类型化 QQ 消息契约，详见[插件开发文档](https://docs.shiroka.org/plugin/apis)与[API 兼容性说明](https://docs.shiroka.org/plugin/api-compatibility)。

## 构建

使用 NuGet `ShiroBot.SDK 0.9.8`（包含 SDK、QQ Model 和 Avalonia 渲染契约），无需引用宿主源码：

```powershell
dotnet restore .\tests\Shirobot.Plugin.RssSubscriber.Tests\Shirobot.Plugin.RssSubscriber.Tests.csproj
dotnet build .\Shirobot.Plugin.RssSubscriber\Shirobot.Plugin.RssSubscriber.csproj -c Release --no-restore
dotnet test .\tests\Shirobot.Plugin.RssSubscriber.Tests\Shirobot.Plugin.RssSubscriber.Tests.csproj -c Release --no-restore
```

新版本尚未公开发布时，将本地 `.nupkg` 所在目录添加为 NuGet 源，或向 restore 传入本地源配置 `--configfile <NuGet.config>`。本轮 SDK / QQ Model ABI 均为 1.0.0.0，需配合新宿主与重新构建的适配器。

## 部署

将构建输出中的 `Shirobot.Plugin.RssSubscriber.dll` 和 `config.toml` 放入宿主的：

```text
plugins/Shirobot.Plugin.RssSubscriber/
```

SDK 和 `ShiroBot.Model.QQ` 由宿主提供。升级前的订阅文件可以继续读取；同一平台有多个适配器实例时，旧订阅需通过目标实例执行 `#rss add <已有 feed_id>`，以绑定到指定实例。

## 命令

| 命令 | 用途 |
| --- | --- |
| `#rss add <url> [id]` | 添加并订阅 feed |
| `#rss add <feed_id>` | 订阅已有 feed |
| `#rss list` / `#rss remove <feed_id>` | 查看或取消当前会话订阅 |
| `#rss latest <feed_id> [tag] [n=N]` | 查看最新条目 |
| `#rss test <feed_id>` | 测试推送 |
| `#rss config [key] [value]` | 查看或修改配置 |
| `#rss feeds` / `#rss rename` / `#rss interval` / `#rss reload` | 管理 feed（Bot 主人） |

群聊写入操作由群主、群管理员或 Bot 主人执行；私聊用户只能管理自己的订阅。`#rss latest` 和 `#rss test` 仅访问当前会话已订阅的 feed。

## 配置与数据

默认配置来自 `Shirobot.Plugin.RssSubscriber/Assets/config.toml`，运行时配置及状态保存在插件目录。可通过 `#rss config` 查看配置；常用选项包括轮询间隔、图片和卡片开关、Markdown、按钮以及 `allow_private_urls`。

建议保持 `allow_private_urls = false`。插件限制订阅协议为 HTTP/HTTPS，并在抓取和重定向时检查目标地址，以降低 SSRF 风险。订阅关系保存在 `subscriptions.json`，抓取基线及投递状态保存在 `state.json`。

## License

MIT，详见 [LICENSE](LICENSE)。
