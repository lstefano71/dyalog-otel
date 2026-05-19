using System.Text;

namespace Dyalog.OTel.Destinations;

/// <summary>
/// Shared file-writing component with optional cross-process cooperative locking.
/// Used by both JsonlFileDestination and TextDestination.
/// </summary>
internal sealed class CooperativeFileWriter : IDisposable
{
    private readonly string _basePath;
    private readonly RotationPeriod _rotation;
    private readonly SharingMode _sharing;
    private readonly FlushMode _flush;
    private readonly LockStyle _lockStyle;
    private readonly int _lockTimeoutMs;

    private readonly object _localLock = new();
    private StreamWriter? _writer;
    private FileStream? _dataStream;
    private FileStream? _lockFileStream;
    private string _currentPeriodKey = "";
    private long _droppedBatches;

    private const int LockRetryDelayMs = 50;
    // Sentinel offset for inline locking — well beyond any realistic file size,
    // within safe 32-bit-friendly range for broad NAS/SMB compatibility.
    private const long InlineLockOffset = 0x7FFFFFFF;

    public long DroppedBatches => Interlocked.Read(ref _droppedBatches);

    public CooperativeFileWriter(
        string basePath,
        RotationPeriod rotation,
        SharingMode sharing = SharingMode.Cooperative,
        FlushMode flush = FlushMode.Batch,
        LockStyle lockStyle = LockStyle.Inline,
        int lockTimeoutMs = 5000)
    {
        _basePath = basePath;
        _rotation = rotation;
        _sharing = sharing;
        // Cooperative mode always flushes per-batch (required for correctness)
        _flush = sharing == SharingMode.Cooperative ? FlushMode.Batch : flush;
        _lockStyle = lockStyle;
        _lockTimeoutMs = lockTimeoutMs;
    }

    /// <summary>
    /// Open the file (and lock file if cooperative + sidecar). Call during destination Init().
    /// </summary>
    public void Open()
    {
        lock (_localLock)
        {
            if (_sharing == SharingMode.Cooperative && _lockStyle == LockStyle.Sidecar)
                EnsureLockFile();
            EnsureWriter();
        }
    }

    /// <summary>
    /// Write content to the file. Returns true if successful, false if lock timed out (batch dropped).
    /// </summary>
    public bool Write(StringBuilder content)
    {
        if (content.Length == 0)
            return true;

        lock (_localLock)
        {
            if (_sharing == SharingMode.Cooperative)
            {
                if (!AcquireCooperativeLock())
                {
                    Interlocked.Increment(ref _droppedBatches);
                    return false;
                }

                try
                {
                    EnsureWriter();
                    _writer!.Write(content);
                    _writer.Flush();
                    _dataStream!.Flush(flushToDisk: true);
                }
                finally
                {
                    ReleaseCooperativeLock();
                }
            }
            else
            {
                EnsureWriter();
                _writer!.Write(content);
                if (_flush == FlushMode.Batch)
                    _writer.Flush();
            }
        }

        return true;
    }

    /// <summary>
    /// Flush buffered data. Only meaningful in exclusive + shutdown flush mode.
    /// </summary>
    public void Flush()
    {
        lock (_localLock)
            _writer?.Flush();
    }

    /// <summary>
    /// Flush and close all handles.
    /// </summary>
    public void Shutdown()
    {
        lock (_localLock)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
            _dataStream = null; // Owned by StreamWriter, disposed with it
            _lockFileStream?.Dispose();
            _lockFileStream = null;
            _currentPeriodKey = "";
        }
    }

    public void Dispose() => Shutdown();

    private void EnsureWriter()
    {
        string periodKey = GetPeriodKey(DateTime.UtcNow);
        if (periodKey == _currentPeriodKey && _writer != null)
            return;

        // Rotate: close old writer, open new
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
        _dataStream = null;

        _currentPeriodKey = periodKey;
        string filePath = BuildRotatedPath(periodKey);

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var fileShare = _sharing == SharingMode.Cooperative
            ? FileShare.ReadWrite
            : FileShare.Read;

        _dataStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, fileShare);
        _writer = new StreamWriter(_dataStream) { AutoFlush = false };
    }

    private void EnsureLockFile()
    {
        if (_lockFileStream != null)
            return;

        string lockPath = _basePath + ".lock";
        string? dir = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _lockFileStream = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite);
    }

    private bool AcquireCooperativeLock()
    {
        var lockStream = GetLockTarget();
        long offset = _lockStyle == LockStyle.Inline ? InlineLockOffset : 0;
        var deadline = Environment.TickCount64 + _lockTimeoutMs;

        while (true)
        {
            try
            {
                lockStream.Lock(offset, 1);
                return true;
            }
            catch (IOException)
            {
                if (Environment.TickCount64 >= deadline)
                    return false;
                Thread.Sleep(LockRetryDelayMs);
            }
        }
    }

    private void ReleaseCooperativeLock()
    {
        var lockStream = GetLockTarget();
        long offset = _lockStyle == LockStyle.Inline ? InlineLockOffset : 0;

        try
        {
            lockStream.Unlock(offset, 1);
        }
        catch (IOException)
        {
            // Best-effort unlock — process exit will release it anyway
        }
    }

    private FileStream GetLockTarget()
    {
        if (_lockStyle == LockStyle.Sidecar)
        {
            EnsureLockFile();
            return _lockFileStream!;
        }

        // Inline: lock on the data file itself
        EnsureWriter();
        return _dataStream!;
    }

    private string BuildRotatedPath(string periodKey)
    {
        if (_rotation == RotationPeriod.None)
            return _basePath;

        string dir = Path.GetDirectoryName(_basePath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(_basePath);
        string ext = Path.GetExtension(_basePath);
        return Path.Combine(dir, $"{name}-{periodKey}{ext}");
    }

    private string GetPeriodKey(DateTime utc) => _rotation switch
    {
        RotationPeriod.Hourly => utc.ToString("yyyy-MM-dd-HH"),
        RotationPeriod.Daily => utc.ToString("yyyy-MM-dd"),
        RotationPeriod.Monthly => utc.ToString("yyyy-MM"),
        _ => ""
    };
}

public enum SharingMode
{
    /// <summary>Default. Cross-process byte-range lock for multi-writer safety.</summary>
    Cooperative,
    /// <summary>No cross-process locking. Caller asserts sole writer.</summary>
    Exclusive
}

public enum FlushMode
{
    /// <summary>Default. Flush to disk after every batch write.</summary>
    Batch,
    /// <summary>Defer flush to pipeline shutdown. Only valid in Exclusive mode.</summary>
    Shutdown
}

public enum LockStyle
{
    /// <summary>Default. Lock a sentinel byte range on the data file itself. No extra files.</summary>
    Inline,
    /// <summary>Lock byte 0 on a companion .lock file. Safest across all NAS/SMB implementations.</summary>
    Sidecar
}