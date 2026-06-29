using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc;
using Google.Protobuf;
using Grpc.Net.Client;
using Spectre.Console;

namespace DigitalBrain.Cli.Commands;

// Dev hot-loop: publish+install a pack .cs into the ALREADY-RUNNING cluster — no kernel recompile/restart.
public static class AuthorCommand
{
    public static async Task<int> RunAsync(string file, bool watch, string gatewayUrl)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            AnsiConsole.MarkupLine("[yellow]Usage:[/] " + Markup.Escape("author <file.cs> [--watch] [--gateway URL]"));
            return 1;
        }

        if (!File.Exists(file))
        {
            AnsiConsole.MarkupLine($"[red]File not found:[/] {Markup.Escape(file)}");
            return 1;
        }

        try
        {
            await PublishAndInstallAsync(file, gatewayUrl);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]author failed:[/] {Markup.Escape(ex.Message)}");
            if (!watch) return 1;
        }

        if (!watch) return 0;

        var dir = Path.GetDirectoryName(Path.GetFullPath(file))!;
        using var watcher = new FileSystemWatcher(dir, Path.GetFileName(file))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        AnsiConsole.MarkupLine($"[green]Watching[/] {Markup.Escape(file)} (Ctrl+C to stop)…");
        var gate = new SemaphoreSlim(1, 1);
        watcher.Changed += async (_, _) =>
        {
            if (!await gate.WaitAsync(0)) return;
            try
            {
                await Task.Delay(150); // let the editor finish writing
                await PublishAndInstallAsync(file, gatewayUrl);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]author failed:[/] {Markup.Escape(ex.Message)}");
            }
            finally { gate.Release(); }
        };

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (OperationCanceledException) { }
        AnsiConsole.MarkupLine("[grey]stopped watching[/]");
        return 0;
    }

    private static async Task PublishAndInstallAsync(string file, string gatewayUrl)
    {
        var code = await File.ReadAllTextAsync(file);
        var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        const string version = "1.0.0-dev";

        var (privateKey, publicKey) = PackSignatureVerifier.GenerateKeyPair();
        var signed = PackSignatureVerifier.SignPack(
            new NeuroPack(name, version, "dev", false, 0.0, code, "Authored via dbt author"),
            privateKey, publicKey);

        using var channel = GrpcChannel.ForAddress(gatewayUrl);
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        var publish = JsonSerializer.Serialize(new
        {
            PackName = name, Version = version, Code = code, OwnerId = "dev",
            IsPrivate = false, CommissionRate = 0.0, Description = "Authored via dbt author",
            AuthorPublicKeyBase64 = signed.AuthorPublicKeyBase64, SignatureBase64 = signed.SignatureBase64
        });
        await client.SendAsync(new SynapseEnvelope
        {
            CorrelationId = "author-pub-" + name,
            TypeName = nameof(PublishToMarketplace),
            Payload = ByteString.CopyFromUtf8(publish)
        });

        var install = JsonSerializer.Serialize(new { PackName = name, Version = version, BuyerId = "dev" });
        await client.SendAsync(new SynapseEnvelope
        {
            CorrelationId = "author-inst-" + name,
            TypeName = nameof(InstallFromMarketplace),
            Payload = ByteString.CopyFromUtf8(install)
        });

        AnsiConsole.MarkupLine($"[green]Published + installed[/] {name}@{version} → live");
    }
}
