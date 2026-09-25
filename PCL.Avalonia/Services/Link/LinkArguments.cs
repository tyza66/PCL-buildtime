namespace PCL.Avalonia.Services.Link;

public static class LinkArguments
{
    public static IReadOnlyList<string> BuildCoreArguments(
        LinkSession session,
        LinkLatencyMode latencyMode)
    {
        var arguments = new List<string>
        {
            $"--network-name={session.NetworkName}",
            $"--network-secret={session.NetworkSecret}",
            "--listeners",
            session.ListenersPort.ToString(),
            "--rpc-portal",
            session.RpcPort.ToString(),
            "--private-mode",
            "true",
            "-i",
            "10.114.114.114",
            "--hostname=" + session.Hostname,
        };

        if (session.IsServer)
        {
            arguments.Add("--tcp-whitelist=" + session.ServerPort);
            arguments.Add("--udp-whitelist=" + session.ServerPort);
        }
        else
        {
            arguments.Add("-d");
            arguments.Add("--tcp-whitelist=0");
            arguments.Add("--udp-whitelist=0");
            AddPortForward(arguments, session.ClientPort, session.ServerPort);
        }

        foreach (var peer in session.Peers)
        {
            arguments.Add("-p=" + peer);
        }

        if (latencyMode == LinkLatencyMode.PreferredLowLatency)
        {
            arguments.Add("--latency-first");
        }

        return arguments;
    }

    private static void AddPortForward(IList<string> arguments, int clientPort, int serverPort)
    {
        arguments.Add($"--port-forward tcp://[::1]:{clientPort}/10.114.114.114:{serverPort}");
        arguments.Add($"--port-forward udp://[::1]:{clientPort}/10.114.114.114:{serverPort}");
        arguments.Add($"--port-forward tcp://127.0.0.1:{clientPort}/10.114.114.114:{serverPort}");
        arguments.Add($"--port-forward udp://127.0.0.1:{clientPort}/10.114.114.114:{serverPort}");
    }
}
