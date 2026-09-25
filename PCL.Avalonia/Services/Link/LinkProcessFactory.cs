using System.Diagnostics;
using System.Text;

namespace PCL.Avalonia.Services.Link;

public sealed class LinkProcessFactory : ILinkProcessFactory
{
    public IEasyTierProcess StartCore(string executablePath)
    {
        return new EasyTierCoreProcess(executablePath);
    }

    private sealed class EasyTierCoreProcess : IEasyTierProcess
    {
        private readonly string _executablePath;
        private Process? _process;
        private readonly StringBuilder _logHistory = new();

        public EasyTierCoreProcess(string executablePath)
        {
            _executablePath = executablePath;
        }

        public event Action<string>? LogLine;

        public bool IsRunning => _process is not null && !_process.HasExited;

        public string RecentLog => _logHistory.ToString();

        public void Start(IReadOnlyList<string> arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            _process = Process.Start(startInfo)
                       ?? throw new InvalidOperationException("联机模块进程启动失败");
            _process.OutputDataReceived += OnOutputData;
            _process.ErrorDataReceived += OnErrorData;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public void Kill()
        {
            if (_process is null)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(2000);
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        public void Dispose() => Kill();

        private void OnOutputData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is not null)
            {
                AppendLog(e.Data);
            }
        }

        private void OnErrorData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is not null)
            {
                AppendLog(e.Data);
            }
        }

        private void AppendLog(string line)
        {
            if (_logHistory.Length > 4096)
            {
                _logHistory.Clear();
            }

            _logHistory.AppendLine(line);
            LogLine?.Invoke(line);
        }
    }
}

public sealed class LinkCliRunner : ILinkCliRunner
{
    private readonly string _cliPath;

    public LinkCliRunner(string cliPath)
    {
        _cliPath = cliPath;
    }

    public async Task<string> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _cliPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("联机模块 CLI 启动失败");
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                error.AppendLine(e.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        });
        var completed = await Task.Run(
            () => process.WaitForExit((int)timeout.TotalMilliseconds),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!completed)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("联机模块 CLI 调用超时");
        }

        var result = output.ToString();
        if (string.IsNullOrWhiteSpace(result) && !string.IsNullOrWhiteSpace(error.ToString()))
        {
            throw new InvalidOperationException("联机模块 CLI 调用失败：" + error);
        }

        return result;
    }
}
