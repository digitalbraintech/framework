namespace DigitalBrain.Silo;

using DigitalBrain.Core;

// Kernel task grain contract (kernel-owned). Task synapses are core protocol (used by MCP/INO); kernel impl here.
public interface IKernelTask : INeuron, IHandle<RunKernelTask>, IHandle<CancelKernelTask>
{
    Task<KernelTaskInfo> GetInfoAsync();
}
