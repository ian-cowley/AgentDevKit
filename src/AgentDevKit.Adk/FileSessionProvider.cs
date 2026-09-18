namespace Glacier.AgentDevKit.Adk;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Pure C# file-based session provider persisting message histories to JSON files on disk.
/// Fully cross-platform, zero native C/C++ dependencies.
/// </summary>
public class FileSessionProvider : ISessionProvider
{
    private readonly string _storageDirectory;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public string StorageDirectory => _storageDirectory;

    public FileSessionProvider(string? storageDirectory = null)
    {
        _storageDirectory = storageDirectory ?? Path.Combine(AppContext.BaseDirectory, "agent_sessions");
        Directory.CreateDirectory(_storageDirectory);
    }

    private string GetSessionFilePath(string sessionId)
    {
        var safeId = string.Join("_", sessionId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_storageDirectory, $"{safeId}.json");
    }

    public async Task<List<LlmContent>> GetHistoryAsync(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        string path = GetSessionFilePath(sessionId);

        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return new List<LlmContent>();
            }

            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<LlmContent>();
            }

            var items = JsonSerializer.Deserialize<List<LlmContent>>(json);
            return items ?? new List<LlmContent>();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveMessageAsync(string sessionId, LlmContent message)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(message);

        string path = GetSessionFilePath(sessionId);

        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            List<LlmContent> history;
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                history = JsonSerializer.Deserialize<List<LlmContent>>(json) ?? new List<LlmContent>();
            }
            else
            {
                history = new List<LlmContent>();
            }

            history.Add(message);
            var updatedJson = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, updatedJson).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
