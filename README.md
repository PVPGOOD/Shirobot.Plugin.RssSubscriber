# Shirobot.Plugin.RssSubscriber

`Shirobot.Plugin.RssSubscriber` 是基于 [Shirobot](https://github.com/greepar/shirobot) 的 RSS / Atom 订阅推送插件。

插件按会话作用域隔离订阅：群聊只管理本群订阅，私聊只管理当前用户自己的订阅；后台统一抓取 feed，再按订阅者分发新增条目。

## 功能

- 支持 RSS 2.0、Atom 1.0、RDF/RSS 1.0
- 使用 `#rss add <url> [id]` 添加并订阅 feed，或用 `#rss add <feed_id>` 订阅已有 feed
- feedId 默认从域名派生，例如 `blog.example.com` -> `example`
- 支持 `#rss latest <feed_id> [tag] [n=N]` 即时查看当前作用域已订阅 feed 的最新条目
- 支持从 feed 标题提取展示名，推送时显示为 `[博客名]`
- 支持抓取正文第一张图片，下载后转为 `base64://` 发送，避免平台拉不到本机或内网图片
- 支持群聊权限控制：群主、群管理员、Bot 主人可以写入订阅
- 支持私聊订阅：任何用户都可以管理自己的私聊订阅
- 支持失败退避、请求超时、lastSeen 去重和状态持久化
- 支持 `#rss config` 查看或修改运行配置

## 如何部署开发环境

推荐目录结构：

- `./RiderProjects/Shirobot.Plugin.RssSubscriber`
- `./RiderProjects/ShiroBot`

如果你的本地仓库布局不同，需要修改 `Shirobot.Plugin.RssSubscriber/Shirobot.Plugin.RssSubscriber.csproj` 里的这些属性：

- `HostProjectRoot`
- `HostExe`
- `HostPluginDir`
- `ProjectReference Include="C:\Users\JustMe\RiderProjects\ShiroBot\ShiroBot.SDK\ShiroBot.SDK.csproj"`

其中：

- `HostExe` 需要指向你本机实际使用的 `ShiroBot.exe`
- `HostPluginDir` 需要指向你本机实际使用的插件输出目录，通常是 `ShiroBot.exe` 所在目录下的 `plugins\Shirobot.Plugin.RssSubscriber\`

开发流程：

1. 打开项目 `Shirobot.Plugin.RssSubscriber/Shirobot.Plugin.RssSubscriber.csproj`
2. 修改插件代码或 `Assets/config.toml`
3. 执行 `dotnet build`
4. Debug 构建后插件会自动复制到 `ShiroBot` 的插件目录
5. 启动 `ShiroBot.exe` 进行调试

如果 `ShiroBot.exe` 正在运行，构建后的复制步骤可能会因为 DLL 被占用而失败。这种情况下先停止宿主，再重新构建。

只想验证编译、不复制到宿主目录时，可以使用：

```text
dotnet build Shirobot.Plugin.RssSubscriber\Shirobot.Plugin.RssSubscriber.csproj -c Debug /p:CopyPluginToHost=false
```

## 配置

默认配置文件：

```text
Shirobot.Plugin.RssSubscriber/Assets/config.toml
```

主要配置：

```toml
enabled = true
default_interval_seconds = 10
min_interval_seconds = 5
request_timeout_seconds = 30
max_items_per_push = 3
max_description_length = 200
latest_max_n = 5
include_image = true
allow_private_urls = false
user_agent = "Shirobot-Rss/1.0"
last_seen_capacity = 100
backoff_max_seconds = 3600
```

说明：

- Bot 主人 / 管理员权限直接使用宿主 SDK 的 OwnerList / AdminList
- `default_interval_seconds` 是默认轮询间隔，单条 feed 可用 `#rss interval` 覆盖
- `min_interval_seconds` 是允许设置的最小轮询间隔
- `max_items_per_push` 控制一次轮询最多推送几条新内容，超过部分只记录为已读
- `latest_max_n` 控制 `#rss latest ... n=N` 的最大 N 值
- `include_image` 控制自动推送时是否附带封面图
- `allow_private_urls` 控制是否允许订阅内网、回环和本机地址，公开使用时应保持 `false`
- `last_seen_capacity` 控制每个 feed 记住多少条已推送 GUID
- `backoff_max_seconds` 控制连续失败后的最大退避间隔

运行中可用 `#rss config` 查看配置，Bot 主人可用 `#rss config <key> <value>` 修改部分配置。

## 使用方式

添加并订阅 feed：

```text
#rss add https://example.com/feed
#rss add https://example.com/feed example
```

订阅已有 feed：

```text
#rss add example
```

查看当前作用域订阅：

```text
#rss list
```

退订当前作用域的 feed：

```text
#rss remove example
```

查看最新条目：

```text
#rss latest example
#rss latest example anime n=3
```

测试推送格式：

```text
#rss test example
```

查看或修改配置：

```text
#rss config
#rss config include_image on
#rss config allow_private_urls off
```

Bot 主人管理命令：

```text
#rss feeds
#rss rename example example-news
#rss interval example-news 60
#rss reload
```

## 命令权限

| 命令 | 群聊 | 私聊 |
| --- | --- | --- |
| `#rss` / `#rss status` | 任何人 | 任何人 |
| `#rss help` | 任何人 | 任何人 |
| `#rss list` | 任何人 | 任何人 |
| `#rss add <url> [id]` | 群主 / 群管理员 / Bot 主人 | 当前用户 |
| `#rss add <feed_id>` | 群主 / 群管理员 / Bot 主人 | 当前用户 |
| `#rss remove <feed_id>` | 群主 / 群管理员 / Bot 主人 | 当前用户 |
| `#rss rename <old_id> <new_id>` | Bot 主人 | Bot 主人 |
| `#rss latest <feed_id> [tag] [n=N]` | 任何人，仅限本群已订阅 | 任何人，仅限本人已订阅 |
| `#rss test <feed_id>` | 群主 / 群管理员 / Bot 主人，仅限本群已订阅 | 当前用户，仅限本人已订阅 |
| `#rss config show` | 任何人 | 任何人 |
| `#rss config <key> <value>` | Bot 主人 | Bot 主人 |
| `#rss interval <feed_id> <seconds>` | Bot 主人 | Bot 主人 |
| `#rss reload` | Bot 主人 | Bot 主人 |
| `#rss feeds` | Bot 主人 | Bot 主人 |

群内执行写入类命令时，成功或失败反馈会 @ 操作者。

## 推送格式

推送和 `#rss latest` 使用同一套消息格式：

```text
文章标题
[封面图]
────────
[Feed 展示名]
https://example.com/post
发布时间: [刚刚]
```

发布时间显示规则：

- 5 分钟内显示 `[刚刚]`
- 1 小时内显示 `[N 分钟前]`
- 24 小时内显示 `[N 小时前]`
- 更早内容显示 `yyyy-MM-dd HH:mm`

## 隐私性

- 群订阅和私聊订阅完全隔离，互相不可见
- `#rss latest` 和 `#rss test` 只能访问当前作用域已经订阅的 feed
- `#rss feeds` 只有 Bot 主人可以查看，用于排查全局 feed 池
- `state.json` 只保存 feed URL、展示名、订阅作用域、lastSeen GUID、失败次数和时间戳等运行状态
- 插件不会把聊天消息内容写入 `state.json`
- 私聊用户只能管理自己的私聊订阅，不能枚举其他用户或群的订阅
- 群普通成员可以查看本群订阅列表，但不能修改订阅

## 安全性

- 仅允许订阅 `http` / `https` URL
- `allow_private_urls = false` 时拒绝 `localhost`、loopback、RFC1918 私有网段、CGNAT、link-local、multicast、IPv6 ULA 等地址
- 每次抓取 feed 前都会重新经过 URL 安全检查，降低 DNS 或配置变化造成的 SSRF 风险
- XML 解析使用 `XDocument` + `XmlReaderSettings`，忽略 DTD，不依赖 `System.ServiceModel.Syndication`
- RSS/Atom 正文会剥离 HTML 后再发送文本
- 图片只取正文第一张 `<img>`，并限制下载为 15 秒、最大 8 MB
- 图片下载失败或超过大小限制时回退到原始 URL，不阻塞整条推送
- 连续抓取失败会指数退避，避免对失败源持续高频请求

生产环境建议：

- 保持 `allow_private_urls = false`
- 不要把测试用的 `127.0.0.1`、内网 feed 或 `state.json` 提交到公开仓库
- 不要把不可信账号加入宿主 OwnerList / AdminList
- 公开群使用时建议把 `default_interval_seconds` 设置到合理值，避免对第三方站点造成压力

## 状态文件

插件运行状态保存在宿主插件目录：

```text
plugins/Shirobot.Plugin.RssSubscriber/state.json
```

示例结构：

```json
{
  "version": 1,
  "feeds": {
    "example": {
      "url": "https://example.com/feed",
      "displayName": "Example Blog",
      "intervalSec": null,
      "lastSeen": ["https://example.com/post-1"],
      "lastFetchAt": "2026-06-01T00:00:00+00:00",
      "consecutiveFailures": 0,
      "createdBy": "group:123456",
      "createdAt": "2026-06-01T00:00:00+00:00"
    }
  },
  "groupSubs": { "123456": ["example"] },
  "friendSubs": { "10001": ["example"] }
}
```

`state.json` 由插件自动维护，通常不需要手动编辑。

## 运行方式

运行本项目需要使用 [Shirobot](https://github.com/greepar/shirobot)。

下载 `Shirobot` 后，将本项目构建产物放到其 `plugins/Shirobot.Plugin.RssSubscriber/` 目录下即可加载。

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.
