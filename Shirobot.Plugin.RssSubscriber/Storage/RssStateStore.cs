using System.Text.Json;
using ShiroBot.SDK.Abstractions;

namespace Shirobot.Plugin.RssSubscriber.Storage;

public sealed class RssStateStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public RssStateStore(string pluginConfigDirectory)
    {
        _path = System.IO.Path.Combine(pluginConfigDirectory, "state.json");
    }

    public string FilePath => _path;

    public RssState Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new RssState();
            }

            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new RssState();
            }

            var state = JsonSerializer.Deserialize<RssState>(json);
            return state ?? new RssState();
        }
        catch (Exception ex)
        {
            BotLog.Warning($"[Rss] 加载 state.json 失败，使用空状态: {ex.GetType().Name}: {ex.Message}");
            return new RssState();
        }
    }

    public async Task SaveAsync(RssState state, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _path + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, state, WriteOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            BotLog.Error($"[Rss] 保存 state.json 失败: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void SaveSync(RssState state)
    {
        _writeLock.Wait();
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _path + ".tmp";
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, state, WriteOptions);
                stream.Flush();
            }

            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            BotLog.Error($"[Rss] 同步保存 state.json 失败: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
