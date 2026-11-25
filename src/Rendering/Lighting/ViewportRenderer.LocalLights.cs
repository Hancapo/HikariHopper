using System.Runtime.InteropServices;
using HikariHopper.Rendering.World;
using RedFury.World;
using HikariHopper.Gpu;
using Buffer = HikariHopper.Gpu.Buffer;

namespace HikariHopper.Rendering;

// The local lights (RedFury's WebGpuRenderer.LocalLights, copied): in a world, the cut's static lights the streamer
// binds; in the viewer, the model's own, at the viewer's hour. Each frame the 512 nearest the camera that reach the
// screen go to the GPU with the screen tiles each touches, which Scene/light.wgsl's lighting walks (Rdr2/material.wgsl's
// local_irradiance for RDR2's materials).
internal sealed unsafe partial class ViewportRenderer
{
    private LocalLight[] localLights = [];
    private readonly LocalLightFrame localLightFrame = new();
    private Buffer? localLightBuffer, localLightTiles;

    // The model's lights, in its own coordinates, and those lit at the hour last drawn.
    private IReadOnlyList<WorldLightDefinition> modelLights = [];
    private (int Hour, LocalLight[] Lights)? modelLit;

    /// <summary>The world's lights, copied: each frame draws at most 512, nearest the camera; none removes them all.</summary>
    public void SetLocalLights(IReadOnlyList<LocalLight> lights) => localLights = [.. lights];

    /// <summary>The lights drawn in the last frame.</summary>
    internal int LocalLightCount => localLightFrame.Count;

    // The shown model's lights that are on at the viewer's hour, where they are in it.
    private LocalLight[] ModelLights()
    {
        int hour = Math.Clamp((int)MathF.Floor(Hours), 0, 23);
        if (modelLit is { } lit && lit.Hour == hour)
            return lit.Lights;
        var lights = new List<LocalLight>();
        foreach (WorldLightDefinition definition in modelLights)
            if (WorldLightBinding.ActiveAt(definition, hour) && WorldLightBinding.ToLocal(definition, System.Numerics.Matrix4x4.Identity) is { } light)
                lights.Add(light);
        modelLit = (hour, [.. lights]);
        return modelLit.Value.Lights;
    }

    // This frame's lights and tiles, relative to where the scene is drawn from, before anything lit is drawn.
    private void WriteLocalLights()
    {
        if (localLightBuffer is null || localLightTiles is null)
            return;
        IReadOnlyList<LocalLight> lights = worldShown ? localLights : surfaceVertices is not null ? ModelLights() : [];
        localLightFrame.Update(lights, Camera.Eye, Camera.Forward, worldShown ? WorldShadowCascades.Near : OrbitCamera.NearPlane, Aspect,
            worldShown ? Camera.Eye : origin);
        if (localLightFrame.Count > 0)
            queue.WriteBuffer(localLightBuffer, 0, localLightFrame.LightData[..(localLightFrame.Count * 64)]);
        queue.WriteBuffer(localLightTiles, 0, MemoryMarshal.AsBytes(localLightFrame.TileMasks));
    }
}
