namespace HikariHopper.Gpu;

/// <summary>
/// One command of a render bundle: what it sets or draws, and its arguments.
/// </summary>
internal readonly record struct BundleCommand(BundleCommand.Kinds Kind, object? Target, ulong First, ulong Second, uint A = 0, uint B = 0, uint C = 0, int D = 0)
{
    internal enum Kinds : byte
    {
        Pipeline,
        BindGroup,
        VertexBuffer,
        IndexBuffer,
        Draw,
        DrawIndexed,
        DrawIndexedIndirect,
    }
}

/// <summary>A render bundle's commands while they're recorded.</summary>
internal sealed class BundleRecording(Instance instance)
{
    internal Instance Instance { get; } = instance;

    internal List<BundleCommand> Commands { get; } = [];
}

/// <summary>
/// Records a render bundle; used up by <see cref="Finish"/>. A bundle keeps its commands and, executed in a pass, replays
/// them into the pass's command list, with the barriers and root arguments the pass's own draws get.
/// </summary>
public readonly struct RenderBundleEncoder
{
    private readonly BundleRecording recording;

    internal RenderBundleEncoder(BundleRecording recording) => this.recording = recording;

    public void SetPipeline(RenderPipeline pipeline) => recording.Commands.Add(new(BundleCommand.Kinds.Pipeline, pipeline, 0, 0));

    public void SetBindGroup(uint index, BindGroup group) => recording.Commands.Add(new(BundleCommand.Kinds.BindGroup, group, 0, 0, index));

    public void SetVertexBuffer(uint slot, Buffer buffer, ulong offset = 0, ulong size = ulong.MaxValue) =>
        recording.Commands.Add(new(BundleCommand.Kinds.VertexBuffer, buffer, offset, size == ulong.MaxValue ? buffer.GetSize() - offset : size, slot));

    public void SetIndexBuffer(Buffer buffer, IndexFormat format, ulong offset = 0, ulong size = ulong.MaxValue) =>
        recording.Commands.Add(new(BundleCommand.Kinds.IndexBuffer, buffer, offset, size == ulong.MaxValue ? buffer.GetSize() - offset : size, (uint)format));

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) =>
        recording.Commands.Add(new(BundleCommand.Kinds.Draw, null, firstVertex, firstInstance, vertexCount, instanceCount));

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) =>
        recording.Commands.Add(new(BundleCommand.Kinds.DrawIndexed, null, firstIndex, firstInstance, indexCount, instanceCount, 0, baseVertex));

    public void DrawIndexedIndirect(Buffer indirect, ulong offset) => recording.Commands.Add(new(BundleCommand.Kinds.DrawIndexedIndirect, indirect, offset, 0));

    public RenderBundle Finish() => new(recording);
}

public sealed class RenderBundle : GpuObject
{
    private readonly Instance instance;

    internal RenderBundle(BundleRecording recording)
    {
        instance = recording.Instance;
        Commands = [.. recording.Commands];
        instance.Count("render bundles", 1);
    }

    internal BundleCommand[] Commands { get; }

    private protected override void Release() => instance.Count("render bundles", -1);
}

internal sealed partial class Recorder
{
    /// <summary>
    /// Replays the bundles into the pass, each from nothing bound, as WebGPU runs them; the pass has nothing bound after
    /// them either.
    /// </summary>
    internal void ExecuteBundles(ReadOnlySpan<RenderBundle> bundles)
    {
        foreach (RenderBundle bundle in bundles)
        {
            ResetBindings(computing: false);
            foreach (BundleCommand command in bundle.Commands)
                switch (command.Kind)
                {
                    case BundleCommand.Kinds.Pipeline:
                        SetPipeline((RenderPipeline)command.Target!);
                        break;
                    case BundleCommand.Kinds.BindGroup:
                        SetBindGroup(command.A, (BindGroup)command.Target!, []);
                        break;
                    case BundleCommand.Kinds.VertexBuffer:
                        SetVertexBuffer(command.A, ((Buffer)command.Target!).Life, command.First, command.Second);
                        break;
                    case BundleCommand.Kinds.IndexBuffer:
                        SetIndexBuffer(((Buffer)command.Target!).Life, (IndexFormat)command.A, command.First, command.Second);
                        break;
                    case BundleCommand.Kinds.Draw:
                        Draw(command.A, command.B, (uint)command.First, (uint)command.Second);
                        break;
                    case BundleCommand.Kinds.DrawIndexed:
                        DrawIndexed(command.A, command.B, (uint)command.First, command.D, (uint)command.Second);
                        break;
                    case BundleCommand.Kinds.DrawIndexedIndirect:
                        DrawIndexedIndirect(((Buffer)command.Target!).Life, command.First);
                        break;
                }
        }
        ResetBindings(computing: false);
    }
}
