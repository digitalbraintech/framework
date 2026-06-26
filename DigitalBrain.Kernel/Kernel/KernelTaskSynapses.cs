namespace DigitalBrain.Kernel;

using DigitalBrain.Core;

// Kernel task grain contract (kernel-owned). Task messages are now universal core protocol (TaskCreated etc.).
// Kernel layer owns the durable execution grain.
public interface IKernelTask : INeuron, IHandle<RunTask>, IHandle<CancelTask>
{
    Task<TaskInfo> GetInfoAsync();
}
