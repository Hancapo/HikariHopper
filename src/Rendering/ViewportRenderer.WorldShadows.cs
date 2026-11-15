using System.Numerics;
using System.Runtime.InteropServices;
using HikariHopper.Rendering.World;
using WebGpuSharp;

namespace HikariHopper.Rendering;

// The sun's shadow over the world (RedFury's world shadows and schedule, copied), into the viewer's shadow map and read
// by light.wgsl as a drawable's is. The cascades follow the view, so any move makes them all stale, and casting every
// caster into every cascade each frame would cost most of a moving frame: while stale, the nearest cascade is cast
// every frame, the second every other and the far pair every fourth, in turn. A cascade waiting its turn is read with
// the view it was cast with, moved to the camera as it is now, so it stays right; slices that turned out of it only lose
// distant shadow for three frames at most.
internal sealed unsafe partial class ViewportRenderer
{
    private readonly ShadowCascade[] shadowRendered = new ShadowCascade[WorldShadowCascades.Count];
    private readonly Vector3[] shadowRenderedOrigin = new Vector3[WorldShadowCascades.Count];
    private readonly bool[] shadowStale = new bool[WorldShadowCascades.Count];
    private readonly bool[] shadowRenderNow = new bool[WorldShadowCascades.Count];

    // The cascades whose casters' lists came from the latest visibility update.
    private readonly bool[] cascadeListsValid = new bool[WorldShadowCascades.Count];
    private ShadowCascade[] worldCascades = new ShadowCascade[WorldShadowCascades.Count];
    private bool shadowsValid, worldShadowDirty = true;
    private long shadowSlot;
    private (Vector3 Eye, Vector3 Forward, int Width, int Height, Vector3 Sun, int Resolution) worldShadowView;
    private WebGpuSharp.Texture? shadowCastInto;

    /// <summary>How many cascades have been cast since the view was made.</summary>
    internal long CascadesCast { get; private set; }

    /// <summary>
    /// Fits the world's cascades to the view and picks those cast this frame (from <see cref="WriteLight"/>): the light
    /// reads each at the view it was cast with, moved to where the camera is now.
    /// </summary>
    private void FitWorldShadows()
    {
        Vector3 eye = Camera.Eye, forward = Camera.Forward;
        var view = (eye, forward, width, height, key.Direction, ShadowResolution);
        // A new view, or what's covered or uploaded changing, makes every cascade stale; a new map holds nothing.
        if (worldShadowView != view || worldVisibilityDirty)
            worldShadowDirty = true;
        worldShadowView = view;
        if (!ReferenceEquals(shadowCastInto, shadowMap))
        {
            shadowCastInto = shadowMap;
            shadowsValid = false;
        }
        ShadowCascade[] fitted = WorldShadowCascades.Compute(eye, forward, Aspect, key.Direction, ShadowResolution);
        if (worldShadowDirty)
        {
            Array.Fill(shadowStale, true);
            worldShadowDirty = false;
        }
        long slot = shadowSlot++;
        Span<float> splits = stackalloc float[WorldShadowCascades.Count];
        Span<float> texels = stackalloc float[WorldShadowCascades.Count];
        Span<float> depths = stackalloc float[WorldShadowCascades.Count];
        for (int c = 0; c < WorldShadowCascades.Count; c++)
        {
            shadowRenderNow[c] = !shadowsValid || shadowStale[c] && WorldShadowCascades.Due(c, slot);
            ShadowCascade sampled;
            if (shadowRenderNow[c])
            {
                shadowRendered[c] = fitted[c];
                shadowRenderedOrigin[c] = eye;
                shadowStale[c] = false;
                sampled = fitted[c];
                // Cast from the world itself: the cascade's view after the move to the camera it was fitted for.
                Matrix4x4 cast = Matrix4x4.CreateTranslation(-eye) * fitted[c].ViewProjection;
                queue.WriteBuffer(shadowViewBuffers[c], 0, MemoryMarshal.AsBytes(new ReadOnlySpan<Matrix4x4>(in cast)));
            }
            else
                sampled = shadowRendered[c].Relocate(eye - shadowRenderedOrigin[c]);
            shadowMatrices[c] = sampled.ViewProjection;
            splits[c] = fitted[c].Split;
            texels[c] = sampled.TexelSize;
            depths[c] = sampled.Depth;
        }
        shadowsValid = true;
        worldCascades = fitted;
        shadowSplits = new Vector4(splits[0], splits[1], splits[2], splits[3]);
        shadowTexels = new Vector4(texels[0], texels[1], texels[2], texels[3]);
        shadowDepths = new Vector4(depths[0], depths[1], depths[2], depths[3]);
    }

    /// <summary>No world shadow this frame (they're off, or the sun is down): every cascade is cast again when there is.</summary>
    private void SkipWorldShadows()
    {
        Array.Clear(shadowRenderNow);
        shadowsValid = false;
    }

    /// <summary>The casters of each cascade due this frame into its layer of the shadow map.</summary>
    private void DrawWorldShadows(CommandEncoder encoder)
    {
        for (int c = 0; c < WorldShadowCascades.Count; c++)
        {
            if (!shadowRenderNow[c])
                continue;
            RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments = [],
                DepthStencilAttachment = new RenderPassDepthStencilAttachment
                {
                    View = shadowLayers[c],
                    DepthLoadOp = LoadOp.Clear,
                    DepthStoreOp = StoreOp.Store,
                    DepthClearValue = 1,
                },
            });
            DrawWorldPass(pass, ShadowBundle + c);
            pass.End();
            CascadesCast++;
        }
    }
}
