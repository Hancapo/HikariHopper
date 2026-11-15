using System.Runtime.InteropServices;
using HikariHopper.Rendering.World;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;

namespace HikariHopper.Rendering;

// The world's local lights (RedFury's WebGpuRenderer.LocalLights, copied): the streamer binds the cut's static lights,
// and each frame the 128 nearest the camera that reach the screen go to the GPU with the screen tiles each touches,
// which rdr2_world.wgsl's local_irradiance walks.
internal sealed unsafe partial class ViewportRenderer
{
    private LocalLight[] localLights = [];
    private readonly LocalLightFrame localLightFrame = new();
    private Buffer? localLightBuffer, localLightTiles;

    /// <summary>The world's lights, copied: each frame draws at most 128, nearest the camera; none removes them all.</summary>
    public void SetLocalLights(IReadOnlyList<LocalLight> lights) => localLights = [.. lights];

    /// <summary>The lights drawn in the last frame.</summary>
    internal int LocalLightCount => localLightFrame.Count;

    // This frame's lights and tiles, relative to the camera, before the world is drawn.
    private void WriteLocalLights()
    {
        if (localLightBuffer is null || localLightTiles is null)
            return;
        localLightFrame.Update(localLights, Camera.Eye, Camera.Forward, WorldShadowCascades.Near, Aspect);
        if (localLightFrame.Count > 0)
            queue.WriteBuffer(localLightBuffer, 0, localLightFrame.LightData[..(localLightFrame.Count * 64)]);
        queue.WriteBuffer(localLightTiles, 0, MemoryMarshal.AsBytes(localLightFrame.TileMasks));
    }
}
