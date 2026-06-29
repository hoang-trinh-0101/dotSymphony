using System.CommandLine;
using System.Net;
using OpenSymphony.Control;
using OpenSymphony.Domain;
using OpenSymphony.Gateway;

namespace OpenSymphony.Cli;

/// <summary>
/// Serve the local control-plane demo stream.
/// ht: Port of older/crates/opensymphony-cli/src/lib.rs Daemon command handler.
/// </summary>
public static class DaemonCommand
{
    public static Command Create()
    {
        var command = new Command("daemon", "Serve the local control-plane demo stream");

        var bindOption = new Option<string>("--bind", () => "127.0.0.1:2468", "Socket address for the local control-plane HTTP and SSE server");
        var sampleIntervalOption = new Option<int>("--sample-interval-ms", () => 1200, "Milliseconds between sample snapshot updates");

        command.Add(bindOption);
        command.Add(sampleIntervalOption);

        command.SetHandler(async (bind, sampleInterval) =>
        {
            var exitCode = await RunDaemonAsync(bind, sampleInterval);
            Environment.Exit(exitCode);
        }, bindOption, sampleIntervalOption);

        return command;
    }

    static async Task<int> RunDaemonAsync(string bind, int sampleIntervalMs)
    {
        try
        {
            if (!IPEndPoint.TryParse(bind, out var bindAddress))
            {
                Console.Error.WriteLine($"Invalid bind address: {bind}");
                return 1;
            }

            Console.WriteLine($"Starting control-plane demo server on {bind}");
            Console.WriteLine($"Sample interval: {sampleIntervalMs}ms");

            // Create a simple demo snapshot store
            var initialSnapshot = GenerateDemoSnapshot(0);
            var snapshotStore = new SnapshotStore(initialSnapshot);

            // TODO: Implement proper GatewayState initialization
            var gatewayState = GatewayState.Create(snapshotStore);

            // Start gateway server
            var server = new GatewayServer(snapshotStore);
            // TODO: Implement proper server startup
            // await server.StartAsync(CancellationToken.None);

            Console.WriteLine($"Control-plane server listening on {bind}");
            Console.WriteLine("Press Ctrl+C to stop");

            // Update snapshots periodically
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(sampleIntervalMs));
            var sequence = 0UL;

            try
            {
                while (!Console.KeyAvailable)
                {
                    await timer.WaitForNextTickAsync();

                    // Generate demo snapshot
                    var snapshot = GenerateDemoSnapshot(sequence++);
                    snapshotStore.Publish(snapshot);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            finally
            {
                Console.WriteLine("Shutting down control-plane server");
                // TODO: Implement proper server shutdown
                // await server.StopAsync(CancellationToken.None);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Daemon failed: {ex}");
            return 1;
        }
    }

    static ControlPlaneDaemonSnapshot GenerateDemoSnapshot(ulong sequence)
    {
        var now = DateTimeOffset.UtcNow;

        return new ControlPlaneDaemonSnapshot(
            now,
            new ControlPlaneDaemonStatus(
                ControlPlaneDaemonState.Ready,
                now,
                "/demo/workspace",
                $"poll=30000ms, running=0, retry_queue=0"
            ),
            new ControlPlaneAgentServerStatus(
                true,
                "http://127.0.0.1:8000",
                0,
                "ready"
            ),
            ControlPlaneMemoryServerStatus.Default,
            new ControlPlaneMetricsSnapshot(
                0,
                0,
                0,
                0,
                0,
                0,
                0
            ),
            new List<ControlPlaneIssueSnapshot>(),
            new List<ControlPlaneRecentEvent>()
        );
    }
}